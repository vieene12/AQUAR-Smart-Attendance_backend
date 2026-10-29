namespace SmartAttendance.Domain.Accounts;

public static class AccountStatus
{
    public const string Active = "active";
    public const string Locked = "locked";

    public static bool IsValid(string status) => status is Active or Locked;
}
