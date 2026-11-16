using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Exceptions;
using ApartmentBookingDomain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ApartmentBookingApplication.Apartments
{
    public class ApartmentService
    {
        private readonly IAppDbContext _db;

        public ApartmentService(IAppDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<DistrictDto>> GetDistrictsAsync(CancellationToken ct)
        {
            return await _db.Districts
                .AsNoTracking()
                .OrderBy(d => d.Name)
                .Select(d => new DistrictDto(d.Id, d.Name))
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<ApartmentCardDto>> SearchAsync(ApartmentSearchQuery query, CancellationToken ct)
        {
            if (query.CheckIn.HasValue != query.CheckOut.HasValue)
                throw new ValidationException("Both checkIn and checkOut must be specified.");
            if (query.CheckIn >= query.CheckOut)
                throw new ValidationException("checkOut must be after checkIn.");
            if (query.GuestsCount is <= 0)
                throw new ValidationException("guestsCount must be positive.");

            var apartments = _db.Apartments.AsNoTracking().Where(a => a.IsActive);

            if (query.DistrictId.HasValue)
                apartments = apartments.Where(a => a.DistrictId == query.DistrictId.Value);

            if (query.GuestsCount.HasValue)
                apartments = apartments.Where(a => a.MaxGuests >= query.GuestsCount.Value);

            int? nights = null;
            if (query.CheckIn.HasValue && query.CheckOut.HasValue)
            {
                var checkIn = query.CheckIn.Value;
                var checkOut = query.CheckOut.Value;
                var now = DateTime.UtcNow;
                nights = checkOut.DayNumber - checkIn.DayNumber;

                // Exclude apartments that intersect confirmed bookings or active temporary locks.
                apartments = apartments.Where(a =>
                    !_db.Bookings.Any(b =>
                        b.ApartmentId == a.Id &&
                        b.Status != BookingStatus.Cancelled &&
                        b.Dates.CheckIn < checkOut && b.Dates.CheckOut > checkIn) &&
                    !_db.BookingLocks.Any(l =>
                        l.ApartmentId == a.Id &&
                        l.ExpiresAt > now &&
                        l.Dates.CheckIn < checkOut && l.Dates.CheckOut > checkIn));
            }

            var rows = await (
                from a in apartments
                join d in _db.Districts on a.DistrictId equals d.Id
                orderby a.PricePerNight
                select new
                {
                    a.Id,
                    a.PhotoUrls,
                    a.Title,
                    a.DistrictId,
                    DistrictName = d.Name,
                    a.PricePerNight,
                    a.MaxGuests,
                    a.Amenities
                }).ToListAsync(ct);

            return rows.Select(r => new ApartmentCardDto(
                    r.Id,
                    r.PhotoUrls.FirstOrDefault(),
                    r.Title,
                    r.DistrictId,
                    r.DistrictName,
                    r.PricePerNight,
                    nights.HasValue ? r.PricePerNight * nights.Value : null,
                    r.MaxGuests,
                    r.Amenities))
                .ToList();
        }

        public async Task<ApartmentDetailsDto> GetByIdAsync(Guid id, CancellationToken ct)
        {
            var result = await (
                from a in _db.Apartments.AsNoTracking()
                join d in _db.Districts on a.DistrictId equals d.Id
                where a.Id == id && a.IsActive
                select new ApartmentDetailsDto(
                    a.Id, a.Title, a.Description, a.Address, a.DistrictId, d.Name,
                    a.MaxGuests, a.PricePerNight, a.PhotoUrls, a.Amenities))
                .FirstOrDefaultAsync(ct);

            return result ?? throw new NotFoundException("Apartment", id);
        }
    }
}
