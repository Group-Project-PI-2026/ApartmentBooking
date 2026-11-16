using System.Security.Claims;
using System.Text.Json;
using ApartmentBookingApplication.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ApartmentBooking.Auth
{
    public static class Policies
    {
        public const string Tenant = nameof(Tenant);
        public const string Owner = nameof(Owner);
        public const string Manager = nameof(Manager);
        public const string Admin = nameof(Admin);
    }

    public static class KeycloakAuthExtensions
    {
        public const string RoleClaimType = "role";

        public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection("Authentication");
            var audience = section["Audience"];

            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // e.g. http://keycloak:8080/realms/apartment-booking — signing keys are discovered via OIDC metadata.
                    options.Authority = section["Authority"];
                    options.RequireHttpsMetadata = section.GetValue("RequireHttpsMetadata", true);
                    options.MapInboundClaims = false; // keep original claim names: sub, email, realm_access ...

                    options.TokenValidationParameters.ValidateAudience = !string.IsNullOrEmpty(audience);
                    options.TokenValidationParameters.ValidAudience = audience;
                    options.TokenValidationParameters.NameClaimType = "preferred_username";
                    options.TokenValidationParameters.RoleClaimType = RoleClaimType;
                });

            services.AddTransient<IClaimsTransformation>(_ => new KeycloakRolesClaimsTransformation(section["ClientId"]));

            services.AddAuthorizationBuilder()
                // Admin can do everything a manager can.
                .AddPolicy(Policies.Tenant, p => p.RequireRole(Roles.Tenant, Roles.Admin))
                .AddPolicy(Policies.Owner, p => p.RequireRole(Roles.Owner, Roles.Admin))
                .AddPolicy(Policies.Manager, p => p.RequireRole(Roles.Manager, Roles.Admin))
                .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));

            return services;
        }
    }

    /// <summary>
    /// Keycloak puts roles into nested JSON claims (<c>realm_access.roles</c> and <c>resource_access.{client}.roles</c>).
    /// This flattens them into simple "role" claims so [Authorize(Roles/Policy)] works.
    /// </summary>
    public class KeycloakRolesClaimsTransformation : IClaimsTransformation
    {
        private readonly string? _clientId;

        public KeycloakRolesClaimsTransformation(string? clientId)
        {
            _clientId = clientId;
        }

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity ||
                identity.HasClaim(c => c.Type == KeycloakAuthExtensions.RoleClaimType))
            {
                return Task.FromResult(principal);
            }

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var realmAccess = identity.FindFirst("realm_access")?.Value;
            if (realmAccess is not null)
                AddRoles(realmAccess, roles);

            var resourceAccess = identity.FindFirst("resource_access")?.Value;
            if (resourceAccess is not null && !string.IsNullOrEmpty(_clientId))
            {
                using var doc = JsonDocument.Parse(resourceAccess);
                if (doc.RootElement.TryGetProperty(_clientId, out var client))
                    AddRoles(client.GetRawText(), roles);
            }

            foreach (var role in roles)
                identity.AddClaim(new Claim(KeycloakAuthExtensions.RoleClaimType, role));

            return Task.FromResult(principal);
        }

        private static void AddRoles(string json, HashSet<string> roles)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("roles", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in array.EnumerateArray())
                {
                    var value = role.GetString();
                    if (!string.IsNullOrEmpty(value))
                        roles.Add(value);
                }
            }
        }
    }
}
