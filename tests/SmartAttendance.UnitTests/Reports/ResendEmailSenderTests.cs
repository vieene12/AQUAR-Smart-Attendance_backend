using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartAttendance.Application.Reports;
using SmartAttendance.Infrastructure.Reporting.Configuration;
using SmartAttendance.Infrastructure.Reporting.Email;

namespace SmartAttendance.UnitTests.Reports;

public sealed class ResendEmailSenderTests
{
    [Fact]
    public async Task DisabledModeMakesNoHttpRequest()
    {
        var handler = new RecordingHandler();
        var sender = CreateSender(handler, new EmailOptions { Enabled = false });

        await Assert.ThrowsAsync<ReportEmailUnavailableException>(
            () => sender.SendAsync(Message(), CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SendsBearerAndProviderIdWithoutPuttingKeyInPayload()
    {
        var handler = new RecordingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"resend-message-id\"}"),
            },
        };
        var sender = CreateSender(handler, new EmailOptions
        {
            Enabled = true,
            ApiKey = "test-api-key-not-real",
            FromAddress = "reports@example.test",
        });

        var result = await sender.SendAsync(Message(), CancellationToken.None);

        Assert.Equal("resend-message-id", result.ProviderMessageId);
        Assert.Equal("Bearer", handler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal("test-api-key-not-real", handler.Request?.Headers.Authorization?.Parameter);
        Assert.DoesNotContain("test-api-key-not-real", handler.Body, StringComparison.Ordinal);
        Assert.Contains("report.pdf", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderRejectionIsDefinitiveUnavailableFailure()
    {
        var handler = new RecordingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.BadRequest),
        };
        var sender = CreateSender(handler, EnabledOptions());

        await Assert.ThrowsAsync<ReportEmailUnavailableException>(
            () => sender.SendAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task NetworkFailureIsUnknownAndMustNotBeBlindlyRetried()
    {
        var handler = new RecordingHandler
        {
            Exception = new HttpRequestException("test network failure"),
        };
        var sender = CreateSender(handler, EnabledOptions());

        await Assert.ThrowsAsync<ReportEmailDeliveryUnknownException>(
            () => sender.SendAsync(Message(), CancellationToken.None));
    }

    private static ResendEmailSender CreateSender(
        HttpMessageHandler handler,
        EmailOptions options) => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") },
            Options.Create(options),
            NullLogger<ResendEmailSender>.Instance);

    private static EmailOptions EnabledOptions() => new()
    {
        Enabled = true,
        ApiKey = "test-api-key-not-real",
        FromAddress = "reports@example.test",
    };

    private static ReportEmailMessage Message() => new(
        "request-id",
        "employee@example.test",
        "Employee",
        new ReportPeriod(
            ReportPeriodKind.Month,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31)),
        "Subject",
        "Body",
        "report.pdf",
        "%PDF-test"u8.ToArray());

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public HttpResponseMessage Response { get; set; } =
            new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"id\"}"),
            };
        public Exception? Exception { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (Exception is not null) throw Exception;
            Request = request;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }
}
