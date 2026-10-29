namespace SmartAttendance.Application.Accounts;

public abstract class AccountManagementException(string message) : Exception(message);

public sealed class AccountValidationException(string message)
    : AccountManagementException(message);

public sealed class AccountNotFoundException(string message)
    : AccountManagementException(message);

public sealed class AccountConflictException(string message)
    : AccountManagementException(message);

public sealed class AccountProtectedException(string message)
    : AccountManagementException(message);

public sealed class AccountSynchronizationException(string message)
    : AccountManagementException(message);
