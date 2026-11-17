using System.Net;
using ApartmentBooking.Tests.Api.Infrastructure;
using ApartmentBookingApplication.Apartments;
using ApartmentBookingApplication.Bookings;
using ApartmentBookingApplication.OwnerRequests;
using ApartmentBookingDomain.Enums;
using static ApartmentBooking.Tests.Api.Infrastructure.ApiHelpers;

namespace ApartmentBooking.Tests.E2E;

/// <summary>
/// End-to-end backend scenarios: a whole user journey over HTTP against the real API host and PostgreSQL.
/// Every step uses only public endpoints (no direct DB writes), exactly as the SPA and Stripe would.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Level", "E2E")]
public class BookingJourneyE2ETests(ApiFactory api)
{
    private readonly HttpClient _anonymous = api.ClientFor(null);

    [ApiFact]
    public async Task E2E01_Tenant_SearchReservePayDepositPayBalanceThenCancel()
    {
        var apartment = await api.SeedApartmentAsync(districtId: 7, pricePerNight: 1_500m, maxGuests: 3, title: "E2E Поділ студія");
        var tenant = TestUser.Tenant();
        var client = api.ClientFor(tenant);
        var checkIn = Today.AddDays(70);
        var checkOut = checkIn.AddDays(4);

        // 1. Guest searches by district, dates and guests and opens the card.
        var found = await (await _anonymous.GetAsync(
                $"/api/apartments?districtId=7&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}&guestsCount=2"))
            .ReadAsync<List<ApartmentCardDto>>();
        var card = Assert.Single(found, a => a.Id == apartment.Id);
        Assert.Equal(6_000m, card.TotalPrice);
        var details = await (await _anonymous.GetAsync($"/api/apartments/{apartment.Id}")).ReadAsync<ApartmentDetailsDto>();
        Assert.Equal("E2E Поділ студія", details.Title);

        // 2. Signed-in tenant reserves -> 15-min lock + Stripe Checkout URL for the 30 % deposit.
        var reservation = await (await (await client.ReserveAsync(apartment.Id, checkIn, checkOut, guests: 2))
            .EnsureStatusAsync(HttpStatusCode.OK)).ReadAsync<ReserveBookingResponse>();
        Assert.Equal(1_800m, reservation.AmountToPay);

        // 3. While the lock is active the apartment disappears from search for everyone else.
        var duringLock = await (await _anonymous.GetAsync(
                $"/api/apartments?districtId=7&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}"))
            .ReadAsync<List<ApartmentCardDto>>();
        Assert.DoesNotContain(duringLock, a => a.Id == apartment.Id);

        // 4. Stripe confirms the deposit.
        var depositSession = api.Stripe.SessionFor("lock_id", reservation.LockId.ToString());
        await (await _anonymous.SendWebhookAsync(CompletedEvent(depositSession))).EnsureStatusAsync(HttpStatusCode.OK);

        // 5. Booking appears in "Future" with the remaining balance.
        var future = await (await client.GetAsync("/api/bookings/my?status=Future")).ReadAsync<List<BookingListItemDto>>();
        var booking = Assert.Single(future);
        Assert.Equal(BookingStatus.PartiallyPaid, booking.Status);
        Assert.Equal(4_200m, booking.BalanceDue);
        Assert.Equal(4, booking.Nights);

        // 6. Tenant pays the balance.
        var balance = await (await (await client.PostAsync($"/api/bookings/{booking.Id}/pay-balance", null))
            .EnsureStatusAsync(HttpStatusCode.OK)).ReadAsync<CheckoutResponse>();
        Assert.Equal(4_200m, balance.Amount);
        var balanceSession = api.Stripe.SessionFor("booking_id", booking.Id.ToString());
        await (await _anonymous.SendWebhookAsync(CompletedEvent(balanceSession))).EnsureStatusAsync(HttpStatusCode.OK);

        var paid = await (await client.GetAsync($"/api/bookings/{booking.Id}")).ReadAsync<BookingDetailsDto>();
        Assert.Equal(BookingStatus.Paid, paid.Status);
        Assert.Equal(6_000m, paid.PaidAmount);

        // 7. Tenant previews the refund (> 7 days -> free) and cancels; both payments are refunded.
        var preview = await (await client.GetAsync($"/api/bookings/{booking.Id}/refund-preview")).ReadAsync<RefundPreviewDto>();
        Assert.Equal(6_000m, preview.RefundAmount);
        var cancel = await (await (await client.PostAsync($"/api/bookings/{booking.Id}/cancel", null))
            .EnsureStatusAsync(HttpStatusCode.OK)).ReadAsync<CancelBookingResponse>();
        Assert.Equal(RefundStatus.Succeeded, cancel.RefundStatus);
        var refunds = api.Stripe.Refunds.Where(r => r.IdempotencyKey.StartsWith($"refund-{booking.Id}-")).ToList();
        Assert.Equal(2, refunds.Count);
        Assert.Equal(6_000m, refunds.Sum(r => r.Amount));

        // 8. Booking moved to "Cancelled" and the dates are available again.
        var cancelled = await (await client.GetAsync("/api/bookings/my?status=Cancelled")).ReadAsync<List<BookingListItemDto>>();
        Assert.Equal(booking.Id, Assert.Single(cancelled).Id);
        var afterCancel = await (await _anonymous.GetAsync(
                $"/api/apartments?districtId=7&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}"))
            .ReadAsync<List<ApartmentCardDto>>();
        Assert.Contains(afterCancel, a => a.Id == apartment.Id);

        // 9. Emails: confirmation + cancellation.
        var mails = api.Email.Messages.Where(m => m.To == tenant.Email).Select(m => m.Subject).ToList();
        Assert.Contains("Бронювання підтверджено", mails);
        Assert.Contains("Бронювання скасовано", mails);
    }

