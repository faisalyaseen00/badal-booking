using System.Net;
using System.Net.Http.Json;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class DeviceTests(ApiFactory api)
{
    private static string NewToken() => $"fcm-test-{Guid.NewGuid():N}";

    private static Task<HttpResponseMessage> Register(TestApi.Session s, string token) =>
        s.Client.PostAsJsonAsync("/devices", new { token, platform = "web" });

    private async Task<Guid?> OwnerOf(string token)
    {
        await using var db = api.CreateDbContext();
        return await db.DeviceTokens.Where(d => d.Token == token).Select(d => (Guid?)d.UserId).SingleOrDefaultAsync();
    }

    [Fact]
    public async Task Register_saves_the_token_for_the_caller_and_never_echoes_it()
    {
        var alice = await api.RegisterCustomerAsync();
        var token = NewToken();

        var response = await Register(alice, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        Assert.Equal(alice.UserId, await OwnerOf(token));
    }

    [Fact]
    public async Task Registering_the_same_token_again_is_idempotent()
    {
        var alice = await api.RegisterCustomerAsync();
        var token = NewToken();

        var first = await (await Register(alice, token)).Json();
        var again = await Register(alice, token);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first.GetProperty("id").GetGuid(), (await again.Json()).GetProperty("id").GetGuid());
        await using var db = api.CreateDbContext();
        Assert.Equal(1, await db.DeviceTokens.CountAsync(d => d.Token == token));
    }

    [Fact]
    public async Task Same_device_used_by_another_account_moves_the_token_to_that_account()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();
        var token = NewToken();
        await Register(alice, token);

        await Register(bob, token);

        Assert.Equal(bob.UserId, await OwnerOf(token)); // Alice's pushes no longer go to this device
    }

    [Fact]
    public async Task Logout_removes_the_device_token_and_is_safe_to_repeat()
    {
        var alice = await api.RegisterCustomerAsync();
        var token = NewToken();
        await Register(alice, token);

        var first = await alice.Client.PostAsJsonAsync("/auth/logout", new { deviceToken = token });
        var again = await alice.Client.PostAsJsonAsync("/auth/logout", new { deviceToken = token });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Null(await OwnerOf(token));
    }

    [Fact]
    public async Task Logout_cannot_remove_someone_elses_device_token()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();
        var token = NewToken();
        await Register(alice, token);

        await bob.Client.PostAsJsonAsync("/auth/logout", new { deviceToken = token });

        Assert.Equal(alice.UserId, await OwnerOf(token));
    }

    [Fact]
    public async Task Delete_works_only_for_your_own_device()
    {
        var alice = await api.RegisterCustomerAsync();
        var bob = await api.RegisterCustomerAsync();
        var id = (await (await Register(alice, NewToken())).Json()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.DeleteAsync($"/devices/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync($"/devices/{id}")).StatusCode);
    }

    [Fact]
    public async Task Devices_require_sign_in_and_valid_input()
    {
        var alice = await api.RegisterCustomerAsync();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.CreateClient().PostAsJsonAsync("/devices", new { token = NewToken() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.Client.PostAsJsonAsync("/devices", new { token = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.Client.PostAsJsonAsync("/devices", new { token = NewToken(), platform = "toaster" })).StatusCode);
    }
}
