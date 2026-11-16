using ApartmentBookingInfrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ApartmentBooking.Tests.Integration;

internal sealed class PostgresTestEnvironment : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    private PostgresTestEnvironment(PostgreSqlContainer container)
    {
        _container = container;
    }

    public static async Task<PostgresTestEnvironment> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("apartment_booking_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await container.StartAsync();
        var environment = new PostgresTestEnvironment(container);

        await using var db = environment.CreateDbContext();
        await db.Database.MigrateAsync();

        return environment;
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
