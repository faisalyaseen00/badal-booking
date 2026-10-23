namespace BadalBooking.Api.Infrastructure;

// Loads KEY=VALUE lines from the repo's .env file into environment variables,
// so `dotnet run`, `dotnet ef` and tests pick up local secrets automatically.
//
// Rules:
//  - Called only in Development. Production (Render) uses real environment variables.
//  - Never overwrites a variable that is already set: real environment variables always win.
//  - Never logs values.
public static class DotEnv
{
    public static void LoadForDevelopment(string startDirectory)
    {
        var path = FindUpwards(startDirectory, ".env");
        if (path is null) return; // no .env (e.g. CI) is fine

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            // Split on the FIRST "=" only: connection strings contain "=" too.
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"')
                value = value[1..^1]; // remove surrounding quotes (used for the Firebase JSON)

            if (value.Length == 0) continue; // empty slot = "not configured yet"
            if (Environment.GetEnvironmentVariable(key) is not null) continue; // real env var wins

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    // The API runs from src/BadalBooking.Api, but .env lives at the repo root.
    private static string? FindUpwards(string directory, string fileName)
    {
        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
