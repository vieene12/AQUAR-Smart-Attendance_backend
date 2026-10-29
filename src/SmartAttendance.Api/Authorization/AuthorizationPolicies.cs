namespace SmartAttendance.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string ActiveUser = "ActiveUser";
    public const string ActiveAdmin = "ActiveAdmin";
    public const string ManagerOrAdmin = "ManagerOrAdmin";
    public const string EmployeeSelf = "EmployeeSelf";
}
