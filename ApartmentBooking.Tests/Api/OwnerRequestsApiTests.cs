using System.Net;
using ApartmentBooking.Tests.Api.Infrastructure;
using ApartmentBookingApplication.Common;
using ApartmentBookingApplication.OwnerRequests;
using ApartmentBookingDomain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static ApartmentBooking.Tests.Api.Infrastructure.ApiHelpers;

namespace ApartmentBooking.Tests.Api;

/// <summary>Integration: owner cooperation requests and manager decisions (TC-OWN-001/004).</summary>
[Collection(ApiCollection.Name)]
[Trait("Level", "Integration")]
public class OwnerRequestsApiTests(ApiFactory api)
{
    private async Task<OwnerRequestDetailsDto> CreateAsync(TestUser owner, CreateOwnerRequestRequest? body = null) =>
        await (await (await api.ClientFor(owner).PostJsonAsync("/api/owner-requests", body ?? ValidOwnerRequest()))
            .EnsureStatusAsync(HttpStatusCode.Created)).ReadAsync<OwnerRequestDetailsDto>();

    [ApiFact]
    public async Task Create_ReturnsCreatedWithLocation_StatusNew_AndHistoryEntry()
    {
        var owner = TestUser.Owner();
        var body = ValidOwnerRequest();

        var response = await api.ClientFor(owner).PostJsonAsync("/api/owner-requests", body);

        await response.EnsureStatusAsync(HttpStatusCode.Created);
        var created = await response.ReadAsync<OwnerRequestDetailsDto>();
        Assert.Equal($"/api/owner-requests/my/{created.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal(OwnerRequestStatus.New, created.Status);
        Assert.Equal(owner.Id, created.OwnerId);
        Assert.Equal("Подільський", created.DistrictName);
        Assert.True(created.Number > 0);
        var history = Assert.Single(created.History);
        Assert.Equal(OwnerRequestStatus.New, history.Status);
    }

    [ApiFact]
    public async Task Create_AnySignedInUser_CanApply()
    {
        var tenantOnly = TestUser.Tenant();

        var response = await api.ClientFor(tenantOnly).PostJsonAsync("/api/owner-requests", ValidOwnerRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public static TheoryData<string, Action<CreateOwnerRequestRequest>> InvalidBodies => new()
    {
        { "ContactName", r => r.ContactName = "" },
        { "ContactEmail", r => r.ContactEmail = "not-an-email" },
        { "Address", r => r.Address = "" },
        { "Title", r => r.Title = "" },
        { "MaxGuests", r => r.MaxGuests = 0 },
        { "ExpectedPricePerNight", r => r.ExpectedPricePerNight = -1 },
    };

    [ApiTheory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_WithInvalidField_Returns400_AndStoresNothing(string field, Action<CreateOwnerRequestRequest> breakIt)
    {
        var owner = TestUser.Owner();
        var body = ValidOwnerRequest();
        breakIt(body);

        var response = await api.ClientFor(owner).PostJsonAsync("/api/owner-requests", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync());
        Assert.False(await api.WithDbAsync(db => db.OwnerRequests.AnyAsync(r => r.OwnerId == owner.Id)));
    }

    [ApiFact]
    public async Task Create_WithUnknownDistrict_Returns400()
    {
        var body = ValidOwnerRequest();
        body.DistrictId = 99;

        var response = await api.ClientFor(TestUser.Owner()).PostJsonAsync("/api/owner-requests", body);

        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
        Assert.Contains("District 99 does not exist", (await response.ReadAsync<ProblemDetails>()).Detail);
    }

    [ApiFact]
    public async Task GetMy_ReturnsOnlyOwnRequests_NewestFirst()
    {
        var owner = TestUser.Owner();
        var first = await CreateAsync(owner);
        var second = await CreateAsync(owner);
        await CreateAsync(TestUser.Owner());

        var my = await (await api.ClientFor(owner).GetAsync("/api/owner-requests/my")).ReadAsync<List<OwnerRequestListItemDto>>();

        Assert.Equal([second.Id, first.Id], my.Select(r => r.Id));
    }

    [ApiFact]
    public async Task ManagerList_SearchByNumberAndEmail_AndFilterByStatus_WithPaging()
    {
        var created = await CreateAsync(TestUser.Owner());
        var manager = api.ClientFor(TestUser.Manager());

        var byNumber = await (await manager.GetAsync($"/api/management/owner-requests?search={created.Number}"))
            .ReadAsync<PagedResult<OwnerRequestListItemDto>>();
        var byEmail = await (await manager.GetAsync($"/api/management/owner-requests?search={Uri.EscapeDataString(created.ContactEmail.ToUpperInvariant())}"))
            .ReadAsync<PagedResult<OwnerRequestListItemDto>>();
        var rejected = await (await manager.GetAsync("/api/management/owner-requests?status=Rejected&pageSize=500"))
            .ReadAsync<PagedResult<OwnerRequestListItemDto>>();
        var paged = await (await manager.GetAsync("/api/management/owner-requests?page=0&pageSize=1"))
            .ReadAsync<PagedResult<OwnerRequestListItemDto>>();

        Assert.Contains(byNumber.Items, r => r.Id == created.Id);
        Assert.Equal(created.Id, Assert.Single(byEmail.Items).Id);
        Assert.All(rejected.Items, r => Assert.Equal(OwnerRequestStatus.Rejected, r.Status));
        Assert.Equal(100, rejected.PageSize); // clamped
        Assert.Equal(1, paged.Page);           // page < 1 normalised
        Assert.Single(paged.Items);
    }

    [ApiFact]
    public async Task Approve_CreatesActiveApartmentWithManagerPrice_AndEmailsOwner()
    {
        var owner = TestUser.Owner();
        var created = await CreateAsync(owner);
        var manager = TestUser.Manager();

        var response = await api.ClientFor(manager).PostJsonAsync(
            $"/api/management/owner-requests/{created.Id}/approve",
            new ApproveOwnerRequestRequest { PricePerNight = 2_100m, CheckInInstructions = "Ключ у консьєржа", Comment = "Ласкаво просимо" });

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        var approved = await response.ReadAsync<OwnerRequestDetailsDto>();
        Assert.Equal(OwnerRequestStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApartmentId);
        Assert.Equal(manager.Id, approved.History.Last().ChangedBy);

        var apartment = await api.WithDbAsync(db => db.Apartments.SingleAsync(a => a.Id == approved.ApartmentId));
        Assert.True(apartment.IsActive);
        Assert.Equal(2_100m, apartment.PricePerNight);
        Assert.Equal(owner.Id, apartment.OwnerId);
        Assert.Contains(api.Email.Messages, m => m.To == created.ContactEmail && m.Subject.Contains("схвалено") && m.Body.Contains("Ласкаво просимо"));
    }

    [ApiFact]
    public async Task Clarify_ThenApprove_IsAllowed_AndHistoryKeepsMessages()
    {
        var created = await CreateAsync(TestUser.Owner());
        var manager = api.ClientFor(TestUser.Manager());

        await (await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/clarify",
            new OwnerRequestMessageRequest { Message = "Додайте фото ванної" })).EnsureStatusAsync(HttpStatusCode.OK);
        var approved = await (await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/approve", new { }))
            .ReadAsync<OwnerRequestDetailsDto>();

        Assert.Equal(
            [OwnerRequestStatus.New, OwnerRequestStatus.NeedsClarification, OwnerRequestStatus.Approved],
            approved.History.Select(h => h.Status));
        Assert.Equal("Додайте фото ванної", approved.History[1].Message);
        Assert.Contains(api.Email.Messages, m => m.To == created.ContactEmail && m.Body == "Додайте фото ванної");
    }

    [ApiFact]
    public async Task Reject_RequiresMessage_AndIsTerminal()
    {
        var created = await CreateAsync(TestUser.Owner());
        var manager = api.ClientFor(TestUser.Manager());

        var empty = await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/reject", new OwnerRequestMessageRequest { Message = "" });
        var reject = await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/reject", new OwnerRequestMessageRequest { Message = "Не той район" });
        var approveAfter = await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/approve", new { });
        var clarifyAfter = await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/clarify", new OwnerRequestMessageRequest { Message = "?" });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, approveAfter.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, clarifyAfter.StatusCode);
        Assert.Contains("already Rejected", (await approveAfter.ReadAsync<ProblemDetails>()).Detail);
        Assert.False(await api.WithDbAsync(db => db.OwnerRequests.AnyAsync(r => r.Id == created.Id && r.ApartmentId != null)));
    }

    [ApiFact]
    public async Task ApproveTwice_SecondCallFails_AndOnlyOneApartmentExists()
    {
        var created = await CreateAsync(TestUser.Owner());
        var manager = api.ClientFor(TestUser.Manager());

        await (await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/approve", new { })).EnsureStatusAsync(HttpStatusCode.OK);
        var second = await manager.PostJsonAsync($"/api/management/owner-requests/{created.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(1, await api.WithDbAsync(db => db.Apartments.CountAsync(a => a.Title == created.Title)));
    }

    [ApiFact]
    public async Task Decision_OnUnknownRequest_Returns404()
    {
        var response = await api.ClientFor(TestUser.Manager())
            .PostJsonAsync($"/api/management/owner-requests/{Guid.NewGuid()}/approve", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [ApiFact]
    [Trait("Category", "KnownBug")]
    [Trait("Bug", "BUG-BE-001")]
    public async Task Approve_WithoutPrice_ForRequestWithZeroExpectedPrice_IsRejected()
    {
        // The web form never sends expectedPricePerNight (see BUG-FE-002), so requests arrive with price 0.
        var created = await CreateAsync(TestUser.Owner(), ValidOwnerRequest(price: 0m));

        var response = await api.ClientFor(TestUser.Manager())
            .PostJsonAsync($"/api/management/owner-requests/{created.Id}/approve", new { });

        // Expected: 400 "price must be positive". Actual: 200 and an unbookable apartment priced 0 appears in the catalog.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
