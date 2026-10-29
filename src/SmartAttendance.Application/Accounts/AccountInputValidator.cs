using System.Text.RegularExpressions;
using SmartAttendance.Domain.Accounts;

namespace SmartAttendance.Application.Accounts;

public static partial class AccountInputValidator
{
    public const int MaximumFullNameLength = 100;
    public const int MaximumEmailLength = 254;
    public const int MinimumPasswordLength = 6;
    public const int MaximumPageSize = 100;

    public static CreateAccountCommand Validate(CreateAccountCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var fullName = ValidateFullName(command.FullName);
        var email = ValidateEmail(command.Email);
        if (string.IsNullOrWhiteSpace(command.Password) ||
            command.Password.Length < MinimumPasswordLength)
        {
            throw new AccountValidationException(
                "Password must contain at least 6 characters.");
        }

        if (command.Role is not (AccountRole.Employee or AccountRole.Manager))
        {
            throw new AccountValidationException(
                "Role must be employee or manager.");
        }

        ValidateHourlyRate(command.HourlyRate);
        return command with
        {
            FullName = fullName,
            Email = email,
        };
    }

    public static UpdateAccountCommand Validate(UpdateAccountCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var fullName = ValidateFullName(command.FullName);
        ValidateHourlyRate(command.HourlyRate);
        return command with { FullName = fullName };
    }

    public static ChangeAccountStatusCommand Validate(ChangeAccountStatusCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Status is not (AccountStatus.Active or AccountStatus.Locked))
        {
            throw new AccountValidationException(
                "Status must be active or locked.");
        }

        return command;
    }

    public static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new AccountValidationException(
                $"Page size must be between 1 and {MaximumPageSize}.");
        }
    }

    private static string ValidateFullName(string value)
    {
        var fullName = value?.Trim() ?? string.Empty;
        if (fullName.Length is 0 or > MaximumFullNameLength)
        {
            throw new AccountValidationException(
                "Full name must contain between 1 and 100 characters.");
        }

        return fullName;
    }

    private static string ValidateEmail(string value)
    {
        var email = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (email.Length is 0 or > MaximumEmailLength ||
            !EmailPattern().IsMatch(email))
        {
            throw new AccountValidationException("Email is invalid.");
        }

        return email;
    }

    [GeneratedRegex(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    private static void ValidateHourlyRate(long hourlyRate)
    {
        if (hourlyRate < 0)
        {
            throw new AccountValidationException(
                "Hourly rate must be a non-negative integer.");
        }
    }
}
