using System.Text.Json;
using System.Text.Json.Serialization;
using ApartmentBooking.Tests.TestDoubles;
using ApartmentBookingApplication.Abstractions;
using ApartmentBookingDomain.Entities;
using ApartmentBookingInfrastructure.BackgroundJobs;
using ApartmentBookingInfrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApartmentBooking.Tests.Api.Infrastructure;

/// <summary>
/// Hosts the real ASP.NET Core API (controllers, middleware, EF Core, PostgreSQL) in memory.
/// Only the external systems are replaced: Keycloak (header-based test auth), Stripe and SMTP.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private TestDatabase? _database;

    internal FakeStripeGateway Stripe { get; } = new();
    internal RecordingEmailSender Email { get; } = new();

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsEnabled)
            return;

        _database = await TestDatabase.CreateAsync();
        _ = Server; // starts the host: migrations are applied on startup
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _database?.ConnectionString ?? "Host=disabled");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Database:SeedDemoData", "false");
        builder.UseSetting("BehindReverseProxy", "true");
        builder.UseSetting("Frontend:BaseUrl", "http://frontend.test");
        builder.UseSetting("Booking:DepositPercent", "30");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Stripe);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);

            var cleanup = services.Single(d => d.ImplementationType == typeof(ExpiredLocksCleanupService));
            services.Remove(cleanup);

            services.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                    o.DefaultScheme = TestAuthHandler.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });
    }

    internal HttpClient ClientFor(TestUser? user)
    {
        var client = CreateClient();
        if (user is null)
            return client;

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.Id.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, user.Email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', user.Roles));
        return client;
    }

    internal async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    internal Task<Apartment> SeedApartmentAsync(
        int districtId = 6,
        decimal pricePerNight = 1_000m,
        int maxGuests = 4,
        bool active = true,
        string? title = null,
        string[]? amenities = null)
    {
        return WithDbAsync(async db =>
        {
            var apartment = new Apartment(
                Guid.NewGuid(),
                districtId,
                title ?? $"Test apartment {Guid.NewGuid():N}"[..24],
                "Seeded by API tests",
                "вул. Тестова, 1",
                maxGuests,
                pricePerNight,
                "Ключі в боксі, код 0000",
                ["https://picsum.photos/seed/test/800/600"],
                amenities ?? ["Wi-Fi", "Кухня"]);
            if (!active)
                apartment.Deactivate();
            db.Apartments.Add(apartment);
            await db.SaveChangesAsync();
            return apartment;
        });
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(descriptor);
    }
}

internal static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "API (in-memory host + PostgreSQL)";
}

/// <summary>Runs only when a PostgreSQL server or Docker is available (see README).</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ApiFactAttribute : FactAttribute
{
    public ApiFactAttribute()
    {
        if (!TestDatabase.IsEnabled)
            Skip = $"Set {TestDatabase.ServerVariable} (existing PostgreSQL) or RUN_POSTGRES_INTEGRATION_TESTS=1 (Docker) to run API tests.";
    }
}