    [ApiFact]
    public async Task E2E02_TwoTenantsCompeteForSameDates_OnlyFirstGetsBooking()
    {
        var apartment = await api.SeedApartmentAsync(districtId: 5);
        var first = TestUser.Tenant();
        var second = TestUser.Tenant();
        var checkIn = Today.AddDays(80);

        var firstReserve = await (await api.ClientFor(first).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(2)))
            .ReadAsync<ReserveBookingResponse>();
        var secondReserve = await api.ClientFor(second).ReserveAsync(apartment.Id, checkIn.AddDays(1), checkIn.AddDays(3));
        Assert.Equal(HttpStatusCode.Conflict, secondReserve.StatusCode);

        await (await _anonymous.SendWebhookAsync(CompletedEvent(api.Stripe.SessionFor("lock_id", firstReserve.LockId.ToString()))))
            .EnsureStatusAsync(HttpStatusCode.OK);

        // After payment the dates are still blocked (now by the booking instead of the lock).
        Assert.Equal(HttpStatusCode.Conflict,
            (await api.ClientFor(second).ReserveAsync(apartment.Id, checkIn.AddDays(1), checkIn.AddDays(3))).StatusCode);
        Assert.Single(await (await api.ClientFor(first).GetAsync("/api/bookings/my")).ReadAsync<List<BookingListItemDto>>());
        Assert.Empty(await (await api.ClientFor(second).GetAsync("/api/bookings/my")).ReadAsync<List<BookingListItemDto>>());
    }

    [ApiFact]
    public async Task E2E03_OwnerApplies_ManagerClarifiesAndApproves_TenantBooksNewApartment()
    {
        var owner = TestUser.Owner();
        var manager = TestUser.Manager();
        var tenant = TestUser.Tenant();
        var title = $"E2E квартира {Guid.NewGuid():N}"[..22];

        // 1. Owner submits a request.
        var request = await (await (await api.ClientFor(owner).PostJsonAsync("/api/owner-requests", ValidOwnerRequest(title, 1_700m)))
            .EnsureStatusAsync(HttpStatusCode.Created)).ReadAsync<OwnerRequestDetailsDto>();

        // 2. Manager finds it in the table and asks for clarification.
        var list = await (await api.ClientFor(manager).GetAsync($"/api/management/owner-requests?search={request.Number}&status=New"))
            .ReadAsync<ApartmentBookingApplication.Common.PagedResult<OwnerRequestListItemDto>>();
        Assert.Contains(list.Items, r => r.Id == request.Id);
        await (await api.ClientFor(manager).PostJsonAsync($"/api/management/owner-requests/{request.Id}/clarify",
            new OwnerRequestMessageRequest { Message = "Уточніть поверх" })).EnsureStatusAsync(HttpStatusCode.OK);

        // 3. Owner sees the new status in the cabinet.
        var mine = await (await api.ClientFor(owner).GetAsync($"/api/owner-requests/my/{request.Id}")).ReadAsync<OwnerRequestDetailsDto>();
        Assert.Equal(OwnerRequestStatus.NeedsClarification, mine.Status);

        // 4. Manager approves with the final price -> apartment is published.
        var approved = await (await api.ClientFor(manager).PostJsonAsync($"/api/management/owner-requests/{request.Id}/approve",
            new ApproveOwnerRequestRequest { PricePerNight = 1_600m, CheckInInstructions = "Код 2468" })).ReadAsync<OwnerRequestDetailsDto>();
        var apartmentId = approved.ApartmentId!.Value;

        // 5. The apartment is searchable and bookable by a tenant.
        var search = await (await _anonymous.GetAsync("/api/apartments?districtId=7")).ReadAsync<List<ApartmentCardDto>>();
        var card = Assert.Single(search, a => a.Id == apartmentId);
        Assert.Equal(1_600m, card.PricePerNight);
        Assert.Equal(title, card.Title);

        var bookingId = await api.BookAsync(tenant, apartmentId, Today, Today.AddDays(1), payInFull: true);
        var booking = await (await api.ClientFor(tenant).GetAsync($"/api/bookings/{bookingId}")).ReadAsync<BookingDetailsDto>();
        Assert.Equal(BookingStatus.Paid, booking.Status);
        Assert.Equal("Код 2468", booking.CheckInInstructions);
    }

    [ApiFact]
    public async Task E2E04_PaymentAbandoned_LockExpires_AnotherTenantBooks()
    {
        var apartment = await api.SeedApartmentAsync(districtId: 4);
        var checkIn = Today.AddDays(90);
        var abandoned = await (await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(2)))
            .ReadAsync<ReserveBookingResponse>();

        await api.WithDbAsync(db => Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlInterpolatedAsync(
            db.Database, $"UPDATE booking_locks SET expires_at = now() - interval '1 second' WHERE id = {abandoned.LockId}"));

        var winner = TestUser.Tenant();
        var bookingId = await api.BookAsync(winner, apartment.Id, checkIn, checkIn.AddDays(2));

        Assert.NotEqual(Guid.Empty, bookingId);
    }
}
