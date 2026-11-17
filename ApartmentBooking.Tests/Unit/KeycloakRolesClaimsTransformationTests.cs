using System.Security.Claims;
using ApartmentBooking.Auth;

namespace ApartmentBooking.Tests.Unit;

[Trait("Level", "Unit")]
public class KeycloakRolesClaimsTransformationTests
{
    private const string ClientId = "apartment-booking-spa";
    private readonly KeycloakRolesClaimsTransformation _sut = new(ClientId);

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Bearer", "preferred_username", KeycloakAuthExtensions.RoleClaimType));

    private static string[] Roles(ClaimsPrincipal p) =>
        p.FindAll(KeycloakAuthExtensions.RoleClaimType).Select(c => c.Value).Order().ToArray();

    [Fact]
    public async Task RealmRoles_AreFlattenedIntoRoleClaims()
    {
        var principal = Authenticated(new Claim("realm_access", """{"roles":["tenant","offline_access"]}"""));

        var result = await _sut.TransformAsync(principal);

        Assert.Equal(["offline_access", "tenant"], Roles(result));
        Assert.True(result.IsInRole("tenant"));
    }

    [Fact]
    public async Task ClientRoles_OfConfiguredClient_AreAdded_OtherClientsIgnored()
    {
        var principal = Authenticated(
            new Claim("realm_access", """{"roles":["tenant"]}"""),
            new Claim("resource_access", """{"apartment-booking-spa":{"roles":["manager"]},"other-app":{"roles":["admin"]}}"""));

        var result = await _sut.TransformAsync(principal);

        Assert.Equal(["manager", "tenant"], Roles(result));
        Assert.False(result.IsInRole("admin"));
    }

    [Fact]
    public async Task DuplicateRoles_AreAddedOnce_CaseInsensitive()
    {
        var principal = Authenticated(
            new Claim("realm_access", """{"roles":["Owner","owner"]}"""),
            new Claim("resource_access", """{"apartment-booking-spa":{"roles":["OWNER"]}}"""));

        var result = await _sut.TransformAsync(principal);

        Assert.Single(Roles(result));
    }

    [Fact]
    public async Task PrincipalWithRoleClaims_IsNotTransformedTwice()
    {
        var principal = Authenticated(
            new Claim(KeycloakAuthExtensions.RoleClaimType, "tenant"),
            new Claim("realm_access", """{"roles":["admin"]}"""));

        var result = await _sut.TransformAsync(principal);

        Assert.Equal(["tenant"], Roles(result));
    }

    [Fact]
    public async Task AnonymousPrincipal_IsReturnedUnchanged()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("realm_access", """{"roles":["admin"]}""") }));

        var result = await _sut.TransformAsync(anonymous);

        Assert.Empty(Roles(result));
    }

    [Fact]
    public async Task MissingRolesArray_ProducesNoRoles()
    {
        var principal = Authenticated(new Claim("realm_access", """{"something":"else"}"""));

        var result = await _sut.TransformAsync(principal);

        Assert.Empty(Roles(result));
    }
}
