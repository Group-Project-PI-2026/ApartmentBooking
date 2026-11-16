using ApartmentBookingApplication.Options;
using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;

namespace ApartmentBookingApplication.Bookings
{
    /// <summary>
    /// Free cancellation if at least <see cref="BookingOptions.FreeCancellationDays"/> days before check-in,
    /// otherwise a penalty of <see cref="BookingOptions.LateCancellationPenaltyPercent"/> % of the total price
    /// (never more than what was actually paid). A stay that has already started cannot be cancelled.
    /// </summary>
    public static class CancellationPolicy
    {
        public static RefundPreviewDto Calculate(Booking booking, DateOnly today, BookingOptions options)
        {
            if (booking.Status == BookingStatus.Cancelled)
                return Denied(booking, "Booking is already cancelled.");

            if (today >= booking.Dates.CheckIn)
                return Denied(booking, "The stay has already started and cannot be cancelled.");

            var daysBeforeCheckIn = booking.Dates.CheckIn.DayNumber - today.DayNumber;

            var penalty = daysBeforeCheckIn >= options.FreeCancellationDays
                ? 0m
                : Math.Round(booking.TotalPrice * options.LateCancellationPenaltyPercent / 100m, 2);

            penalty = Math.Min(penalty, booking.PaidAmount);
            var refund = booking.PaidAmount - penalty;

            return new RefundPreviewDto(booking.Id, booking.TotalPrice, booking.PaidAmount, penalty, refund, true, null);
        }

        private static RefundPreviewDto Denied(Booking booking, string reason) =>
            new(booking.Id, booking.TotalPrice, booking.PaidAmount, 0, 0, false, reason);
    }
}
