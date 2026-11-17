using System.Net;
using ApartmentBooking.Tests.Api.Infrastructure;
using ApartmentBookingApplication.Apartments;
using Microsoft.AspNetCore.Mvc;
using static ApartmentBooking.Tests.Api.Infrastructure.ApiHelpers;

namespace ApartmentBooking.Tests.Api;

/// <summary>Integration: public catalog / search endpoints (TC-SRCH-*).</summary>
[Collection(ApiCollection.Name)]
[Trait("Level", "Integration")]
public class ApartmentsApiTests(ApiFactory api)
{
    private readonly HttpClient _anonymous = api.ClientFor(null);

    [ApiFact]
    public async Task GetDistricts_ReturnsTenKyivDistricts_Anonymously()
    {
        var response = await _anonymous.GetAsync("/api/apartments/districts");

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var districts = await response.ReadAsync<List<DistrictDto>>();
        Assert.Equal(10, districts.Count);
        Assert.Equal(Enumerable.Range(1, 10), districts.Select(d => d.Id).Order());
        Assert.Contains(districts, d => d.Id == 6 && d.Name == "Печерський");
    }

    [ApiFact]
    public async Task Search_ByDistrictAndGuests_ReturnsOnlyMatchingActiveApartments()
    {
        var match = await api.SeedApartmentAsync(districtId: 3, maxGuests: 4);
        var tooSmall = await api.SeedApartmentAsync(districtId: 3, maxGuests: 1);
        var otherDistrict = await api.SeedApartmentAsync(districtId: 4, maxGuests: 4);
        var inactive = await api.SeedApartmentAsync(districtId: 3, maxGuests: 4, active: false);

        var result = await (await _anonymous.GetAsync("/api/apartments?districtId=3&guestsCount=3"))
            .ReadAsync<List<ApartmentCardDto>>();

        var ids = result.Select(a => a.Id).ToList();
        Assert.Contains(match.Id, ids);
        Assert.DoesNotContain(tooSmall.Id, ids);
        Assert.DoesNotContain(otherDistrict.Id, ids);
        Assert.DoesNotContain(inactive.Id, ids);
        Assert.All(result, a => Assert.Equal(3, a.DistrictId));
    }

    [ApiFact]
    public async Task Search_ResultsAreSortedByPriceAscending()
    {
        await api.SeedApartmentAsync(districtId: 2, pricePerNight: 3_000m);
        await api.SeedApartmentAsync(districtId: 2, pricePerNight: 900m);
        await api.SeedApartmentAsync(districtId: 2, pricePerNight: 1_800m);

        var result = await (await _anonymous.GetAsync("/api/apartments?districtId=2")).ReadAsync<List<ApartmentCardDto>>();

        var prices = result.Select(a => a.PricePerNight).ToList();
        Assert.Equal(prices.Order(), prices);
    }

    [ApiFact]
    public async Task Search_WithDates_CalculatesTotalPriceForAllNights()
    {
        var apartment = await api.SeedApartmentAsync(districtId: 1, pricePerNight: 1_250m);
        var checkIn = Today.AddDays(40);

        var result = await (await _anonymous.GetAsync(
                $"/api/apartments?districtId=1&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkIn.AddDays(3):yyyy-MM-dd}"))
            .ReadAsync<List<ApartmentCardDto>>();

        var card = Assert.Single(result, a => a.Id == apartment.Id);
        Assert.Equal(3_750m, card.TotalPrice);
    }

    [ApiFact]
    public async Task Search_WithoutDates_ReturnsNullTotalPrice()
    {
        var apartment = await api.SeedApartmentAsync(districtId: 8);

        var result = await (await _anonymous.GetAsync("/api/apartments?districtId=8")).ReadAsync<List<ApartmentCardDto>>();

        Assert.Null(Assert.Single(result, a => a.Id == apartment.Id).TotalPrice);
    }

