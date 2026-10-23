using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BadalBooking.Tests;

// Starts the real API in memory (no network port) and calls it over HTTP.
public class PingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Ping_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PingResponse>();
        Assert.Equal("ok", body?.Status);
    }

    private sealed record PingResponse(string Status);
}
