using System.Net.Http.Json;
using BadalBooking.Tests.Infrastructure;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class StatusTests(ApiFactory api)
{
    [Fact]
    public async Task Demo_bookings_shows_only_the_demo_customers_last_5_without_personal_data()
    {
        // The demo customer (configured on the server) may already exist from another run of this test.
        var client = api.CreateClient();
        var register = await client.PostAsJsonAsync("/auth/register", new { email = ApiFactory.DemoCustomerEmail, password = ApiFactory.TestPassword });
        var demo = register.IsSuccessStatusCode
            ? await api.LoginAsync(ApiFactory.DemoCustomerEmail, ApiFactory.TestPassword)
            : await api.LoginAsync(ApiFactory.DemoCustomerEmail, ApiFactory.TestPassword);
        for (var i = 0; i < 6; i++) await demo.CreateBookingAsync($"Secret Pilgrim {i}");
        var someoneElse = await api.RegisterCustomerAsync();
        await someoneElse.CreateBookingAsync("Other Customer Pilgrim");

        var response = await api.CreateClient().GetAsync("/status/demo-bookings"); // no sign-in needed
        var text = await response.Content.ReadAsStringAsync();
        var body = await response.Json();

        Assert.True(body.GetProperty("configured").GetBoolean());
        Assert.Equal(5, body.GetProperty("bookings").GetArrayLength());
        Assert.DoesNotContain("Pilgrim", text);                         // no pilgrim names
        Assert.DoesNotContain(ApiFactory.DemoCustomerEmail, text);      // email is masked
        Assert.All(body.GetProperty("bookings").EnumerateArray(),
            b => Assert.Equal(8, b.GetProperty("reference").GetString()!.Length)); // short reference, not the full id
    }
}
