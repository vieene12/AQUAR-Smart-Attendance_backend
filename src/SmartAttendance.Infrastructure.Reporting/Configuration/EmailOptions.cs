namespace SmartAttendance.Infrastructure.Reporting.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const string ResendMode = "Resend";
    public const string FakeMode = "Fake";

    public bool Enabled { get; set; }
    public string Mode { get; set; } = ResendMode;
    public string ApiKey { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Smart Attendance";
    public int TimeoutSeconds { get; set; } = 15;
}
