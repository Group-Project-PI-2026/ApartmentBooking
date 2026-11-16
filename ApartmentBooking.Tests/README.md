# Backend automated tests

The project contains fast domain tests and PostgreSQL integration tests for the critical booking flows.

Run the default suite:

```powershell
dotnet test ApartmentBooking.slnx --configuration Release
```

The default suite skips PostgreSQL concurrency tests because they create disposable Docker containers. To run them, start Docker Desktop and set the opt-in variable:

```powershell
$env:RUN_POSTGRES_INTEGRATION_TESTS = "1"
dotnet test ApartmentBooking.Tests\ApartmentBooking.Tests.csproj --configuration Release
```

Do not replace PostgreSQL with EF InMemory for these tests. The production implementation depends on transactions, `SELECT ... FOR UPDATE`, `ExecuteDeleteAsync`, `xmin`, and database uniqueness constraints.
