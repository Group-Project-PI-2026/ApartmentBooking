using ApartmentBookingApplication.Abstractions;
using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ApartmentBookingApplication.Common
{
    internal static class AvailabilityQueries
    {
        /// <summary>
        /// True if the dates intersect a non-cancelled booking or an active (not expired) lock.
        /// </summary>
        public static async Task<bool> IsOccupiedAsync(
            this IAppDbContext db,
            Guid apartmentId,
            DateRange dates,
            Guid? excludeLockId,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var checkIn = dates.CheckIn;
            var checkOut = dates.CheckOut;

            var bookingOverlap = await db.Bookings.AnyAsync(b =>
                b.ApartmentId == apartmentId &&
                b.Status != BookingStatus.Cancelled &&
                b.Dates.CheckIn < checkOut && b.Dates.CheckOut > checkIn, ct);

            if (bookingOverlap)
                return true;

            return await db.BookingLocks.AnyAsync(l =>
                l.ApartmentId == apartmentId &&
                l.ExpiresAt > now &&
                (excludeLockId == null || l.Id != excludeLockId) &&
                l.Dates.CheckIn < checkOut && l.Dates.CheckOut > checkIn, ct);
        }
    }
}
