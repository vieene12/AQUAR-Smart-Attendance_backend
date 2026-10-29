namespace SmartAttendance.Infrastructure.Firebase.Configuration;

public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string? ProjectId { get; init; }
}
