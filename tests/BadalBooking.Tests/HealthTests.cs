using System.Net;
using BadalBooking.Tests.Infrastructure;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class HealthTests(ApiFactory api)
{
    [Fact]
    public async Task Health_reports_healthy_when_the_database_is_reachable()
    {
        var response = await api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Json();
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Up", body.GetProperty("database").GetString());
    }
}
