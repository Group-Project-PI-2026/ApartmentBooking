namespace ApartmentBookingDomain.Entities
{
    public class Apartment
    {
        public Guid Id { get; private set; }
        public Guid OwnerId { get; private set; }

        public string Description { get; private set;  }
        public string Address { get; private set; }
        public int MaxGuests { get; private set; }
        public decimal PricePerNight { get; private set; }
        public bool IsActive { get; private set; }
        
        private Apartment()
        {
            Description = string.Empty;
            Address = string.Empty;
        }
        public Apartment(Guid ownerId, string title, string description, string address, int maxGuests, decimal pricePerNight)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title must not be empty.");
        }
        public void UpdatePrice(decimal newPrice)
        {
            if (newPrice < 0)
                throw new ArgumentException("Price cannot be negative.");
            PricePerNight = newPrice;
        }

    }
}
