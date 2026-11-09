namespace ApartmentBookingDomain.Entities
{
    public class Apartment
    {
        public Guid Id { get; private set; }
        public Guid OwnerId { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set;  }
        public string Address { get; private set; }
        public int MaxGuests { get; private set; }
        public decimal PricePerNight { get; private set; }
        public bool IsActive { get; private set; }
        
        private Apartment()
        {
            Title = string.Empty;
            Description = string.Empty;
            Address = string.Empty;
        }
        public Apartment(Guid ownerId, string title, string description, string address, int maxGuests, decimal pricePerNight)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title must not be empty.");
            if (string.IsNullOrWhiteSpace(address)) 
                throw new ArgumentException("Address must not be empty.");
            if (maxGuests <= 0) 
                throw new ArgumentException("Max guests must be at least 1.");
            if (pricePerNight < 0) 
                throw new ArgumentException("Price cannot be negative.");

            Id = Guid.NewGuid();
            OwnerId = ownerId;
            Title = title;
            Description = description;
            Address = address;
            MaxGuests = maxGuests;
            PricePerNight = pricePerNight;
            IsActive = true;
        }
        public void UpdatePrice(decimal newPrice)
        {
            if (newPrice < 0)
                throw new ArgumentException("Price cannot be negative.");
            PricePerNight = newPrice;
        }
        public void Deactivate()
        {
            IsActive = false;
        }
    }
}
