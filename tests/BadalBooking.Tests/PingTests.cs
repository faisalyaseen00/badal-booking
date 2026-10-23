using System.Net;
using System.Net.Http.Json;
using BadalBooking.Tests.Infrastructure;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class PingTests(ApiFactory api)
{
    [Fact]
    public async Task Ping_returns_ok()
    {
        var response = await api.CreateClient().GetAsync("/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PingResponse>();
        Assert.Equal("ok", body?.Status);
    }

    private sealed record PingResponse(string Status);
}
