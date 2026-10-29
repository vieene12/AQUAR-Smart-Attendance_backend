using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.Api.Accounts;

public sealed record AccountResponse(
    string Uid,
    string? FullName,
    string? Email,
    string Role,
    string Status,
    long? HourlyRate)
{
    public static AccountResponse FromDomain(ManagedAccount account) =>
        new(
            account.Uid,
            account.FullName,
            account.Email,
            account.Role,
            account.Status,
            account.HourlyRate);
}

public sealed record AccountPageResponse(
    IReadOnlyList<AccountResponse> Items,
    string? NextPageToken);
