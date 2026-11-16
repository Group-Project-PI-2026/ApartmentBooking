using ApartmentBookingDomain.Enums;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBookingDomain.Entities
{
    /// <summary>
    /// Request of a property owner to add their apartment to the platform.
    /// </summary>
    public class OwnerRequest
    {
        public Guid Id { get; private set; }
        /// <summary>Human-readable sequential number shown in the managers' table.</summary>
        public int Number { get; private set; }
        public Guid OwnerId { get; private set; }

        public string ContactName { get; private set; }
        public string ContactEmail { get; private set; }
        public string ContactPhone { get; private set; }

        public int DistrictId { get; private set; }
        public string Address { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public int MaxGuests { get; private set; }
        public decimal ExpectedPricePerNight { get; private set; }
        public List<string> PhotoUrls { get; private set; } = new();
        public List<string> Amenities { get; private set; } = new();

        public OwnerRequestStatus Status { get; private set; }
        public Guid? ApartmentId { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime UpdatedAt { get; private set; }

        public List<OwnerRequestHistoryEntry> History { get; private set; } = new();

        private OwnerRequest()
        {
            ContactName = string.Empty;
            ContactEmail = string.Empty;
            ContactPhone = string.Empty;
            Address = string.Empty;
            Title = string.Empty;
            Description = string.Empty;
        }

        public OwnerRequest(
            Guid ownerId,
            string contactName,
            string contactEmail,
            string contactPhone,
            int districtId,
            string address,
            string title,
            string description,
            int maxGuests,
            decimal expectedPricePerNight,
            IEnumerable<string>? photoUrls,
            IEnumerable<string>? amenities)
        {
            if (string.IsNullOrWhiteSpace(contactName))
                throw new DomainException("Contact name must not be empty.");
            if (string.IsNullOrWhiteSpace(contactEmail))
                throw new DomainException("Contact email must not be empty.");
            if (string.IsNullOrWhiteSpace(address))
                throw new DomainException("Address must not be empty.");
            if (string.IsNullOrWhiteSpace(title))
                throw new DomainException("Title must not be empty.");
            if (maxGuests <= 0)
                throw new DomainException("Max guests must be at least 1.");
            if (expectedPricePerNight < 0)
                throw new DomainException("Price cannot be negative.");

            Id = Guid.NewGuid();
            OwnerId = ownerId;
            ContactName = contactName;
            ContactEmail = contactEmail;
            ContactPhone = contactPhone ?? string.Empty;
            DistrictId = districtId;
            Address = address;
            Title = title;
            Description = description ?? string.Empty;
            MaxGuests = maxGuests;
            ExpectedPricePerNight = expectedPricePerNight;
            PhotoUrls = photoUrls?.ToList() ?? new List<string>();
            Amenities = amenities?.ToList() ?? new List<string>();
            Status = OwnerRequestStatus.New;
            CreatedAt = UpdatedAt = DateTime.UtcNow;

            History.Add(new OwnerRequestHistoryEntry(Id, OwnerRequestStatus.New, null, ownerId));
        }

        /// <summary>Approves the request and creates the apartment that should be persisted by the caller.</summary>
        public Apartment Approve(Guid managerId, decimal? pricePerNight, string? checkInInstructions, string? comment)
        {
            EnsureCanBeProcessed();

            var apartment = new Apartment(
                OwnerId,
                DistrictId,
                Title,
                Description,
                Address,
                MaxGuests,
                pricePerNight ?? ExpectedPricePerNight,
                checkInInstructions,
                PhotoUrls,
                Amenities);

            ApartmentId = apartment.Id;
            ChangeStatus(OwnerRequestStatus.Approved, comment, managerId);
            return apartment;
        }

        public void Reject(Guid managerId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new DomainException("Rejection reason must not be empty.");

            EnsureCanBeProcessed();
            ChangeStatus(OwnerRequestStatus.Rejected, reason, managerId);
        }

        public void RequestClarification(Guid managerId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new DomainException("Clarification message must not be empty.");

            EnsureCanBeProcessed();
            ChangeStatus(OwnerRequestStatus.NeedsClarification, message, managerId);
        }

        private void EnsureCanBeProcessed()
        {
            if (Status is OwnerRequestStatus.Approved or OwnerRequestStatus.Rejected)
                throw new DomainException($"Request is already {Status} and cannot be changed.");
        }

        private void ChangeStatus(OwnerRequestStatus status, string? message, Guid changedBy)
        {
            Status = status;
            UpdatedAt = DateTime.UtcNow;
            History.Add(new OwnerRequestHistoryEntry(Id, status, message, changedBy));
        }
    }
}
