using System.Globalization;
using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Common;
using ApartmentBookingApplication.Exceptions;
using ApartmentBookingApplication.Options;
using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentBookingApplication.Bookings
{
    /// <summary>Tenant's bookings: reservation, list/details, balance payment, cancellation.</summary>
    public class BookingService
    {
        private readonly IAppDbContext _db;
        private readonly ICurrentUser _currentUser;
        private readonly IPaymentGateway _paymentGateway;
        private readonly IEmailSender _emailSender;
        private readonly BookingOptions _bookingOptions;
        private readonly FrontendOptions _frontendOptions;
        private readonly ILogger<BookingService> _logger;

        public BookingService(
            IAppDbContext db,
            ICurrentUser currentUser,
            IPaymentGateway paymentGateway,
            IEmailSender emailSender,
            IOptions<BookingOptions> bookingOptions,
            IOptions<FrontendOptions> frontendOptions,
            ILogger<BookingService> logger)
        {
            _db = db;
            _currentUser = currentUser;
            _paymentGateway = paymentGateway;
            _emailSender = emailSender;
            _bookingOptions = bookingOptions.Value;
            _frontendOptions = frontendOptions.Value;
            _logger = logger;
        }

        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        public async Task<ReserveBookingResponse> ReserveAsync(ReserveBookingRequest request, CancellationToken ct)
        {
            var tenantId = _currentUser.UserId;
            var dates = DateRange.CreateUpcoming(request.CheckIn, request.CheckOut, Today);

            BookingLock bookingLock;
            Apartment apartment;

            await using (var transaction = await _db.Database.BeginTransactionAsync(ct))
            {
                // Row lock on the apartment serializes concurrent reservations of the same apartment.
                apartment = await _db.GetApartmentForUpdateAsync(request.ApartmentId, ct)
                    ?? throw new NotFoundException("Apartment", request.ApartmentId);

                if (!apartment.IsActive)
                    throw new NotFoundException("Apartment", request.ApartmentId);
                if (request.GuestsCount > apartment.MaxGuests)
                    throw new ValidationException($"This apartment accommodates at most {apartment.MaxGuests} guests.");

                // A tenant holds at most one reservation per apartment: drop their previous unfinished ones.
                await _db.BookingLocks
                    .Where(l => l.ApartmentId == apartment.Id && l.TenantId == tenantId)
                    .ExecuteDeleteAsync(ct);

                if (await _db.IsOccupiedAsync(apartment.Id, dates, excludeLockId: null, ct))
                    throw new ConflictException("Selected dates are no longer available.");

                var totalPrice = apartment.CalculatePrice(dates);
                if (totalPrice <= 0)
                    throw new ValidationException("Total price must be positive.");

                var amountToPay = request.PayInFull
                    ? totalPrice
                    : Math.Round(totalPrice * _bookingOptions.DepositPercent / 100m, 2);
                if (amountToPay <= 0 || amountToPay > totalPrice)
                    amountToPay = totalPrice;

                bookingLock = new BookingLock(apartment.Id, tenantId, _currentUser.Email, dates, totalPrice, amountToPay);
                _db.BookingLocks.Add(bookingLock);
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }

            // The external call is made outside the DB transaction so the row lock is not held while waiting for Stripe.
            CheckoutSessionResult session;
            try
            {
                session = await _paymentGateway.CreateCheckoutSessionAsync(new CheckoutSessionRequest(
                    ProductName: $"{apartment.Title}: {dates.CheckIn:dd.MM.yyyy} – {dates.CheckOut:dd.MM.yyyy}",
                    Amount: bookingLock.AmountToPay,
                    CustomerEmail: _currentUser.Email,
                    SuccessUrl: $"{_frontendOptions.BaseUrl}/bookings/payment-success?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl: $"{_frontendOptions.BaseUrl}/apartments/{apartment.Id}",
                    Metadata: new Dictionary<string, string>
                    {
                        [PaymentMetadata.Kind] = PaymentMetadata.KindReservation,
                        [PaymentMetadata.LockId] = bookingLock.Id.ToString(),
                        [PaymentMetadata.ApartmentId] = apartment.Id.ToString(),
                        [PaymentMetadata.TenantId] = tenantId.ToString(),
                        [PaymentMetadata.CheckIn] = dates.CheckIn.ToString(PaymentMetadata.DateFormat, CultureInfo.InvariantCulture),
                        [PaymentMetadata.CheckOut] = dates.CheckOut.ToString(PaymentMetadata.DateFormat, CultureInfo.InvariantCulture),
                        [PaymentMetadata.TotalPrice] = bookingLock.TotalPrice.ToString(CultureInfo.InvariantCulture)
                    }), ct);
            }
            catch
            {
                // Release the dates immediately if the payment page could not be created.
                _db.BookingLocks.Remove(bookingLock);
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            bookingLock.AttachCheckoutSession(session.SessionId);
            await _db.SaveChangesAsync(ct);

            return new ReserveBookingResponse(
                bookingLock.Id, bookingLock.ExpiresAt, session.Url, bookingLock.TotalPrice, bookingLock.AmountToPay);
        }

        public async Task<IReadOnlyList<BookingListItemDto>> GetMyAsync(BookingTab? tab, CancellationToken ct)
        {
            var tenantId = _currentUser.UserId;
            var today = Today;

            var bookings = _db.Bookings.AsNoTracking().Where(b => b.TenantId == tenantId);

            bookings = tab switch
            {
                BookingTab.Future => bookings.Where(b => b.Status != BookingStatus.Cancelled && b.Dates.CheckOut > today),
                BookingTab.Past => bookings.Where(b => b.Status != BookingStatus.Cancelled && b.Dates.CheckOut <= today),
                BookingTab.Cancelled => bookings.Where(b => b.Status == BookingStatus.Cancelled),
                _ => bookings
            };

            var rows = await (
                from b in bookings
                join a in _db.Apartments on b.ApartmentId equals a.Id
                orderby b.Dates.CheckIn
                select new { Booking = b, a.Title, a.Address, a.PhotoUrls })
                .ToListAsync(ct);

            return rows.Select(r => new BookingListItemDto(
                    r.Booking.Id,
                    r.Booking.ApartmentId,
                    r.Title,
                    r.Address,
                    r.PhotoUrls.FirstOrDefault(),
                    r.Booking.Dates.CheckIn,
                    r.Booking.Dates.CheckOut,
                    r.Booking.Dates.Nights,
                    r.Booking.TotalPrice,
                    r.Booking.PaidAmount,
                    r.Booking.BalanceDue,
                    r.Booking.Status))
                .ToList();
        }

        public async Task<BookingDetailsDto> GetByIdAsync(Guid id, CancellationToken ct)
        {
            var booking = await GetOwnBookingAsync(id, tracking: false, ct);
            var apartment = await _db.Apartments.AsNoTracking().FirstAsync(a => a.Id == booking.ApartmentId, ct);
            var today = Today;

            var isActive = booking.Status != BookingStatus.Cancelled;
            var stayInProgress = today >= booking.Dates.CheckIn && today <= booking.Dates.CheckOut;
            var instructions = isActive && booking.BalanceDue == 0 && stayInProgress
                ? apartment.CheckInInstructions
                : null;

            var preview = CancellationPolicy.Calculate(booking, today, _bookingOptions);

            return new BookingDetailsDto(
                booking.Id,
                booking.ApartmentId,
                apartment.Title,
                apartment.Address,
                apartment.PhotoUrls.FirstOrDefault(),
                booking.Dates.CheckIn,
                booking.Dates.CheckOut,
                booking.Dates.Nights,
                Math.Round(booking.TotalPrice / booking.Dates.Nights, 2),
                booking.TotalPrice,
                booking.PaidAmount,
                booking.BalanceDue,
                booking.Status,
                booking.CreatedAt,
                CanPayBalance: isActive && booking.BalanceDue > 0 && today < booking.Dates.CheckOut,
                CanCancel: preview.CanCancel,
                CheckInInstructions: instructions,
                booking.CancelledAt,
                booking.PenaltyAmount,
                booking.RefundAmount,
                booking.RefundStatus);
        }

        public async Task<CheckoutResponse> PayBalanceAsync(Guid id, CancellationToken ct)
        {
            var booking = await GetOwnBookingAsync(id, tracking: false, ct);

            if (booking.Status == BookingStatus.Cancelled)
                throw new ConflictException("Booking is cancelled.");
            if (booking.BalanceDue <= 0)
                throw new ConflictException("Booking is already fully paid.");
            if (Today >= booking.Dates.CheckOut)
                throw new ConflictException("The stay is already over.");

            var apartmentTitle = await _db.Apartments
                .Where(a => a.Id == booking.ApartmentId)
                .Select(a => a.Title)
                .FirstAsync(ct);

            var session = await _paymentGateway.CreateCheckoutSessionAsync(new CheckoutSessionRequest(
                ProductName: $"{apartmentTitle}: доплата залишку",
                Amount: booking.BalanceDue,
                CustomerEmail: _currentUser.Email ?? booking.TenantEmail,
                SuccessUrl: $"{_frontendOptions.BaseUrl}/bookings/{booking.Id}?payment=success",
                CancelUrl: $"{_frontendOptions.BaseUrl}/bookings/{booking.Id}",
                Metadata: new Dictionary<string, string>
                {
                    [PaymentMetadata.Kind] = PaymentMetadata.KindBalance,
                    [PaymentMetadata.BookingId] = booking.Id.ToString()
                }), ct);

            return new CheckoutResponse(session.Url, booking.BalanceDue);
        }

        public async Task<RefundPreviewDto> GetRefundPreviewAsync(Guid id, CancellationToken ct)
        {
            var booking = await GetOwnBookingAsync(id, tracking: false, ct);
            return CancellationPolicy.Calculate(booking, Today, _bookingOptions);
        }

        public async Task<CancelBookingResponse> CancelAsync(Guid id, CancellationToken ct)
        {
            var booking = await GetOwnBookingAsync(id, tracking: true, ct);
            var preview = CancellationPolicy.Calculate(booking, Today, _bookingOptions);

            if (!preview.CanCancel)
                throw new ConflictException(preview.Reason ?? "Booking cannot be cancelled.");

            // 1. Persist the cancellation first: the dates are released and the refund is recorded as Pending,
            //    so a payment provider failure can never leave the booking in an unknown state.
            booking.Cancel(preview.PenaltyAmount, preview.RefundAmount);
            await _db.SaveChangesAsync(ct); // optimistic concurrency (xmin) protects against double cancel

            // 2. Refund through the payment gateway.
            if (preview.RefundAmount > 0)
            {
                await RefundBookingAsync(booking, preview.RefundAmount, ct);
                await _db.SaveChangesAsync(CancellationToken.None);
            }

            if (!string.IsNullOrEmpty(booking.TenantEmail))
            {
                await _emailSender.SendAsync(
                    booking.TenantEmail,
                    "Бронювання скасовано",
                    $"Ваше бронювання {booking.Id} ({booking.Dates.CheckIn:dd.MM.yyyy} – {booking.Dates.CheckOut:dd.MM.yyyy}) скасовано. " +
                    $"Сума до повернення: {booking.RefundAmount:0.00} {_bookingOptions.Currency.ToUpperInvariant()}.",
                    CancellationToken.None);
            }

            return new CancelBookingResponse(booking.Id, booking.Status, booking.PenaltyAmount, booking.RefundAmount, booking.RefundStatus);
        }

        private async Task RefundBookingAsync(Booking booking, decimal refundAmount, CancellationToken ct)
        {
            var payments = await _db.Payments
                .Where(p => p.BookingId == booking.Id)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(ct);

            var remaining = refundAmount;
            try
            {
                foreach (var payment in payments)
                {
                    if (remaining <= 0)
                        break;

                    var part = Math.Min(remaining, payment.RefundableAmount);
                    if (part <= 0)
                        continue;
                    if (string.IsNullOrEmpty(payment.PaymentIntentId))
                        throw new PaymentGatewayException($"Payment {payment.Id} has no PaymentIntent to refund.");

                    await _paymentGateway.RefundAsync(
                        payment.PaymentIntentId, part, $"refund-{booking.Id}-{payment.Id}", CancellationToken.None);

                    payment.RegisterRefund(part);
                    remaining -= part;
                }

                if (remaining > 0)
                    throw new PaymentGatewayException($"Could not distribute refund, {remaining} left.");

                booking.MarkRefundSucceeded();
            }
            catch (Exception ex)
            {
                // The booking stays cancelled; the failed refund is visible (RefundStatus = Failed) and can be retried manually.
                _logger.LogError(ex, "Refund for booking {BookingId} failed", booking.Id);
                booking.MarkRefundFailed();
            }
        }

        private async Task<Booking> GetOwnBookingAsync(Guid id, bool tracking, CancellationToken ct)
        {
            var query = tracking ? _db.Bookings : _db.Bookings.AsNoTracking();
            var booking = await query.FirstOrDefaultAsync(b => b.Id == id, ct);

            // Someone else's booking is reported as "not found" to avoid leaking ids.
            if (booking is null || booking.TenantId != _currentUser.UserId)
                throw new NotFoundException("Booking", id);

            return booking;
        }
    }
}
