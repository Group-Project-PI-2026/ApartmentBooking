using ApartmentBookingDomain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ApartmentBookingApplication.Abstractions
{
    public interface IAppDbContext
    {
        DbSet<Apartment> Apartments { get; }
        DbSet<District> Districts { get; }
        DbSet<Booking> Bookings { get; }
        DbSet<BookingLock> BookingLocks { get; }
        DbSet<Payment> Payments { get; }
        DbSet<OwnerRequest> OwnerRequests { get; }

        DatabaseFacade Database { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads the apartment with a row-level lock (SELECT ... FOR UPDATE).
        /// Must be called inside a transaction. All operations that change the calendar of an
        /// apartment take this lock first, so they are serialized per apartment and
        /// double bookings are impossible.
        /// </summary>
        Task<Apartment?> GetApartmentForUpdateAsync(Guid apartmentId, CancellationToken cancellationToken = default);
    }
}
