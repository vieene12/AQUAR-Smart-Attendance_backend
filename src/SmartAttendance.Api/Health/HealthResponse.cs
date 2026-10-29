namespace SmartAttendance.Api.Health;

public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset TimestampUtc);
