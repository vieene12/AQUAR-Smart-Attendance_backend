using SmartAttendance.Application.Reports;

namespace SmartAttendance.Api.Reports;

public sealed partial class ReportEmailOperationLogger(
    ILogger<ReportEmailOperationLogger> logger) : IReportEmailOperationLogger
{
    public void StageCompleted(string stage, long elapsedMilliseconds) =>
        LogCompleted(logger, stage, elapsedMilliseconds);

    public void StageFailed(
        string stage,
        long elapsedMilliseconds,
        string failureType,
        bool canceled) =>
        LogFailed(logger, stage, elapsedMilliseconds, failureType, canceled);

    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Information,
        Message = "Report email stage {Stage} completed in {ElapsedMilliseconds} ms.")]
    private static partial void LogCompleted(
        ILogger logger,
        string stage,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Warning,
        Message = "Report email stage {Stage} failed after {ElapsedMilliseconds} ms ({FailureType}, canceled: {Canceled}).")]
    private static partial void LogFailed(
        ILogger logger,
        string stage,
        long elapsedMilliseconds,
        string failureType,
        bool canceled);
}
