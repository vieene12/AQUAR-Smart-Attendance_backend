using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.Api.Authentication;

public static class FirebaseSecurityValues
{
    public const string AdminRole = AccountRole.Admin;
    public const string ManagerRole = AccountRole.Manager;
    public const string EmployeeRole = AccountRole.Employee;
    public const string ActiveStatus = AccountStatus.Active;

    public static bool IsValidRole(string role) => AccountRole.IsValid(role);

    public static bool IsValidStatus(string status) => AccountStatus.IsValid(status);
}
