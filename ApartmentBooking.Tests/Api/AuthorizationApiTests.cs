using System.Net;
using ApartmentBooking.Tests.Api.Infrastructure;
using ApartmentBookingApplication.Bookings;
using Microsoft.EntityFrameworkCore;
using static ApartmentBooking.Tests.Api.Infrastructure.ApiHelpers;

namespace ApartmentBooking.Tests.Api;

/// <summary>Integration: JWT/RBAC policies and IDOR protection (TC-AUTH-003/004/005).</summary>
[Collection(ApiCollection.Name)]
[Trait("Level", "Integration")]
public class AuthorizationApiTests(ApiFactory api)
{
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", "/api/bookings/my" },
        { "GET", $"/api/bookings/{Guid.NewGuid()}" },
        { "POST", "/api/bookings/reserve" },
        { "POST", $"/api/bookings/{Guid.NewGuid()}/cancel" },
        { "GET", "/api/owner-requests/my" },
        { "POST", "/api/owner-requests" },
        { "GET", "/api/management/owner-requests" },
        { "POST", $"/api/management/owner-requests/{Guid.NewGuid()}/approve" },
    };

    [ApiTheory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task ProtectedEndpoint_WithoutToken_Returns401(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "POST" ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json") : null
        };

        var response = await api.ClientFor(null).SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [ApiFact]
    public async Task Reserve_WithoutToken_DoesNotCreateLockOrStripeSession()
    {
        var apartment = await api.SeedApartmentAsync();
        var sessionsBefore = api.Stripe.Sessions.Count;

        var response = await api.ClientFor(null).ReserveAsync(apartment.Id, Today.AddDays(20), Today.AddDays(22));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(sessionsBefore, api.Stripe.Sessions.Count);
        Assert.False(await api.WithDbAsync(db => db.BookingLocks.AnyAsync(l => l.ApartmentId == apartment.Id)));
    }

    [ApiTheory]
    [InlineData("tenant")]
    [InlineData("owner")]
    public async Task ManagerEndpoints_ForNonManagerRoles_Return403(string role)
    {
        var user = new TestUser(Guid.NewGuid(), $"{role}@myrent.test", role);

        var response = await api.ClientFor(user).GetAsync("/api/management/owner-requests");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [ApiTheory]
    [InlineData("manager")]
    [InlineData("admin")]
    public async Task ManagerEndpoints_ForManagerAndAdmin_Return200(string role)
    {
        var user = new TestUser(Guid.NewGuid(), $"{role}@myrent.test", role);

        var response = await api.ClientFor(user).GetAsync("/api/management/owner-requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [ApiFact]
    public async Task TenantEndpoints_ForManagerWithoutTenantRole_Return403()
    {
        var response = await api.ClientFor(TestUser.Manager()).GetAsync("/api/bookings/my");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [ApiFact]
    public async Task AdminRole_CanUseTenantEndpoints()
    {
        var response = await api.ClientFor(TestUser.Admin()).GetAsync("/api/bookings/my");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [ApiFact]
    public async Task ForeignBooking_IsReportedAsNotFound_ForEveryTenantEndpoint()
    {
        var apartment = await api.SeedApartmentAsync();
        var owner = TestUser.Tenant();
        var stranger = TestUser.Tenant();
        var checkIn = Today.AddDays(30);
        var bookingId = await api.BookAsync(owner, apartment.Id, checkIn, checkIn.AddDays(2));
        var client = api.ClientFor(stranger);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/bookings/{bookingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/bookings/{bookingId}/refund-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/bookings/{bookingId}/pay-balance", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/bookings/{bookingId}/cancel", null)).StatusCode);

        var my = await (await client.GetAsync("/api/bookings/my")).ReadAsync<List<BookingListItemDto>>();
        Assert.DoesNotContain(my, b => b.Id == bookingId);

        var booking = await api.WithDbAsync(db => db.Bookings.SingleAsync(b => b.Id == bookingId));
        Assert.NotEqual(ApartmentBookingDomain.Enums.BookingStatus.Cancelled, booking.Status);
    }

    [ApiFact]
    public async Task ForeignOwnerRequest_IsReportedAsNotFound()
    {
        var author = TestUser.Owner();
        var created = await (await api.ClientFor(author).PostJsonAsync("/api/owner-requests", ValidOwnerRequest()))
            .EnsureStatusAsync(HttpStatusCode.Created);
        var request = await created.ReadAsync<ApartmentBookingApplication.OwnerRequests.OwnerRequestDetailsDto>();

        var response = await api.ClientFor(TestUser.Owner()).GetAsync($"/api/owner-requests/my/{request.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(request.ContactEmail, await response.Content.ReadAsStringAsync());
    }

    [ApiFact]
    public async Task TokenWithInvalidSubClaim_Returns401_NotServerError()
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, "not-a-guid");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "tenant");

        var response = await client.GetAsync("/api/bookings/my");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
