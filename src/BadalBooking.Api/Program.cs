var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Simple liveness check: proves the API process is up.
// (The real /health endpoint that also checks the database comes in step 3.1.)
app.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

app.Run();

// Makes the auto-generated Program class visible to the test project,
// so tests can start the whole API in memory with WebApplicationFactory.
public partial class Program { }
