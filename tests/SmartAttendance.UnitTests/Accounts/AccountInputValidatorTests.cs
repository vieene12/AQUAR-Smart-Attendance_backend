using SmartAttendance.Application.Accounts;

namespace SmartAttendance.UnitTests.Accounts;

public sealed class AccountInputValidatorTests
{
    [Fact]
    public void CreateNormalizesSupportedFields()
    {
        var result = AccountInputValidator.Validate(
            new CreateAccountCommand(
                "  Nguyễn Văn A  ",
                " EMPLOYEE@EXAMPLE.COM ",
                "secret123",
                "employee",
                50_000));

        Assert.Equal("Nguyễn Văn A", result.FullName);
        Assert.Equal("employee@example.com", result.Email);
    }

    [Theory]
    [InlineData("admin", 1)]
    [InlineData("owner", 1)]
    [InlineData("employee", -1)]
    public void CreateRejectsInvalidRoleOrRate(string role, long hourlyRate)
    {
        Assert.Throws<AccountValidationException>(() =>
            AccountInputValidator.Validate(
                new CreateAccountCommand(
                    "Valid Name",
                    "valid@example.com",
                    "secret123",
                    role,
                    hourlyRate)));
    }

    [Theory]
    [InlineData("missing-domain-dot@example")]
    [InlineData("two@@example.com")]
    [InlineData("space @example.com")]
    public void CreateRejectsEmailOutsideExistingFlutterContract(string email)
    {
        Assert.Throws<AccountValidationException>(() =>
            AccountInputValidator.Validate(
                new CreateAccountCommand(
                    "Valid Name",
                    email,
                    "secret123",
                    "employee",
                    1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ListRejectsUnboundedPageSize(int pageSize)
    {
        Assert.Throws<AccountValidationException>(() =>
            AccountInputValidator.ValidatePageSize(pageSize));
    }
}
