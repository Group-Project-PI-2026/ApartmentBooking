using ApartmentBookingDomain.Exceptions;

namespace ApartmentBookingDomain.Entities
{
    public class Apartment
    {
        public Guid Id { get; private set; }
        public Guid OwnerId { get; private set; }
        public int DistrictId { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public string Address { get; private set; }
        public int MaxGuests { get; private set; }
        public decimal PricePerNight { get; private set; }
        public string CheckInInstructions { get; private set; }
        public List<string> PhotoUrls { get; private set; } = new();
        public List<string> Amenities { get; private set; } = new();
        public bool IsActive { get; private set; }
        public DateTime CreatedAt { get; private set; }

        private Apartment()
        {
            Title = string.Empty;
            Description = string.Empty;
            Address = string.Empty;
            CheckInInstructions = string.Empty;
        }

        public Apartment(
            Guid ownerId,
            int districtId,
            string title,
            string description,
            string address,
            int maxGuests,
            decimal pricePerNight,
            string? checkInInstructions = null,
            IEnumerable<string>? photoUrls = null,
            IEnumerable<string>? amenities = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new DomainException("Title must not be empty.");
            if (string.IsNullOrWhiteSpace(address))
                throw new DomainException("Address must not be empty.");
            if (maxGuests <= 0)
                throw new DomainException("Max guests must be at least 1.");
            if (pricePerNight < 0)
                throw new DomainException("Price cannot be negative.");

            Id = Guid.NewGuid();
            OwnerId = ownerId;
            DistrictId = districtId;
            Title = title;
            Description = description ?? string.Empty;
            Address = address;
            MaxGuests = maxGuests;
            PricePerNight = pricePerNight;
            CheckInInstructions = checkInInstructions ?? string.Empty;
            PhotoUrls = photoUrls?.ToList() ?? new List<string>();
            Amenities = amenities?.ToList() ?? new List<string>();
            IsActive = true;
            CreatedAt = DateTime.UtcNow;
        }

        public decimal CalculatePrice(DateRange dates)
        {
            return PricePerNight * dates.Nights;
        }

        public void UpdatePrice(decimal newPrice)
        {
            if (newPrice < 0)
                throw new DomainException("Price cannot be negative.");
            PricePerNight = newPrice;
        }

        public void UpdateCheckInInstructions(string instructions)
        {
            CheckInInstructions = instructions ?? string.Empty;
        }

        public void Activate()
        {
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }
    }
}
