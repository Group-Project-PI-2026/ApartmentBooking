using System.ComponentModel.DataAnnotations;
using ApartmentBookingDomain.Enums;

namespace ApartmentBookingApplication.OwnerRequests
{
    public class CreateOwnerRequestRequest
    {
        [Required, MaxLength(200)]
        public string ContactName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(200)]
        public string ContactEmail { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string ContactPhone { get; set; } = string.Empty;

        [Required]
        public int DistrictId { get; set; }

        [Required, MaxLength(300)]
        public string Address { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(4000)]
        public string Description { get; set; } = string.Empty;

        [Range(1, 100)]
        public int MaxGuests { get; set; }

        [Range(0, 1_000_000)]
        public decimal ExpectedPricePerNight { get; set; }

        public List<string> PhotoUrls { get; set; } = new();

        public List<string> Amenities { get; set; } = new();
    }

    public class ApproveOwnerRequestRequest
    {
        /// <summary>Final price per night; the owner's expected price is used when omitted.</summary>
        [Range(0, 1_000_000)]
        public decimal? PricePerNight { get; set; }

        [MaxLength(4000)]
        public string? CheckInInstructions { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }
    }

    /// <summary>Body for reject / clarify.</summary>
    public class OwnerRequestMessageRequest
    {
        [Required, MaxLength(2000)]
        public string Message { get; set; } = string.Empty;
    }

    public class OwnerRequestListQuery
    {
        public string? Search { get; set; }
        public OwnerRequestStatus? Status { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public record OwnerRequestListItemDto(
        Guid Id,
        int Number,
        string ContactName,
        string ContactPhone,
        string Address,
        string DistrictName,
        OwnerRequestStatus Status,
        DateTime CreatedAt);

    public record OwnerRequestHistoryDto(OwnerRequestStatus Status, string? Message, Guid ChangedBy, DateTime ChangedAt);

    public record OwnerRequestDetailsDto(
        Guid Id,
        int Number,
        Guid OwnerId,
        string ContactName,
        string ContactEmail,
        string ContactPhone,
        int DistrictId,
        string DistrictName,
        string Address,
        string Title,
        string Description,
        int MaxGuests,
        decimal ExpectedPricePerNight,
        IReadOnlyList<string> PhotoUrls,
        IReadOnlyList<string> Amenities,
        OwnerRequestStatus Status,
        Guid? ApartmentId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<OwnerRequestHistoryDto> History);
}
