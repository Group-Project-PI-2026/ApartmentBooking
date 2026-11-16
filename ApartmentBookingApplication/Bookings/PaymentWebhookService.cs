using System.Globalization;
using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Common;
using ApartmentBookingApplication.Options;
using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentBookingApplication.Bookings
{
    /// <summary>
    /// Handles payment provider webhooks: turns a paid booking_lock into a Booking,
    /// and registers balance payments. Processing is idempotent (Stripe may deliver an event more than once).
    /// </summary>
    public class PaymentWebhookService
    {
        public const string CheckoutSessionCompleted = "checkout.session.completed";

        private readonly IAppDbContext _db;
        private readonly IPaymentGateway _paymentGateway;
        private readonly IEmailSender _emailSender;
        private readonly BookingOptions _bookingOptions;
        private readonly ILogger<PaymentWebhookService> _logger;

        public PaymentWebhookService(
            IAppDbContext db,
            IPaymentGateway paymentGateway,
            IEmailSender emailSender,
            IOptions<BookingOptions> bookingOptions,
            ILogger<PaymentWebhookService> logger)
        {
            _db = db;
            _paymentGateway = paymentGateway;
            _emailSender = emailSender;
            _bookingOptions = bookingOptions.Value;
            _logger = logger;
        }

        public async Task HandleAsync(string payload, string? signature, CancellationToken ct)
        {
            var evt = _paymentGateway.ParseWebhookEvent(payload, signature);

            if (evt.Type != CheckoutSessionCompleted || evt.PaymentStatus != "paid" || evt.SessionId is null)
            {
                _logger.LogInformation("Ignoring payment event {EventId} of type {Type}", evt.EventId, evt.Type);
                return;
            }

            if (await _db.Payments.AnyAsync(p => p.CheckoutSessionId == evt.SessionId, ct))
            {
                _logger.LogInformation("Checkout session {SessionId} already processed", evt.SessionId);
                return;
            }

            evt.Metadata.TryGetValue(PaymentMetadata.Kind, out var kind);
            switch (kind)
            {
                case PaymentMetadata.KindReservation:
                    await HandleReservationPaidAsync(evt, ct);
                    break;
                case PaymentMetadata.KindBalance:
                    await HandleBalancePaidAsync(evt, ct);
                    break;
                default:
                    _logger.LogWarning("Checkout session {SessionId} has unknown kind '{Kind}'", evt.SessionId, kind);
                    break;
            }
        }

        private async Task HandleReservationPaidAsync(PaymentWebhookEvent evt, CancellationToken ct)
        {
            var lockId = Guid.Parse(evt.Metadata[PaymentMetadata.LockId]);
            var apartmentId = Guid.Parse(evt.Metadata[PaymentMetadata.ApartmentId]);
            var tenantId = Guid.Parse(evt.Metadata[PaymentMetadata.TenantId]);
            var dates = new DateRange(
                DateOnly.ParseExact(evt.Metadata[PaymentMetadata.CheckIn], PaymentMetadata.DateFormat, CultureInfo.InvariantCulture),
                DateOnly.ParseExact(evt.Metadata[PaymentMetadata.CheckOut], PaymentMetadata.DateFormat, CultureInfo.InvariantCulture));
            var totalPrice = decimal.Parse(evt.Metadata[PaymentMetadata.TotalPrice], CultureInfo.InvariantCulture);

            Booking? booking = null;

            await using (var transaction = await _db.Database.BeginTransactionAsync(ct))
            {
                await _db.GetApartmentForUpdateAsync(apartmentId, ct);

                // Re-check under the apartment lock in case of concurrent webhook deliveries.
                if (await _db.Payments.AnyAsync(p => p.CheckoutSessionId == evt.SessionId, ct))
                    return;

                var bookingLock = await _db.BookingLocks.FirstOrDefaultAsync(l => l.Id == lockId, ct);

                // The lock may have expired (payment took longer than 15 minutes) or been cleaned up.
                // The booking is still created if nobody else has taken the dates in the meantime.
                var occupied = await _db.IsOccupiedAsync(apartmentId, dates, excludeLockId: lockId, ct);

                if (bookingLock is not null)
                    _db.BookingLocks.Remove(bookingLock);

                if (!occupied)
                {
                    booking = new Booking(apartmentId, tenantId, bookingLock?.TenantEmail ?? evt.CustomerEmail, dates, totalPrice, evt.Amount);
                    _db.Bookings.Add(booking);
                    _db.Payments.Add(new Payment(booking.Id, PaymentType.Reservation, evt.Amount, evt.SessionId!, evt.PaymentIntentId));
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }

            if (booking is null)
            {
                _logger.LogWarning(
                    "Dates for lock {LockId} were taken before payment {SessionId} completed; refunding",
                    lockId, evt.SessionId);
                await RefundWholePaymentAsync(evt, ct);
                return;
            }

            _logger.LogInformation("Booking {BookingId} created from lock {LockId}", booking.Id, lockId);

            if (!string.IsNullOrEmpty(booking.TenantEmail))
            {
                await _emailSender.SendAsync(
                    booking.TenantEmail,
                    "Бронювання підтверджено",
                    $"Ваше бронювання {booking.Id} на {booking.Dates.CheckIn:dd.MM.yyyy} – {booking.Dates.CheckOut:dd.MM.yyyy} підтверджено. " +
                    $"Сплачено {booking.PaidAmount:0.00} з {booking.TotalPrice:0.00} {_bookingOptions.Currency.ToUpperInvariant()}. " +
                    "Інструкції із заселення будуть доступні в особистому кабінеті в день заїзду.",
                    CancellationToken.None);
            }
        }

        private async Task HandleBalancePaidAsync(PaymentWebhookEvent evt, CancellationToken ct)
        {
            var bookingId = Guid.Parse(evt.Metadata[PaymentMetadata.BookingId]);
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

            if (booking is null || booking.Status == BookingStatus.Cancelled || booking.BalanceDue <= 0)
            {
                // Booking was cancelled or already paid (e.g. two balance sessions were paid) — return the money.
                _logger.LogWarning("Balance payment {SessionId} for booking {BookingId} is not applicable; refunding",
                    evt.SessionId, bookingId);
                await RefundWholePaymentAsync(evt, ct);
                return;
            }

            booking.RegisterPayment(evt.Amount);
            _db.Payments.Add(new Payment(booking.Id, PaymentType.Balance, evt.Amount, evt.SessionId!, evt.PaymentIntentId));
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Balance {Amount} registered for booking {BookingId}", evt.Amount, booking.Id);
        }

        private async Task RefundWholePaymentAsync(PaymentWebhookEvent evt, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(evt.PaymentIntentId))
            {
                _logger.LogError("Cannot refund session {SessionId}: no PaymentIntent", evt.SessionId);
                return;
            }

            // Idempotency key makes repeated webhook deliveries safe.
            await _paymentGateway.RefundAsync(evt.PaymentIntentId, evt.Amount, $"refund-session-{evt.SessionId}", ct);
        }
    }
}
