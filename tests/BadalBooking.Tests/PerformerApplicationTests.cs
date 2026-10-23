using System.Net;
using System.Net.Http.Json;
using BadalBooking.Api.Domain;
using BadalBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BadalBooking.Tests;

[Collection(TestCollection.Name)]
public class PerformerApplicationTests(ApiFactory api)
{
    private static Task<HttpResponseMessage> Apply(TestApi.Session s) =>
        s.Client.PostAsJsonAsync("/performer-applications", new { fullName = "Yusuf Ali", phone = "+966 50 123 4567", note = "I live in Makkah." });

    private static async Task<Guid> ApplyAndGetId(TestApi.Session s) =>
        (await (await Apply(s)).Json()).GetProperty("id").GetGuid();

    private async Task<UserRole> RoleOf(Guid userId)
    {
        await using var db = api.CreateDbContext();
        return (await db.Users.SingleAsync(u => u.Id == userId)).Role;
    }

    [Fact]
    public async Task Customer_can_apply_once_and_see_the_pending_status()
    {
        var alice = await api.RegisterCustomerAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.GetAsync("/performer-applications/me")).StatusCode);

        var first = await Apply(alice);
        var second = await Apply(alice);
        var mine = await (await alice.Client.GetAsync("/performer-applications/me")).Json();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode); // only one open application
        Assert.Equal("Pending", mine.GetProperty("status").GetString());
        Assert.Equal(UserRole.Customer, await RoleOf(alice.UserId)); // applying alone changes nothing
    }

    [Fact]
    public async Task Only_admins_can_list_approve_or_reject()
    {
        var alice = await api.RegisterCustomerAsync();
        var performer = await api.LoginSeededPerformerAsync();
        var id = await ApplyAndGetId(alice);

        foreach (var notAdmin in new[] { alice, performer })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await notAdmin.Client.GetAsync("/admin/performer-applications")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await notAdmin.Client.PostAsync($"/admin/performer-applications/{id}/approve", null)).StatusCode);
        }
        Assert.Equal(UserRole.Customer, await RoleOf(alice.UserId));
    }

    [Fact]
    public async Task Approval_makes_the_applicant_a_performer_after_they_sign_in_again()
    {
        var alice = await api.RegisterCustomerAsync();
        var admin = await api.LoginAdminAsync();
        var id = await ApplyAndGetId(alice);

        var pending = (await (await admin.Client.GetAsync("/admin/performer-applications?status=Pending")).Json()).EnumerateArray();
        Assert.Contains(pending, a => a.GetProperty("application").GetProperty("id").GetGuid() == id);

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/admin/performer-applications/{id}/approve", null)).StatusCode);
        Assert.Equal(UserRole.Performer, await RoleOf(alice.UserId));

        // The old token still says Customer (JWTs are fixed until they expire)...
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.GetAsync("/bookings/available")).StatusCode);
        // ...a fresh sign-in gets the Performer role from the database.
        var me = await (await alice.Client.GetAsync("/auth/me")).Json();
        var again = await api.LoginAsync(me.GetProperty("email").GetString()!, ApiFactory.TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await again.Client.GetAsync("/bookings/available")).StatusCode);
    }

    [Fact]
    public async Task A_decision_can_only_be_made_once()
    {
        var alice = await api.RegisterCustomerAsync();
        var admin = await api.LoginAdminAsync();
        var id = await ApplyAndGetId(alice);
        (await admin.Client.PostAsync($"/admin/performer-applications/{id}/approve", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsync($"/admin/performer-applications/{id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsync($"/admin/performer-applications/{id}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.PostAsync($"/admin/performer-applications/{Guid.NewGuid()}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Rejection_keeps_the_customer_role_and_allows_a_new_application()
    {
        var alice = await api.RegisterCustomerAsync();
        var admin = await api.LoginAdminAsync();
        var id = await ApplyAndGetId(alice);

        var reject = await admin.Client.PostAsJsonAsync($"/admin/performer-applications/{id}/reject", new { note = "Please add your experience." });
        var mine = await (await alice.Client.GetAsync("/performer-applications/me")).Json();

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("Rejected", mine.GetProperty("status").GetString());
        Assert.Equal("Please add your experience.", mine.GetProperty("decisionNote").GetString());
        Assert.Equal(UserRole.Customer, await RoleOf(alice.UserId));
        Assert.Equal(HttpStatusCode.Created, (await Apply(alice)).StatusCode);
    }

    [Fact]
    public async Task Performers_and_admins_cannot_apply_and_input_is_validated()
    {
        var performer = await api.LoginSeededPerformerAsync();
        var admin = await api.LoginAdminAsync();
        var alice = await api.RegisterCustomerAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Apply(performer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Apply(admin)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alice.Client.PostAsJsonAsync("/performer-applications", new { fullName = "", phone = "abc" })).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_be_created_by_registering()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = $"x-{Guid.NewGuid():N}@test.local", password = ApiFactory.TestPassword, role = "Admin" });
        Assert.Equal("Customer", (await response.Json()).GetProperty("user").GetProperty("role").GetString());
    }
}
