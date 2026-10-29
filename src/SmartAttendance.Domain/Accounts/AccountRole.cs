namespace SmartAttendance.Domain.Accounts;

public static class AccountRole
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Employee = "employee";

    public static bool IsValid(string role) => role is Admin or Manager or Employee;
}
