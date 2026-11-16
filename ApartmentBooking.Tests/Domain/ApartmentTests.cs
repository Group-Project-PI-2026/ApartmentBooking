using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBooking.Tests.Domain;

public class ApartmentTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesActiveApartment()
    {
        var apartment = CreateApartment(pricePerNight: 1_200m);

        Assert.True(apartment.IsActive);
        Assert.Equal(1_200m, apartment.PricePerNight);
        Assert.Equal(3, apartment.MaxGuests);
    }

    [Fact]
    public void Constructor_WithBlankTitle_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            new Apartment(Guid.NewGuid(), 1, " ", "Description", "Address", 2, 1_000m));
    }

    [Fact]
    public void Constructor_WithZeroGuests_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            new Apartment(Guid.NewGuid(), 1, "Apartment", "Description", "Address", 0, 1_000m));
    }

    [Fact]
    [Trait("Requirement", "Positive nightly price")]
    public void Constructor_WithZeroNightlyPrice_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => CreateApartment(pricePerNight: 0m));
    }

    private static Apartment CreateApartment(decimal pricePerNight) =>
        new(
            Guid.NewGuid(),
            1,
            "Test apartment",
            "Description",
            "Kyiv",
            3,
            pricePerNight,
            "Door code is provided on arrival",
            ["https://example.test/photo.jpg"],
            ["wifi"]);
}
