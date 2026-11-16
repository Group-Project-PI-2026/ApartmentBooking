using System.ComponentModel.DataAnnotations;
using ApartmentBookingDomain.Enums;

namespace ApartmentBookingApplication.Bookings
{
    public enum BookingTab
    {
        Future,
        Past,
        Cancelled
    }

    public class ReserveBookingRequest
    {
        [Required]
        public Guid ApartmentId { get; set; }

        [Required]
        public DateOnly CheckIn { get; set; }

        [Required]
        public DateOnly CheckOut { get; set; }

        [Range(1, 100)]
        public int GuestsCount { get; set; } = 1;

        /// <summary>Pay the whole price now instead of the prepayment only.</summary>
        public bool PayInFull { get; set; }
    }

    public record ReserveBookingResponse(
        Guid LockId,
        DateTime ExpiresAt,
        string CheckoutUrl,
        decimal TotalPrice,
        decimal AmountToPay);

    public record CheckoutResponse(string CheckoutUrl, decimal Amount);

    public record BookingListItemDto(
        Guid Id,
        Guid ApartmentId,
        string ApartmentTitle,
        string ApartmentAddress,
        string? PhotoUrl,
        DateOnly CheckIn,
        DateOnly CheckOut,
        int Nights,
        decimal TotalPrice,
        decimal PaidAmount,
        decimal BalanceDue,
        BookingStatus Status);

    public record BookingDetailsDto(
        Guid Id,
        Guid ApartmentId,
        string ApartmentTitle,
        string ApartmentAddress,
        string? PhotoUrl,
        DateOnly CheckIn,
        DateOnly CheckOut,
        int Nights,
        decimal PricePerNight,
        decimal TotalPrice,
        decimal PaidAmount,
        decimal BalanceDue,
        BookingStatus Status,
        DateTime CreatedAt,
        bool CanPayBalance,
        bool CanCancel,
        string? CheckInInstructions, // only on/after the check-in day of a fully paid booking
        DateTime? CancelledAt,
        decimal PenaltyAmount,
        decimal RefundAmount,
        RefundStatus RefundStatus);

    public record RefundPreviewDto(
        Guid BookingId,
        decimal TotalPrice,
        decimal PaidAmount,
        decimal PenaltyAmount,
        decimal RefundAmount,
        bool CanCancel,
        string? Reason);

    public record CancelBookingResponse(
        Guid BookingId,
        BookingStatus Status,
        decimal PenaltyAmount,
        decimal RefundAmount,
        RefundStatus RefundStatus);
}
