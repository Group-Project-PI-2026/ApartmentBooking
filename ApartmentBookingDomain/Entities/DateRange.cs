namespace ApartmentBookingDomain.Entities
{
    public record DateRange
    {
        public DateOnly CheckIn { get; init; }
        public DateOnly CheckOut { get; init; }

        public DateRange(DateOnly checkIn, DateOnly checkOut)
        {
            if (checkIn >= checkOut)
                throw new ArgumentException("Check-out date must be after check-in date.");
            if (checkIn < DateOnly.FromDateTime(DateTime.UtcNow))
                throw new ArgumentException("Check-in date cannot be in the past.");

            CheckIn = checkIn;
            CheckOut = checkOut;
        }
        public bool Overlaps(DateRange other)
        {
            return CheckIn < other.CheckOut && CheckOut > other.CheckIn;
        }
    }
}
