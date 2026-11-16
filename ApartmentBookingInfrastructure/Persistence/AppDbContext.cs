using ApartmentBookingApplication.Abstractions;
using ApartmentBookingDomain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentBookingInfrastructure.Persistence
{
    public class AppDbContext : DbContext, IAppDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Apartment> Apartments => Set<Apartment>();
        public DbSet<District> Districts => Set<District>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<BookingLock> BookingLocks => Set<BookingLock>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<OwnerRequest> OwnerRequests => Set<OwnerRequest>();

        public async Task<Apartment?> GetApartmentForUpdateAsync(Guid apartmentId, CancellationToken cancellationToken = default)
        {
            return await Apartments
                .FromSql($"SELECT * FROM apartments WHERE id = {apartmentId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }
}
