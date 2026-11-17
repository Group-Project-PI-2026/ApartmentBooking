using Npgsql;
using Testcontainers.PostgreSql;

namespace ApartmentBooking.Tests.Api.Infrastructure;

/// <summary>
/// Disposable PostgreSQL database for API integration and E2E tests.
/// Uses an existing server when TEST_POSTGRES_CONNECTION is set (a fresh database is created and dropped),
/// otherwise starts a throw-away Testcontainers PostgreSQL container (requires Docker).
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    public const string ServerVariable = "TEST_POSTGRES_CONNECTION";

    private readonly Func<ValueTask> _dispose;

    private TestDatabase(string connectionString, Func<ValueTask> dispose)
    {
        ConnectionString = connectionString;
        _dispose = dispose;
    }

    public string ConnectionString { get; }

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ServerVariable)) ||
        Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS") == "1";

    public static async Task<TestDatabase> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        if (!string.IsNullOrWhiteSpace(server))
            return await CreateOnExistingServerAsync(server);

        var container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("apartment_booking_api_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync();
        return new TestDatabase(container.GetConnectionString(), container.DisposeAsync);
    }

    private static async Task<TestDatabase> CreateOnExistingServerAsync(string server)
    {
        var databaseName = $"ab_tests_{Guid.NewGuid():N}"[..21];
        var admin = new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString;

        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = databaseName }.ConnectionString;

        return new TestDatabase(connectionString, async () =>
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        });
    }

    public ValueTask DisposeAsync() => _dispose();
}
