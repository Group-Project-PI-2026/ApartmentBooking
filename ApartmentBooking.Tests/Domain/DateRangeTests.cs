using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBooking.Tests.Domain;

public class DateRangeTests
{
    [Fact]
    public void Constructor_WhenDatesAreEqual_ThrowsDomainException()
    {
        var day = new DateOnly(2027, 3, 10);

        var exception = Assert.Throws<DomainException>(() => new DateRange(day, day));

        Assert.Contains("after check-in", exception.Message);
    }

    [Fact]
    public void Constructor_WhenCheckoutIsBeforeCheckin_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            new DateRange(new DateOnly(2027, 3, 11), new DateOnly(2027, 3, 10)));
    }

    [Fact]
    public void CreateUpcoming_WhenCheckinIsInPast_ThrowsDomainException()
    {
        var today = new DateOnly(2027, 3, 10);

        Assert.Throws<DomainException>(() =>
            DateRange.CreateUpcoming(today.AddDays(-1), today.AddDays(2), today));
    }

    [Fact]
    public void Overlaps_WhenRangesAreAdjacent_ReturnsFalse()
    {
        var first = new DateRange(new DateOnly(2027, 3, 10), new DateOnly(2027, 3, 12));
        var second = new DateRange(new DateOnly(2027, 3, 12), new DateOnly(2027, 3, 14));

        Assert.False(first.Overlaps(second));
        Assert.False(second.Overlaps(first));
    }

    [Fact]
    public void Overlaps_WhenRangesShareANight_ReturnsTrue()
    {
        var first = new DateRange(new DateOnly(2027, 3, 10), new DateOnly(2027, 3, 13));
        var second = new DateRange(new DateOnly(2027, 3, 12), new DateOnly(2027, 3, 14));

        Assert.True(first.Overlaps(second));
        Assert.True(second.Overlaps(first));
    }
}
