using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

namespace BadalBooking.Api.Infrastructure.Firebase;

// Loads the Firebase Admin SDK from environment variables ONLY:
//   Firebase__ProjectId           e.g. badal-booking
//   Firebase__ServiceAccountJson  the whole service-account JSON on one line (never a file in the repo)
//
// Production: both are required, startup stops if missing or mismatched.
// Development/Testing: optional; without them, Firebase features are simply unavailable
// (tests use fakes, step 2.7).
public static class FirebaseSetup
{
    public static void AddFirebase(this WebApplicationBuilder builder)
    {
        var projectId = builder.Configuration["Firebase:ProjectId"];
        var json = builder.Configuration["Firebase:ServiceAccountJson"];

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(json))
        {
            if (builder.Environment.IsProduction())
                throw new InvalidOperationException("Firebase__ProjectId and Firebase__ServiceAccountJson are required in production.");
            return; // not configured locally / in tests
        }

        builder.Services.AddSingleton(_ => CreateApp(projectId, json));
        builder.Services.AddSingleton<IFirebaseTokenVerifier, FirebaseTokenVerifier>();
    }

    private static FirebaseApp CreateApp(string projectId, string json)
    {
        ServiceAccountCredential credential;
        try
        {
            credential = CredentialFactory.FromJson<ServiceAccountCredential>(json);
        }
        catch (Exception)
        {
            // Never include the JSON (it contains the private key) in the error.
            throw new InvalidOperationException("Firebase__ServiceAccountJson is not a valid service-account JSON.");
        }

        // A key from another project would verify tokens for the wrong app.
        if (credential.ProjectId != projectId)
            throw new InvalidOperationException(
                $"Firebase__ServiceAccountJson belongs to project '{credential.ProjectId}', but Firebase__ProjectId is '{projectId}'.");

        // One FirebaseApp per process (the SDK keeps a global default instance).
        return FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
        {
            Credential = credential.ToGoogleCredential(),
            ProjectId = projectId,
        });
    }
}
