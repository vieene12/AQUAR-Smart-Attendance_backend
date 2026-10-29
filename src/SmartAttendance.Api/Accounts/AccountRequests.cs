using System.Text.Json.Serialization;

namespace SmartAttendance.Api.Accounts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAccountRequest(
    string FullName,
    string Email,
    string Password,
    string Role,
    long HourlyRate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAccountRequest(string FullName, long HourlyRate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeAccountStatusRequest(string Status);