    [ApiFact]
    public async Task Search_ExcludesBookedApartment_ButKeepsItForAdjacentDates()
    {
        var apartment = await api.SeedApartmentAsync(districtId: 9);
        var checkIn = Today.AddDays(50);
        await api.BookAsync(TestUser.Tenant(), apartment.Id, checkIn, checkIn.AddDays(3));

        async Task<bool> Visible(DateOnly from, DateOnly to) =>
            (await (await _anonymous.GetAsync($"/api/apartments?districtId=9&checkIn={from:yyyy-MM-dd}&checkOut={to:yyyy-MM-dd}"))
                .ReadAsync<List<ApartmentCardDto>>()).Any(a => a.Id == apartment.Id);

        Assert.False(await Visible(checkIn.AddDays(1), checkIn.AddDays(2)), "overlapping stay must hide the apartment");
        Assert.True(await Visible(checkIn.AddDays(3), checkIn.AddDays(5)), "check-in on the check-out day is allowed");
        Assert.True(await Visible(checkIn.AddDays(-2), checkIn), "check-out on the check-in day is allowed");
    }

    [ApiFact]
    public async Task Search_ExcludesApartmentWithActiveLock()
    {
        var apartment = await api.SeedApartmentAsync(districtId: 10);
        var checkIn = Today.AddDays(60);
        await (await api.ClientFor(TestUser.Tenant()).ReserveAsync(apartment.Id, checkIn, checkIn.AddDays(2)))
            .EnsureStatusAsync(HttpStatusCode.OK);

        var result = await (await _anonymous.GetAsync(
                $"/api/apartments?districtId=10&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkIn.AddDays(2):yyyy-MM-dd}"))
            .ReadAsync<List<ApartmentCardDto>>();

        Assert.DoesNotContain(result, a => a.Id == apartment.Id);
    }

    [ApiTheory]
    [InlineData("checkIn=2030-01-10", "Both checkIn and checkOut")]
    [InlineData("checkIn=2030-01-10&checkOut=2030-01-10", "checkOut must be after checkIn")]
    [InlineData("checkIn=2030-01-12&checkOut=2030-01-10", "checkOut must be after checkIn")]
    [InlineData("guestsCount=0", "guestsCount must be positive")]
    public async Task Search_WithInvalidParameters_Returns400ProblemDetails(string query, string expectedDetail)
    {
        var response = await _anonymous.GetAsync($"/api/apartments?{query}");

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Equal("Validation error", problem.Title);
        Assert.Contains(expectedDetail, problem.Detail);
    }

    [ApiFact]
    public async Task GetById_ReturnsDetails_AndHidesCheckInInstructions()
    {
        var apartment = await api.SeedApartmentAsync(amenities: ["Wi-Fi", "Пральна машина"]);

        var response = await _anonymous.GetAsync($"/api/apartments/{apartment.Id}");

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var details = await response.ReadAsync<ApartmentDetailsDto>();
        Assert.Equal(apartment.Title, details.Title);
        Assert.Equal("Печерський", details.DistrictName);
        Assert.Equal(["Wi-Fi", "Пральна машина"], details.Amenities);
        Assert.DoesNotContain("код 0000", body); // instructions are only for paid tenants
    }

    [ApiFact]
    public async Task GetById_ForInactiveOrUnknownApartment_Returns404()
    {
        var inactive = await api.SeedApartmentAsync(active: false);

        Assert.Equal(HttpStatusCode.NotFound, (await _anonymous.GetAsync($"/api/apartments/{inactive.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _anonymous.GetAsync($"/api/apartments/{Guid.NewGuid()}")).StatusCode);
    }

    [ApiFact]
    [Trait("Category", "KnownBug")]
    [Trait("Bug", "BUG-BE-003")]
    public async Task Search_WithCheckInInThePast_Returns400()
    {
        var past = Today.AddDays(-10);

        var response = await _anonymous.GetAsync($"/api/apartments?checkIn={past:yyyy-MM-dd}&checkOut={past.AddDays(2):yyyy-MM-dd}");

        // Expected: past dates are rejected like in the reserve endpoint. Actual: 200 with "available" apartments.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ApiTheoryAttribute : TheoryAttribute
{
    public ApiTheoryAttribute()
    {
        if (!TestDatabase.IsEnabled)
            Skip = "PostgreSQL is not configured for API tests.";
    }
}
