namespace ApartmentBooking.Tests.Integration;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set RUN_POSTGRES_INTEGRATION_TESTS=1 and start Docker to run PostgreSQL integration tests.";
        }
    }
}
