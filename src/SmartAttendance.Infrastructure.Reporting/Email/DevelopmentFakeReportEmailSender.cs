using SmartAttendance.Application.Reports;

namespace SmartAttendance.Infrastructure.Reporting.Email;

public sealed class DevelopmentFakeReportEmailSender : IReportEmailSender
{
    private readonly object _gate = new();
    private readonly List<ReportEmailMessage> _messages = [];

    public IReadOnlyList<ReportEmailMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages
                    .Select(Clone)
                    .ToArray();
            }
        }
    }

    public Task<ReportEmailProviderResult> SendAsync(
        ReportEmailMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = Clone(message);
        lock (_gate)
        {
            _messages.Add(captured);
        }

        return Task.FromResult(
            new ReportEmailProviderResult($"fake-{message.RequestId}"));
    }

    private static ReportEmailMessage Clone(ReportEmailMessage message) =>
        message with { AttachmentBytes = message.AttachmentBytes.ToArray() };
}
