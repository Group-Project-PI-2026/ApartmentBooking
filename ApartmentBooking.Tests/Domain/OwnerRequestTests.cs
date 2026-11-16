using ApartmentBookingDomain.Entities;
using ApartmentBookingDomain.Enums;
using ApartmentBookingDomain.Exceptions;

namespace ApartmentBooking.Tests.Domain;

public class OwnerRequestTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesNewRequestAndHistoryEntry()
    {
        var ownerId = Guid.NewGuid();

        var request = CreateRequest(ownerId);

        Assert.Equal(OwnerRequestStatus.New, request.Status);
        Assert.Equal(ownerId, request.OwnerId);
        var history = Assert.Single(request.History);
        Assert.Equal(OwnerRequestStatus.New, history.Status);
        Assert.Equal(ownerId, history.ChangedBy);
    }

    [Fact]
    public void Approve_CreatesApartmentForOwnerAndClosesRequest()
    {
        var ownerId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var request = CreateRequest(ownerId);

        var apartment = request.Approve(managerId, 1_500m, "Use the key box", "Approved");

        Assert.Equal(OwnerRequestStatus.Approved, request.Status);
        Assert.Equal(apartment.Id, request.ApartmentId);
        Assert.Equal(ownerId, apartment.OwnerId);
        Assert.Equal(1_500m, apartment.PricePerNight);
        Assert.Equal(2, request.History.Count);
    }

    [Fact]
    public void Reject_WithBlankReason_ThrowsDomainException()
    {
        var request = CreateRequest(Guid.NewGuid());

        Assert.Throws<DomainException>(() => request.Reject(Guid.NewGuid(), " "));
    }

    [Fact]
    public void RequestClarification_ChangesStatusAndAddsHistory()
    {
        var managerId = Guid.NewGuid();
        var request = CreateRequest(Guid.NewGuid());

        request.RequestClarification(managerId, "Please add a clearer address.");

        Assert.Equal(OwnerRequestStatus.NeedsClarification, request.Status);
        Assert.Equal(2, request.History.Count);
        Assert.Equal(managerId, request.History[^1].ChangedBy);
    }

    [Fact]
    public void TerminalRequest_CannotBeProcessedAgain()
    {
        var request = CreateRequest(Guid.NewGuid());
        request.Approve(Guid.NewGuid(), null, null, null);

        Assert.Throws<DomainException>(() => request.Reject(Guid.NewGuid(), "Duplicate decision"));
        Assert.Throws<DomainException>(() => request.Approve(Guid.NewGuid(), null, null, null));
    }

    [Fact]
    public void Constructor_WithZeroGuests_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => new OwnerRequest(
            Guid.NewGuid(),
            "Owner",
            "owner@example.test",
            "+380000000000",
            1,
            "Kyiv",
            "Apartment",
            "Description",
            0,
            1_200m,
            [],
            []));
    }

    private static OwnerRequest CreateRequest(Guid ownerId) =>
        new(
            ownerId,
            "Owner",
            "owner@example.test",
            "+380000000000",
            1,
            "Kyiv",
            "Apartment",
            "Description",
            3,
            1_200m,
            ["https://example.test/photo.jpg"],
            ["wifi"]);
}
