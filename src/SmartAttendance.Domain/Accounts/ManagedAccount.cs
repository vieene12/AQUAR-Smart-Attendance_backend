namespace SmartAttendance.Domain.Accounts;

public sealed record ManagedAccount(
    string Uid,
    string? FullName,
    string? Email,
    string Role,
    string Status,
    long? HourlyRate);
