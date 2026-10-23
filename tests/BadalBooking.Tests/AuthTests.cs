using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BadalBooking.Tests.Infrastructure;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Register_always_creates_a_customer_even_if_the_client_asks_for_performer()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = $"x-{Guid.NewGuid():N}@test.local", password = ApiFactory.TestPassword, role = "Performer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Customer", (await response.Json()).GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Register_with_an_existing_email_in_different_case_returns_409()
    {
        var email = $"dup-{Guid.NewGuid():N}@test.local";
        var client = api.CreateClient();
        (await client.PostAsJsonAsync("/auth/register", new { email, password = ApiFactory.TestPassword })).EnsureSuccessStatusCode();

        var again = await client.PostAsJsonAsync("/auth/register", new { email = email.ToUpperInvariant(), password = ApiFactory.TestPassword });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Login_gives_the_same_401_for_unknown_email_and_wrong_password()
    {
        var customer = await api.RegisterCustomerAsync();
        var me = await customer.Client.GetFromJsonAsync<MeDto>("/auth/me");
        var client = api.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/auth/login", new { email = me!.Email, password = "wrong-password" });
        var unknownEmail = await client.PostAsJsonAsync("/auth/login", new { email = "nobody@test.local", password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal((await wrongPassword.Json()).GetProperty("title").GetString(),
                     (await unknownEmail.Json()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Protected_endpoints_reject_missing_and_tampered_tokens()
    {
        var customer = await api.RegisterCustomerAsync();
        var anonymous = api.CreateClient();
        var tampered = api.CreateClient();
        tampered.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.Token[..^2] + "xx");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tampered.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/bookings")).StatusCode);
    }

    [Fact]
    public async Task Seeded_performer_can_log_in_with_performer_role()
    {
        var performer = await api.LoginSeededPerformerAsync();

        var me = await performer.Client.GetFromJsonAsync<MeDto>("/auth/me");

        Assert.Equal("Performer", me!.Role);
    }

    [Fact]
    public async Task Roles_are_enforced_customer_vs_performer()
    {
        var customer = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync("/bookings/available")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await performer.Client.GetAsync("/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await performer.Client.PostAsJsonAsync("/bookings", new { pilgrimName = "x" })).StatusCode);
    }

    private sealed record MeDto(Guid Id, string Email, string Role);
}
