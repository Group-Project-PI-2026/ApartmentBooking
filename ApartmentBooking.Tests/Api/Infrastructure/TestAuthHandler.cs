using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApartmentBooking.Tests.Api.Infrastructure;

/// <summary>
/// Replaces Keycloak JWT validation in tests. The identity is taken from request headers,
/// so every test can act as any user/role without a running Keycloak.
/// No X-Test-UserId header = anonymous request (401 on protected endpoints).
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string UserIdHeader = "X-Test-UserId";
    public const string RolesHeader = "X-Test-Roles";
    public const string EmailHeader = "X-Test-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", userId.ToString()) };

        if (Request.Headers.TryGetValue(EmailHeader, out var email))
            claims.Add(new Claim("email", email.ToString()));

        var roles = Request.Headers[RolesHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var identity = new ClaimsIdentity(claims, Scheme, "sub", "role");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal sealed record TestUser(Guid Id, string Email, params string[] Roles)
{
    public static TestUser Tenant() => Create("tenant");
    public static TestUser Owner() => Create("owner");
    public static TestUser Manager() => Create("manager");
    public static TestUser Admin() => Create("admin");

    private static TestUser Create(string role)
    {
        var id = Guid.NewGuid();
        return new TestUser(id, $"{role}-{id:N}@myrent.test", role);
    }
}
