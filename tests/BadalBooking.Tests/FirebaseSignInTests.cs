using System.Net;
using System.Net.Http.Json;
using BadalBooking.Api.Domain;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Tests;

// The account-linking rules of POST /auth/firebase (Part B), with a fake verifier.
[Collection(TestCollection.Name)]
public class FirebaseSignInTests(ApiFactory api)
{
    private static string NewUid() => $"uid-{Guid.NewGuid():N}";
    private static string NewEmail() => $"fb-{Guid.NewGuid():N}@test.local";

    private Task<HttpResponseMessage> SignIn(string idToken) =>
        api.CreateClient().PostAsJsonAsync("/auth/firebase", new { idToken });

    private Task<HttpResponseMessage> SignIn(string uid, string? email, bool verified) =>
        SignIn(FakeFirebaseVerifier.Token(uid, email, verified));

    private static async Task<(Guid Id, string Role)> UserOf(HttpResponseMessage response)
    {
        var user = (await response.Json()).GetProperty("user");
        return (user.GetProperty("id").GetGuid(), user.GetProperty("role").GetString()!);
    }

    private async Task<int> LinksFor(string uid)
    {
        await using var db = api.CreateDbContext();
        return await db.FirebaseIdentities.CountAsync(i => i.FirebaseUid == uid);
    }

    [Fact]
    public async Task New_verified_firebase_user_becomes_a_customer_and_gets_our_jwt()
    {
        var uid = NewUid();

        var response = await SignIn(uid, NewEmail(), verified: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Customer", (await UserOf(response)).Role);
        Assert.False(string.IsNullOrEmpty((await response.Json()).GetProperty("accessToken").GetString()));
        Assert.Equal(1, await LinksFor(uid));
    }

    [Fact]
    public async Task Same_firebase_user_signing_in_again_gets_the_same_account()
    {
        var uid = NewUid();
        var email = NewEmail();

        var first = await UserOf(await SignIn(uid, email, true));
        var second = await UserOf(await SignIn(uid, email, true));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await LinksFor(uid));
    }

    [Fact]
    public async Task Unverified_or_missing_email_is_refused_and_creates_nothing()
    {
        var email = NewEmail();
        var unverified = await SignIn(NewUid(), email, verified: false);
        var noEmail = await SignIn(NewUid(), email: null, verified: true);

        Assert.Equal(HttpStatusCode.Forbidden, unverified.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noEmail.StatusCode);
        await using var db = api.CreateDbContext();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Verified_email_of_an_existing_customer_links_to_that_customer()
    {
        var customer = await api.RegisterCustomerAsync(); // password account
        var email = (await (await customer.Client.GetAsync("/auth/me")).Json()).GetProperty("email").GetString()!;

        var response = await SignIn(NewUid(), email.ToUpperInvariant(), verified: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(customer.UserId, (await UserOf(response)).Id);
    }

    [Fact]
    public async Task Unverified_email_of_an_existing_customer_does_not_link()
    {
        var customer = await api.RegisterCustomerAsync();
        var email = (await (await customer.Client.GetAsync("/auth/me")).Json()).GetProperty("email").GetString()!;
        var uid = NewUid();

        Assert.Equal(HttpStatusCode.Forbidden, (await SignIn(uid, email, verified: false)).StatusCode);
        Assert.Equal(0, await LinksFor(uid));
    }

    // The key Part B rule: a Firebase login must NEVER take over a Performer (or Admin) account by email,
    // even with a verified email.
    [Theory]
    [InlineData(ApiFactory.PerformerEmail)]
    [InlineData(ApiFactory.AdminEmail)]
    public async Task Verified_email_of_a_performer_or_admin_is_refused(string staffEmail)
    {
        var uid = NewUid();

        var response = await SignIn(uid, staffEmail, verified: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await LinksFor(uid));
        Assert.DoesNotContain("Performer", await response.Content.ReadAsStringAsync()); // doesn't reveal the role
    }

    [Fact]
    public async Task A_second_firebase_account_cannot_replace_an_existing_link()
    {
        var email = NewEmail();
        var original = NewUid();
        var intruder = NewUid();
        var owner = await UserOf(await SignIn(original, email, true));

        var response = await SignIn(intruder, email, true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await LinksFor(intruder));
        Assert.Equal(owner.Id, (await UserOf(await SignIn(original, email, true))).Id); // owner unaffected
    }

    [Fact]
    public async Task Invalid_token_is_401_and_firebase_outage_is_503()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn("invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await SignIn("firebase-down")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SignIn("")).StatusCode);
    }

    [Fact]
    public async Task Simultaneous_first_sign_ins_create_exactly_one_account()
    {
        var uid = NewUid();
        var email = NewEmail();
        var responses = new HttpResponseMessage[5];

        await TestApi.RaceAsync(Enumerable.Range(0, 5).Select(i => (Func<Task<HttpResponseMessage>>)(async () =>
            responses[i] = await SignIn(uid, email, true))).ToArray());

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await UserOf(r)).Id));
        Assert.Single(ids.Distinct());
        await using var db = api.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == email));
        Assert.Equal(1, await LinksFor(uid));
    }

    [Fact]
    public async Task Approved_performer_signs_in_with_firebase_and_gets_the_performer_role()
    {
        var uid = NewUid();
        var email = NewEmail();
        var first = await SignIn(uid, email, true);
        var token = (await first.Json()).GetProperty("accessToken").GetString()!;
        var customer = api.CreateClient();
        customer.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var appId = (await (await customer.PostAsJsonAsync("/performer-applications", new { fullName = "Fb User", phone = "+966500000000" })).Json())
            .GetProperty("id").GetGuid();
        var admin = await api.LoginAdminAsync();
        (await admin.Client.PostAsync($"/admin/performer-applications/{appId}/approve", null)).EnsureSuccessStatusCode();

        var again = await UserOf(await SignIn(uid, email, true));

        Assert.Equal("Performer", again.Role); // found by uid (rule 1), role read from the database
        await using var db = api.CreateDbContext();
        Assert.Equal(UserRole.Performer, (await db.Users.SingleAsync(u => u.Email == email)).Role);
    }
}
