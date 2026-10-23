using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BadalBooking.Tests.Infrastructure;

// Small helpers so each test reads like the scenario it checks.
public static class TestApi
{
    public sealed record Session(HttpClient Client, Guid UserId, string Token);

    public static async Task<Session> RegisterCustomerAsync(this ApiFactory api)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/register",
            new { email = $"customer-{Guid.NewGuid():N}@test.local", password = ApiFactory.TestPassword });
        response.EnsureSuccessStatusCode();
        return await ToSessionAsync(client, response);
    }

    public static async Task<Session> LoginAsync(this ApiFactory api, string email, string password)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return await ToSessionAsync(client, response);
    }

    public static Task<Session> LoginAdminAsync(this ApiFactory api) =>
        api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

    public static Task<Session> LoginSeededPerformerAsync(this ApiFactory api) =>
        api.LoginAsync(ApiFactory.PerformerEmail, ApiFactory.PerformerPassword);

    public static async Task<Session> NewPerformerAsync(this ApiFactory api) =>
        await api.LoginAsync(await api.CreatePerformerAsync(), ApiFactory.TestPassword);

    public static async Task<Guid> CreateBookingAsync(this Session customer, string pilgrim = "Test Pilgrim")
    {
        var response = await customer.Client.PostAsJsonAsync("/bookings", new { pilgrimName = pilgrim });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Json()).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> PayAsync(this Session customer, Guid bookingId, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/bookings/{bookingId}/pay");
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return customer.Client.SendAsync(request);
    }

    public static async Task<Guid> CreatePaidBookingAsync(this Session customer)
    {
        var id = await customer.CreateBookingAsync();
        (await customer.PayAsync(id)).EnsureSuccessStatusCode();
        return id;
    }

    public static Task<HttpResponseMessage> AcceptAsync(this Session performer, Guid bookingId) =>
        performer.Client.PostAsync($"/bookings/{bookingId}/accept", null);

    public static Task<HttpResponseMessage> CancelAsync(this Session customer, Guid bookingId) =>
        customer.Client.PostAsync($"/bookings/{bookingId}/cancel", null);

    public static async Task<JsonElement> Json(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // Start all requests at the same moment (as close as the test server allows) and wait for all.
    public static async Task<HttpStatusCode[]> RaceAsync(params Func<Task<HttpResponseMessage>>[] requests)
    {
        using var start = new ManualResetEventSlim(false);
        var tasks = requests.Select(r => Task.Run(async () => { start.Wait(); return (await r()).StatusCode; })).ToArray();
        start.Set();
        return await Task.WhenAll(tasks);
    }

    private static async Task<Session> ToSessionAsync(HttpClient client, HttpResponseMessage response)
    {
        var body = await response.Json();
        var token = body.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Session(client, body.GetProperty("user").GetProperty("id").GetGuid(), token);
    }
}
