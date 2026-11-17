using System.Globalization;
using ApartmentBooking.Tests.TestDoubles;
using ApartmentBookingApplication.Bookings;
using ApartmentBookingApplication.Exceptions;
using ApartmentBookingApplication.Options;
using ApartmentBookingApplication.OwnerRequests;
using ApartmentBookingDomain.Entities;
using ApartmentBookingInfrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApartmentBooking.Tests.Integration;

public class CriticalBookingFlowTests
{
    [PostgresFact]
    public async Task ConcurrentReservations_ForSameDates_CreateExactlyOneActiveLock()
    {
        await using var environment = await PostgresTestEnvironment.StartAsync();
        var apartmentId = await SeedApartmentAsync(environment);
        var gateway = new RecordingPaymentGateway();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new ReserveBookingRequest
        {
            ApartmentId = apartmentId,
            CheckIn = today.AddDays(30),
            CheckOut = today.AddDays(32),
            GuestsCount = 2
        };

        await using var firstDb = environment.CreateDbContext();
        await using var secondDb = environment.CreateDbContext();
        var firstService = CreateBookingService(firstDb, Guid.NewGuid(), gateway);
        var secondService = CreateBookingService(secondDb, Guid.NewGuid(), gateway);

        var results = await Task.WhenAll(
            Record.ExceptionAsync(() => firstService.ReserveAsync(request, CancellationToken.None)),
            Record.ExceptionAsync(() => secondService.ReserveAsync(request, CancellationToken.None)));

        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is ConflictException);

        await using var verificationDb = environment.CreateDbContext();
        Assert.Equal(1, await verificationDb.BookingLocks.CountAsync());
        Assert.Equal(0, await verificationDb.Bookings.CountAsync());
    }

    [PostgresFact]
    public async Task DuplicateReservationWebhook_CreatesOneBookingAndOnePayment()
    {
        await using var environment = await PostgresTestEnvironment.StartAsync();
        var apartmentId = await SeedApartmentAsync(environment);
        var tenantId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dates = new DateRange(today.AddDays(30), today.AddDays(32));
        var totalPrice = 2_400m;
        var amountPaid = 720m;
        var sessionId = "cs_duplicate_webhook";

        Guid lockId;
        await using (var seedDb = environment.CreateDbContext())
        {
            var bookingLock = new BookingLock(
                apartmentId,
                tenantId,
                "tenant@example.test",
                dates,
                totalPrice,
                amountPaid);
            bookingLock.AttachCheckoutSession(sessionId);
            seedDb.BookingLocks.Add(bookingLock);
            await seedDb.SaveChangesAsync();
            lockId = bookingLock.Id;
        }

        var gateway = new RecordingPaymentGateway
        {
            WebhookEvent = new ApartmentBookingApplication.Abstractions.PaymentWebhookEvent(
                "evt_duplicate",
                PaymentWebhookService.CheckoutSessionCompleted,
                sessionId,
                "pi_duplicate",
                "paid",
                amountPaid,
                "tenant@example.test",
                new Dictionary<string, string>
                {
                    ["kind"] = "reservation",
                    ["lock_id"] = lockId.ToString(),
                    ["apartment_id"] = apartmentId.ToString(),
                    ["tenant_id"] = tenantId.ToString(),
                    ["check_in"] = dates.CheckIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["check_out"] = dates.CheckOut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["total_price"] = totalPrice.ToString(CultureInfo.InvariantCulture)
                })
        };

        await using (var webhookDb = environment.CreateDbContext())
        {
            var service = new PaymentWebhookService(
                webhookDb,
                gateway,
                new RecordingEmailSender(),
                Options.Create(new BookingOptions()),
                NullLogger<PaymentWebhookService>.Instance);

            await service.HandleAsync("payload", "signature", CancellationToken.None);
            await service.HandleAsync("payload", "signature", CancellationToken.None);
        }

        await using var verificationDb = environment.CreateDbContext();
        Assert.Equal(1, await verificationDb.Bookings.CountAsync());
        Assert.Equal(1, await verificationDb.Payments.CountAsync());
        Assert.Equal(0, await verificationDb.BookingLocks.CountAsync());
    }

    [PostgresFact]
    [Trait("Risk", "Concurrent owner request decision")]
    [Trait("Category", "KnownBug")]
    [Trait("Bug", "BUG-BE-006")]
    public async Task ConcurrentOwnerApprovals_CreateAtMostOneApartment()
    {
        await using var environment = await PostgresTestEnvironment.StartAsync();
        var requestId = await SeedOwnerRequestAsync(environment);

        await using var firstDb = environment.CreateDbContext();
        await using var secondDb = environment.CreateDbContext();
        var firstService = CreateOwnerRequestService(firstDb, Guid.NewGuid());
        var secondService = CreateOwnerRequestService(secondDb, Guid.NewGuid());
        var approval = new ApproveOwnerRequestRequest
        {
            PricePerNight = 1_400m,
            CheckInInstructions = "Use the key box"
        };

        var results = await Task.WhenAll(
            Record.ExceptionAsync(() => firstService.ApproveAsync(requestId, approval, CancellationToken.None)),
            Record.ExceptionAsync(() => secondService.ApproveAsync(requestId, approval, CancellationToken.None)));

        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is not null);

        await using var verificationDb = environment.CreateDbContext();
        Assert.Equal(1, await verificationDb.Apartments.CountAsync());
    }

    private static BookingService CreateBookingService(
        AppDbContext db,
        Guid tenantId,
        RecordingPaymentGateway gateway) =>
        new(
            db,
            new TestCurrentUser(tenantId, $"{tenantId:N}@example.test", "Tenant"),
            gateway,
            new RecordingEmailSender(),
            Options.Create(new BookingOptions()),
            Options.Create(new FrontendOptions { BaseUrl = "https://frontend.example.test" }),
            NullLogger<BookingService>.Instance);

    private static OwnerRequestService CreateOwnerRequestService(AppDbContext db, Guid managerId) =>
        new(
            db,
            new TestCurrentUser(managerId, $"{managerId:N}@example.test", "Manager"),
            new RecordingEmailSender());

    private static async Task<Guid> SeedApartmentAsync(PostgresTestEnvironment environment)
    {
        await using var db = environment.CreateDbContext();
        var apartment = new Apartment(
            Guid.NewGuid(),
            1,
            "Integration test apartment",
            "Description",
            "Kyiv",
            4,
            1_200m,
            "Use the key box");
        db.Apartments.Add(apartment);
        await db.SaveChangesAsync();
        return apartment.Id;
    }

    private static async Task<Guid> SeedOwnerRequestAsync(PostgresTestEnvironment environment)
    {
        await using var db = environment.CreateDbContext();
        var request = new OwnerRequest(
            Guid.NewGuid(),
            "Owner",
            "owner@example.test",
            "+380000000000",
            1,
            "Kyiv",
            "Owner apartment",
            "Description",
            3,
            1_200m,
            [],
            []);
        db.OwnerRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }
}
