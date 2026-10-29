using SmartAttendance.Application.Reports;
using System.Globalization;

namespace SmartAttendance.UnitTests.Reports;

public sealed class AttendanceReportEmailServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendsTrustedPdfAndReturnsAcceptedWithoutExposingEmail()
    {
        var sender = new FakeEmailSender();
        var service = CreateService(sender: sender);

        var result = await service.SendAsync(Command(), CancellationToken.None);

        Assert.Equal("accepted", result.Status);
        Assert.Equal("e***@example.test", result.MaskedRecipient);
        Assert.Equal(1, sender.Calls);
        Assert.Equal("employee@example.test", sender.LastMessage?.Recipient);
        Assert.Equal("report.pdf", sender.LastMessage?.AttachmentFileName);
    }

    [Fact]
    public async Task AcceptedIdempotentReplayDoesNotSendTwice()
    {
        var sender = new FakeEmailSender();
        var store = new FakeReportRequestStore();
        var service = CreateService(sender: sender, store: store);

        var first = await service.SendAsync(Command(), CancellationToken.None);
        var second = await service.SendAsync(Command(), CancellationToken.None);

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task TimeoutBecomesUnknownAndCannotBeRetried()
    {
        var sender = new FakeEmailSender
        {
            Exception = new ReportEmailDeliveryUnknownException("timeout"),
        };
        var store = new FakeReportRequestStore();
        var service = CreateService(sender: sender, store: store);

        await Assert.ThrowsAsync<ReportEmailDeliveryUnknownException>(
            () => service.SendAsync(Command(), CancellationToken.None));
        await Assert.ThrowsAsync<ReportRequestConflictException>(
            () => service.SendAsync(Command(), CancellationToken.None));
        Assert.Equal(1, sender.Calls);
        Assert.Equal(ReportRequestStatus.Unknown, store.LastStatus);
    }

    [Fact]
    public async Task ReservationPrecedesAttendanceAndCancellationMarksFailedWithoutSending()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new FakeReportRequestStore();
        var sender = new FakeEmailSender();
        var reader = new CancelDuringAttendanceReader(
            ReportTestData.Source(),
            store,
            cancellation);
        var service = CreateService(reader: reader, sender: sender, store: store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SendAsync(Command(), cancellation.Token));

        Assert.Equal(1, store.ReservationCalls);
        Assert.Equal(1, store.MarkFailedCalls);
        Assert.Equal(ReportRequestStatus.Failed, store.LastStatus);
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task ClientCancellationAfterReservationDoesNotCancelProviderOrAcceptedStatus()
    {
        using var clientCancellation = new CancellationTokenSource();
        var store = new FakeReportRequestStore();
        var sender = new CancelClientOnSendEmailSender(clientCancellation);
        var service = CreateService(sender: sender, store: store);

        var result = await service.SendAsync(Command(), clientCancellation.Token);

        Assert.True(clientCancellation.IsCancellationRequested);
        Assert.Equal("accepted", result.Status);
        Assert.Equal(1, sender.Calls);
        Assert.Equal(1, store.MarkAcceptedCalls);
        Assert.Equal(ReportRequestStatus.Accepted, store.LastStatus);
    }

    [Fact]
    public async Task ProviderRejectionMarksFailedAndReleasesReservation()
    {
        var sender = new FakeEmailSender
        {
            Exception = new ReportEmailUnavailableException("rejected"),
        };
        var store = new FakeReportRequestStore();
        var service = CreateService(sender: sender, store: store);

        await Assert.ThrowsAsync<ReportEmailUnavailableException>(
            () => service.SendAsync(Command(), CancellationToken.None));

        Assert.Equal(1, store.MarkFailedCalls);
        Assert.Equal(ReportRequestStatus.Failed, store.LastStatus);
    }

    [Fact]
    public async Task ProviderCancellationIsPersistedAsUnknown()
    {
        var sender = new FakeEmailSender
        {
            Exception = new OperationCanceledException("provider timeout"),
        };
        var store = new FakeReportRequestStore();
        var service = CreateService(sender: sender, store: store);

        await Assert.ThrowsAsync<ReportEmailDeliveryUnknownException>(
            () => service.SendAsync(Command(), CancellationToken.None));

        Assert.Equal(1, store.MarkUnknownCalls);
        Assert.Equal(ReportRequestStatus.Unknown, store.LastStatus);
    }

    [Fact]
    public async Task SuccessfulRequestLogsOnlyNamedStages()
    {
        var logger = new FakeReportEmailOperationLogger();
        var service = CreateService(logger: logger);

        await service.SendAsync(Command(), CancellationToken.None);

        Assert.Equal(
            [
                "recipient-profile",
                "idempotency-reservation",
                "attendance-read",
                "pdf-generation",
                "email-provider",
                "status-accepted",
            ],
            logger.CompletedStages);
        Assert.Empty(logger.FailedStages);
    }

    [Theory]
    [InlineData("bad-email")]
    [InlineData("")]
    public async Task InvalidRegisteredEmailIsRejectedBeforeReservation(string email)
    {
        var service = CreateService(source: ReportTestData.Source(email));

        await Assert.ThrowsAsync<ReportEmailInvalidException>(
            () => service.SendAsync(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task PdfOverFiveMiBIsRejected()
    {
        var service = CreateService(
            pdf: new FakePdfGenerator(new byte[AttendanceReportEmailService.MaxPdfBytes + 1]));

        await Assert.ThrowsAsync<ReportPayloadTooLargeException>(
            () => service.SendAsync(Command(), CancellationToken.None));
    }

    [Theory]
    [InlineData("week", "2026-10-05", "2026-10-05", "2026-10-11")]
    [InlineData("month", "2026-10-05", "2026-10-01", "2026-10-31")]
    [InlineData("quarter", "2026-10-05", "2026-10-01", "2026-12-31")]
    [InlineData("year", "2026-10-05", "2026-01-01", "2026-12-31")]
    public void PeriodUsesVietnamCalendar(
        string kind,
        string anchor,
        string expectedStart,
        string expectedEnd)
    {
        var period = AttendanceReportEmailService.ParsePeriod(kind, anchor, Now);

        Assert.Equal(
            expectedStart,
            period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(
            expectedEnd,
            period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static AttendanceReportEmailService CreateService(
        EmployeeReportSource? source = null,
        IAttendanceReportReader? reader = null,
        IReportEmailSender? sender = null,
        FakeReportRequestStore? store = null,
        IAttendanceReportPdfGenerator? pdf = null,
        IReportEmailOperationLogger? logger = null) => new(
            reader ?? new FakeReportReader(source ?? ReportTestData.Source()),
            pdf ?? new FakePdfGenerator(),
            store ?? new FakeReportRequestStore(),
            sender ?? new FakeEmailSender(),
            new FakeReportClock(Now),
            logger ?? new FakeReportEmailOperationLogger());

    private static SendAttendanceReportCommand Command() => new(
        "caller-uid",
        "employee-uid",
        "month",
        "2026-10-05",
        "11111111-1111-1111-1111-111111111111");

    private sealed class CancelDuringAttendanceReader(
        EmployeeReportSource source,
        FakeReportRequestStore store,
        CancellationTokenSource cancellation) : IAttendanceReportReader
    {
        public Task<EmployeeReportRecipient> LoadRecipientAsync(
            string employeeUid,
            CancellationToken cancellationToken) => Task.FromResult(
                new EmployeeReportRecipient(source.Uid, source.FullName, source.Email));

        public Task<EmployeeReportSource> LoadAttendanceAsync(
            EmployeeReportRecipient recipient,
            ReportPeriod period,
            CancellationToken cancellationToken)
        {
            Assert.Equal(1, store.ReservationCalls);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(source);
        }
    }

    private sealed class CancelClientOnSendEmailSender(
        CancellationTokenSource clientCancellation) : IReportEmailSender
    {
        public int Calls { get; private set; }

        public Task<ReportEmailProviderResult> SendAsync(
            ReportEmailMessage message,
            CancellationToken cancellationToken)
        {
            Calls++;
            clientCancellation.Cancel();
            Assert.False(cancellationToken.IsCancellationRequested);
            return Task.FromResult(new ReportEmailProviderResult("provider-id"));
        }
    }
}
