namespace SmartAttendance.UnitTests.FirebaseEmulator;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class FirebaseEmulatorFactAttribute : FactAttribute
{
    public FirebaseEmulatorFactAttribute()
    {
        if (!FirebaseEmulatorTestEnvironment.IsEnabled)
        {
            Skip = FirebaseEmulatorTestEnvironment.SkipReason;
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class FirebaseEmulatorTheoryAttribute : TheoryAttribute
{
    public FirebaseEmulatorTheoryAttribute()
    {
        if (!FirebaseEmulatorTestEnvironment.IsEnabled)
        {
            Skip = FirebaseEmulatorTestEnvironment.SkipReason;
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class FirebaseEmulatorKnownLimitationFactAttribute : FactAttribute
{
    public FirebaseEmulatorKnownLimitationFactAttribute()
    {
        Skip = "Auth Emulator does not reject an existing ID token after the user is disabled, " +
            "even when Firebase Admin verifies it with checkRevoked=true.";
    }
}

internal static class FirebaseEmulatorTestEnvironment
{
    public const string ProjectId = "demo-smart-attendance";
    public const string SkipReason =
        "Set RUN_FIREBASE_EMULATOR_E2E=1 and start Auth/Firestore emulators.";

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("RUN_FIREBASE_EMULATOR_E2E"),
            "1",
            StringComparison.Ordinal);

    public static string GetRequiredLoopbackHost(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate($"http://{value}", UriKind.Absolute, out var uri) ||
            !uri.IsLoopback)
        {
            throw new InvalidOperationException(
                $"{variableName} must identify a loopback emulator without a scheme.");
        }

        return value;
    }
}
