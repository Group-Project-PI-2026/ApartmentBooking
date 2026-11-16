namespace ApartmentBookingApplication.Apartments
{
    public record ApartmentSearchQuery(int? DistrictId, DateOnly? CheckIn, DateOnly? CheckOut, int? GuestsCount);

    public record DistrictDto(int Id, string Name);

    public record ApartmentCardDto(
        Guid Id,
        string? PhotoUrl,
        string Title,
        int DistrictId,
        string DistrictName,
        decimal PricePerNight,
        decimal? TotalPrice, // total for the requested dates, null when dates were not specified

        int MaxGuests,
        IReadOnlyList<string> Amenities);

    public record ApartmentDetailsDto(
        Guid Id,
        string Title,
        string Description,
        string Address,
        int DistrictId,
        string DistrictName,
        int MaxGuests,
        decimal PricePerNight,
        IReadOnlyList<string> PhotoUrls,
        IReadOnlyList<string> Amenities);
}
