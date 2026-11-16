using ApartmentBookingDomain.Exceptions;

namespace ApartmentBookingDomain.Entities
{
    public record DateRange
    {
        public DateOnly CheckIn { get; init; }
        public DateOnly CheckOut { get; init; }

        public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

        // NOTE: this constructor is also used by EF Core when materializing existing rows,
        // so it must not reject past dates (otherwise old bookings could not be loaded).
        public DateRange(DateOnly checkIn, DateOnly checkOut)
        {
            if (checkIn >= checkOut)
                throw new DomainException("Check-out date must be after check-in date.");

            CheckIn = checkIn;
            CheckOut = checkOut;
        }

        /// <summary>Creates a range for a new stay; check-in cannot be in the past.</summary>
        public static DateRange CreateUpcoming(DateOnly checkIn, DateOnly checkOut, DateOnly today)
        {
            if (checkIn < today)
                throw new DomainException("Check-in date cannot be in the past.");

            return new DateRange(checkIn, checkOut);
        }

        public bool Overlaps(DateRange other)
        {
            return CheckIn < other.CheckOut && CheckOut > other.CheckIn;
        }
    }
}
