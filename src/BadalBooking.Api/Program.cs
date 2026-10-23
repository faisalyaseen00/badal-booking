using BadalBooking.Api.Data;
using BadalBooking.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Local secrets live in the git-ignored .env file at the repo root.
// Only loaded in Development; in production every value comes from real environment variables.
if (builder.Environment.IsDevelopment())
{
    DotEnv.LoadForDevelopment(builder.Environment.ContentRootPath);
    builder.Configuration.AddEnvironmentVariables(); // re-read so the .env values are visible
}

// Comes from .env locally, and from the ConnectionStrings__Default
// environment variable in production (Render). Never from a committed file.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is missing. Set ConnectionStrings__Default.");

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        // Keep EF's own bookkeeping table in our "app" schema too, not in "public".
        npgsql.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
    .UseSnakeCaseNamingConvention()); // PascalCase C# names -> snake_case SQL names (no quoting needed)

var app = builder.Build();

// Simple liveness check: proves the API process is up.
// (The real /health endpoint that also checks the database comes in step 3.1.)
app.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

app.Run();

// Makes the auto-generated Program class visible to the test project,
// so tests can start the whole API in memory with WebApplicationFactory.
public partial class Program { }
