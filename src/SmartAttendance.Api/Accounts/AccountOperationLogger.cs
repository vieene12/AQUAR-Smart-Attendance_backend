using SmartAttendance.Application.Accounts;

namespace SmartAttendance.Api.Accounts;

public sealed partial class AccountOperationLogger(
    ILogger<AccountOperationLogger> logger) : IAccountOperationLogger
{
    public void SynchronizationFailure(
        string operationStep,
        string targetUid,
        Exception exception) =>
        LogFailure(logger, exception, operationStep, targetUid);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Account synchronization failed at {OperationStep} for UID {TargetUid}.")]
    private static partial void LogFailure(
        ILogger logger,
        Exception exception,
        string operationStep,
        string targetUid);
}
