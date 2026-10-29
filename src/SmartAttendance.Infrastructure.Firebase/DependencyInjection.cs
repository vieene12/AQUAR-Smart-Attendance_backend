using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Api.Gax;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using SmartAttendance.Application.Authentication;
using SmartAttendance.Application.Accounts;
using SmartAttendance.Infrastructure.Firebase.Accounts;
using SmartAttendance.Infrastructure.Firebase.Authentication;
using SmartAttendance.Application.Reports;
using SmartAttendance.Infrastructure.Firebase.Reports;

namespace Microsoft.Extensions.DependencyInjection;

public static class FirebaseDependencyInjection
{
    public static IServiceCollection AddFirebaseInfrastructure(
        this IServiceCollection services,
        string? projectId,
        string? authEmulatorHost,
        string? firestoreEmulatorHost)
    {
        services.AddSingleton(new FirebaseRuntimeSettings(
            projectId,
            authEmulatorHost,
            firestoreEmulatorHost));
        services.AddSingleton(CreateFirebaseApp);
        services.AddSingleton(static provider =>
            FirebaseAuth.GetAuth(provider.GetRequiredService<FirebaseApp>()));
        services.AddSingleton(CreateFirestoreDb);
        services.AddSingleton<IFirebaseIdTokenVerifier, FirebaseAdminIdTokenVerifier>();
        services.AddSingleton<IUserProfileReader, FirestoreUserProfileReader>();
        services.AddSingleton<IAccountStore, FirebaseAccountStore>();
        services.AddSingleton<IAttendanceReportReader, FirestoreAttendanceReportReader>();
        services.AddSingleton<IReportEmailRequestStore, FirestoreReportEmailRequestStore>();
        return services;
    }

    private static FirebaseApp CreateFirebaseApp(IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<FirebaseRuntimeSettings>();
        var projectId = GetRequiredProjectId(settings.ProjectId);

        try
        {
            var credential = string.IsNullOrWhiteSpace(settings.AuthEmulatorHost)
                ? GoogleCredential.GetApplicationDefault()
                : GoogleCredential.FromAccessToken("firebase-auth-emulator");
            return FirebaseApp.Create(
                new AppOptions
                {
                    ProjectId = projectId,
                    Credential = credential,
                },
                $"smart-attendance-{Guid.NewGuid():N}");
        }
        catch (InvalidOperationException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Firebase credentials are unavailable.",
                exception);
        }
    }

    private static FirestoreDb CreateFirestoreDb(IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<FirebaseRuntimeSettings>();
        var projectId = GetRequiredProjectId(settings.ProjectId);

        try
        {
            return new FirestoreDbBuilder
            {
                ProjectId = projectId,
                EmulatorDetection = string.IsNullOrWhiteSpace(settings.FirestoreEmulatorHost)
                    ? EmulatorDetection.None
                    : EmulatorDetection.EmulatorOnly,
            }.Build();
        }
        catch (InvalidOperationException exception)
        {
            throw new FirebaseServiceUnavailableException(
                "Firestore credentials are unavailable.",
                exception);
        }
    }

    private static string GetRequiredProjectId(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new FirebaseServiceUnavailableException(
                "Firebase ProjectId is not configured.");
        }

        return projectId;
    }

    private sealed record FirebaseRuntimeSettings(
        string? ProjectId,
        string? AuthEmulatorHost,
        string? FirestoreEmulatorHost);
}
