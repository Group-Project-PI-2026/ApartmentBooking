using ApartmentBookingDomain.Enums;

namespace ApartmentBookingDomain.Entities
{
    public class OwnerRequestHistoryEntry
    {
        public Guid Id { get; private set; }
        public Guid OwnerRequestId { get; private set; }
        public OwnerRequestStatus Status { get; private set; }
        public string? Message { get; private set; }
        public Guid ChangedBy { get; private set; }
        public DateTime ChangedAt { get; private set; }

        private OwnerRequestHistoryEntry() { }

        public OwnerRequestHistoryEntry(Guid ownerRequestId, OwnerRequestStatus status, string? message, Guid changedBy)
        {
            Id = Guid.NewGuid();
            OwnerRequestId = ownerRequestId;
            Status = status;
            Message = message;
            ChangedBy = changedBy;
            ChangedAt = DateTime.UtcNow;
        }
    }
}
