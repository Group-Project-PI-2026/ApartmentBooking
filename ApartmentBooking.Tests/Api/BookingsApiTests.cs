using System.Net;
using ApartmentBooking.Tests.Api.Infrastructure;
using ApartmentBookingApplication.Bookings;
using ApartmentBookingDomain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static ApartmentBooking.Tests.Api.Infrastructure.ApiHelpers;

namespace ApartmentBooking.Tests.Api;

/// <summary>Integration: reservation lock, tenant cabinet, balance payment and cancellation (TC-BOOK-*, TC-ACC-*).</summary>
[Collection(ApiCollection.Name)]
[Trait("Level", "Integration")]
public class BookingsApiTests(ApiFactory api)
{
    [ApiFact]
    public async Task Reserve_CreatesFifteenMinuteLock_WithThirtyPercentDeposit_AndStripeMetadata()
    {
        var apartment = await api.SeedApartmentAsync(pricePerNight: 1_000m);
        var tenant = TestUser.Tenant();
        var checkIn = Today.AddDays(25);

        var response = await api.ClientFor(tenant).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(3));

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var reservation = await response.ReadAsync<ReserveBookingResponse>();
        Assert.Equal(3_000m, reservation.TotalPrice);
        Assert.Equal(900m, reservation.AmountToPay);
        Assert.InRange(reservation.ExpiresAt - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));
        Assert.StartsWith("https://checkout.stripe.test/", reservation.CheckoutUrl);

        var session = api.Stripe.SessionFor("lock_id", reservation.LockId.ToString());
        Assert.Equal(900m, session.Request.Amount);
        Assert.Equal(tenant.Email, session.Request.CustomerEmail);
        Assert.Equal("reservation", session.Request.Metadata["kind"]);
        Assert.Equal(apartment.Id.ToString(), session.Request.Metadata["apartment_id"]);
        Assert.Equal(tenant.Id.ToString(), session.Request.Metadata["tenant_id"]);
        Assert.Equal(checkIn.ToString("yyyy-MM-dd"), session.Request.Metadata["check_in"]);
        Assert.Equal(3_000m, decimal.Parse(session.Request.Metadata["total_price"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("session_id={CHECKOUT_SESSION_ID}", session.Request.SuccessUrl);

        var bookingLock = await api.WithDbAsync(db => db.BookingLocks.SingleAsync(l => l.Id == reservation.LockId));
        Assert.Equal(session.SessionId, bookingLock.CheckoutSessionId);
    }

    [ApiFact]
    public async Task Reserve_PayInFull_ChargesTotalPrice()
    {
        var apartment = await api.SeedApartmentAsync(pricePerNight: 1_100m);

        var reservation = await (await api.ClientFor(TestUser.Tenant())
                .ReserveAsync(apartment.Id, Today.AddDays(26), Today.AddDays(28), payInFull: true))
            .ReadAsync<ReserveBookingResponse>();

        Assert.Equal(2_200m, reservation.TotalPrice);
        Assert.Equal(2_200m, reservation.AmountToPay);
    }

    [ApiFact]
    public async Task Reserve_DatesHeldByAnotherTenant_Returns409_WithoutSecondLock()
    {
        var apartment = await api.SeedApartmentAsync();
        var checkIn = Today.AddDays(27);
        await (await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(4)))
            .EnsureStatusAsync(HttpStatusCode.OK);

        var response = await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn.AddDays(2), checkIn.AddDays(6));

        await response.EnsureStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal("Selected dates are no longer available.", (await response.ReadAsync<ProblemDetails>()).Detail);
        Assert.Equal(1, await api.WithDbAsync(db => db.BookingLocks.CountAsync(l => l.ApartmentId == apartment.Id)));
    }

    [ApiFact]
    public async Task Reserve_DatesOfConfirmedBooking_Returns409()
    {
        var apartment = await api.SeedApartmentAsync();
        var checkIn = Today.AddDays(28);
        await api.BookAsync(TestUser.Tenant(), apartment.Id, checkIn, checkIn.AddDays(2));

        var response = await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [ApiFact]
    public async Task Reserve_SameTenantTwice_ReplacesPreviousLock()
    {
        var apartment = await api.SeedApartmentAsync();
        var client = api.ClientFor(TestUser.Tenant());
        var checkIn = Today.AddDays(29);

        var first = await (await client.ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(2))).ReadAsync<ReserveBookingResponse>();
        var second = await (await client.ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(3))).ReadAsync<ReserveBookingResponse>();

        var locks = await api.WithDbAsync(db => db.BookingLocks.Where(l => l.ApartmentId == apartment.Id).ToListAsync());
        var single = Assert.Single(locks);
        Assert.Equal(second.LockId, single.Id);
        Assert.NotEqual(first.LockId, second.LockId);
    }

    [ApiFact]
    public async Task Reserve_WhenStripeFails_Returns502_AndReleasesDates()
    {
        var apartment = await api.SeedApartmentAsync();
        var checkIn = Today.AddDays(31);
        api.Stripe.FailNextCheckout = true;

        var response = await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(2));

        await response.EnsureStatusAsync(HttpStatusCode.BadGateway);
        Assert.False(await api.WithDbAsync(db => db.BookingLocks.AnyAsync(l => l.ApartmentId == apartment.Id)));
        await (await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(2)))
            .EnsureStatusAsync(HttpStatusCode.OK);
    }

    [ApiFact]
    public async Task Reserve_MoreGuestsThanCapacity_Returns400()
    {
        var apartment = await api.SeedApartmentAsync(maxGuests: 2);

        var response = await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, Today.AddDays(32), Today.AddDays(33), guests: 3);

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.Contains("at most 2 guests", (await response.ReadAsync<ProblemDetails>()).Detail);
    }

    [ApiFact]
    public async Task Reserve_CheckInInThePast_Returns400_BusinessRule()
    {
        var apartment = await api.SeedApartmentAsync();

        var response = await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, Today.AddDays(-1), Today.AddDays(2));

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.Equal("Business rule violation", (await response.ReadAsync<ProblemDetails>()).Title);
    }

    [ApiFact]
    public async Task Reserve_ZeroGuests_FailsModelValidation()
    {
        var apartment = await api.SeedApartmentAsync();

        var response = await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, Today.AddDays(33), Today.AddDays(34), guests: 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("GuestsCount", await response.Content.ReadAsStringAsync());
    }

    [ApiFact]
    public async Task Reserve_UnknownOrInactiveApartment_Returns404()
    {
        var inactive = await api.SeedApartmentAsync(active: false);
        var client = api.ClientFor(TestUser.Tenant());

        Assert.Equal(HttpStatusCode.NotFound, (await client.ReserveAsync(Guid.NewGuid(), Today.AddDays(5), Today.AddDays(6))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.ReserveAsync(inactive.Id, Today.AddDays(5), Today.AddDays(6))).StatusCode);
    }

    [ApiFact]
    public async Task MyBookings_TabFilter_SeparatesFutureAndCancelled()
    {
        var tenant = TestUser.Tenant();
        var client = api.ClientFor(tenant);
        var a1 = await api.SeedApartmentAsync();
        var a2 = await api.SeedApartmentAsync();
        var active = await api.BookAsync(tenant, a1.Id, Today.AddDays(40), Today.AddDays(42));
        var cancelled = await api.BookAsync(tenant, a2.Id, Today.AddDays(45), Today.AddDays(47));
        await (await client.PostAsync($"/api/bookings/{cancelled}/cancel", null)).EnsureStatusAsync(HttpStatusCode.OK);

        var future = await (await client.GetAsync("/api/bookings/my?status=Future")).ReadAsync<List<BookingListItemDto>>();
        var cancelledTab = await (await client.GetAsync("/api/bookings/my?status=Cancelled")).ReadAsync<List<BookingListItemDto>>();
        var past = await (await client.GetAsync("/api/bookings/my?status=Past")).ReadAsync<List<BookingListItemDto>>();
        var all = await (await client.GetAsync("/api/bookings/my")).ReadAsync<List<BookingListItemDto>>();

        Assert.Equal([active], future.Select(b => b.Id));
        Assert.Equal([cancelled], cancelledTab.Select(b => b.Id));
        Assert.Empty(past);
        Assert.Equal(2, all.Count);
    }

    [ApiFact]
    public async Task BookingDetails_ShowsAmounts_AndHidesInstructionsBeforeCheckIn()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync(pricePerNight: 2_000m);
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(20), Today.AddDays(22));

        var details = await (await api.ClientFor(tenant).GetAsync($"/api/bookings/{bookingId}")).ReadAsync<BookingDetailsDto>();

        Assert.Equal(BookingStatus.PartiallyPaid, details.Status);
        Assert.Equal(4_000m, details.TotalPrice);
        Assert.Equal(1_200m, details.PaidAmount);
        Assert.Equal(2_800m, details.BalanceDue);
        Assert.Equal(2_000m, details.PricePerNight);
        Assert.Equal(2, details.Nights);
        Assert.True(details.CanPayBalance);
        Assert.True(details.CanCancel);
        Assert.Null(details.CheckInInstructions);
    }

    [ApiFact]
    public async Task CheckInInstructions_AreVisibleOnlyForFullyPaidStayInProgress()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync();
        var paid = await api.BookAsync(tenant, apartment.Id, Today, Today.AddDays(2), payInFull: true);
        var other = await api.SeedApartmentAsync();
        var unpaid = await api.BookAsync(tenant, other.Id, Today, Today.AddDays(2));
        var client = api.ClientFor(tenant);

        var paidDetails = await (await client.GetAsync($"/api/bookings/{paid}")).ReadAsync<BookingDetailsDto>();
        var unpaidDetails = await (await client.GetAsync($"/api/bookings/{unpaid}")).ReadAsync<BookingDetailsDto>();

        Assert.Equal("Ключі в боксі, код 0000", paidDetails.CheckInInstructions);
        Assert.Null(unpaidDetails.CheckInInstructions);
        Assert.False(paidDetails.CanCancel, "stay already started");
    }

    [ApiFact]
    public async Task PayBalance_CreatesCheckoutForRemainingAmount()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync(pricePerNight: 1_000m);
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(35), Today.AddDays(37));

        var response = await api.ClientFor(tenant).PostAsync($"/api/bookings/{bookingId}/pay-balance", null);

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var checkout = await response.ReadAsync<CheckoutResponse>();
        Assert.Equal(1_400m, checkout.Amount);
        var session = api.Stripe.SessionFor("booking_id", bookingId.ToString());
        Assert.Equal("balance", session.Request.Metadata["kind"]);
        Assert.Equal(1_400m, session.Request.Amount);
    }

    [ApiFact]
    public async Task PayBalance_ForFullyPaidBooking_Returns409()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync();
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(36), Today.AddDays(37), payInFull: true);

        var response = await api.ClientFor(tenant).PostAsync($"/api/bookings/{bookingId}/pay-balance", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [ApiFact]
    public async Task Cancel_MoreThanSevenDaysBefore_RefundsEverything_AndSendsEmail()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync(pricePerNight: 1_000m);
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(10), Today.AddDays(12), payInFull: true);
        var client = api.ClientFor(tenant);

        var preview = await (await client.GetAsync($"/api/bookings/{bookingId}/refund-preview")).ReadAsync<RefundPreviewDto>();
        var cancel = await (await (await client.PostAsync($"/api/bookings/{bookingId}/cancel", null))
            .EnsureStatusAsync(HttpStatusCode.OK)).ReadAsync<CancelBookingResponse>();

        Assert.Equal(0m, preview.PenaltyAmount);
        Assert.Equal(2_000m, preview.RefundAmount);
        Assert.Equal(BookingStatus.Cancelled, cancel.Status);
        Assert.Equal(RefundStatus.Succeeded, cancel.RefundStatus);
        Assert.Equal(2_000m, cancel.RefundAmount);
        Assert.Contains(api.Stripe.Refunds, r => r.Amount == 2_000m && r.IdempotencyKey.StartsWith($"refund-{bookingId}-"));
        Assert.Contains(api.Email.Messages, m => m.To == tenant.Email && m.Subject == "Бронювання скасовано");
    }

    [ApiFact]
    public async Task Cancel_InsideLateWindow_AppliesFiftyPercentPenalty()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync(pricePerNight: 1_000m);
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(3), Today.AddDays(5), payInFull: true);

        var cancel = await (await api.ClientFor(tenant).PostAsync($"/api/bookings/{bookingId}/cancel", null))
            .ReadAsync<CancelBookingResponse>();

        Assert.Equal(1_000m, cancel.PenaltyAmount);
        Assert.Equal(1_000m, cancel.RefundAmount);
    }

    [ApiFact]
    public async Task Cancel_Twice_SecondCallReturns409_AndRefundsOnce()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync();
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(15), Today.AddDays(16), payInFull: true);
        var client = api.ClientFor(tenant);

        await (await client.PostAsync($"/api/bookings/{bookingId}/cancel", null)).EnsureStatusAsync(HttpStatusCode.OK);
        var second = await client.PostAsync($"/api/bookings/{bookingId}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(api.Stripe.Refunds, r => r.IdempotencyKey.StartsWith($"refund-{bookingId}-"));
    }

    [ApiFact]
    public async Task Cancel_WhenRefundFails_BookingStaysCancelled_WithFailedRefundStatus()
    {
        var tenant = TestUser.Tenant();
        var apartment = await api.SeedApartmentAsync();
        var bookingId = await api.BookAsync(tenant, apartment.Id, Today.AddDays(18), Today.AddDays(19), payInFull: true);
        api.Stripe.FailRefunds = true;
        try
        {
            var cancel = await (await (await api.ClientFor(tenant).PostAsync($"/api/bookings/{bookingId}/cancel", null))
                .EnsureStatusAsync(HttpStatusCode.OK)).ReadAsync<CancelBookingResponse>();

            Assert.Equal(BookingStatus.Cancelled, cancel.Status);
            Assert.Equal(RefundStatus.Failed, cancel.RefundStatus);
        }
        finally
        {
            api.Stripe.FailRefunds = false;
        }
    }

    [ApiFact]
    public async Task CancelledBooking_ReleasesDatesForOtherTenants()
    {
        var apartment = await api.SeedApartmentAsync();
        var tenant = TestUser.Tenant();
        var checkIn = Today.AddDays(55);
        var bookingId = await api.BookAsync(tenant, apartment.Id, checkIn, checkIn.AddDays(2));
        await api.ClientFor(tenant).PostAsync($"/api/bookings/{bookingId}/cancel", null);

        var response = await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
