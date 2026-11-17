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

## API integration and E2E tests (QA: Zinchenko)

| Folder | Level | What it covers |
| --- | --- | --- |
| `Unit/` | Unit | Keycloak role mapping, Correlation ID middleware, exception -> ProblemDetails mapping, request DTO validation, webhook event filtering, extra domain rules |
| `Api/` | Integration | Real ASP.NET Core host (`WebApplicationFactory`) + real PostgreSQL: search, auth/RBAC/IDOR, reservation lock, cancellation, Stripe webhook, owner requests, health/observability |
| `E2E/` | End-to-end | Whole user journeys over HTTP: search -> reserve -> pay deposit -> pay balance -> cancel; owner apply -> manager clarify/approve -> tenant books |

Only external systems are replaced: Keycloak (header-based `TestAuthHandler`), Stripe (`FakeStripeGateway`) and SMTP (`RecordingEmailSender`).

PostgreSQL for API/E2E tests:

```powershell
# Option A: Docker (Testcontainers starts postgres:17-alpine)
$env:RUN_POSTGRES_INTEGRATION_TESTS = "1"

# Option B: an existing server (a temporary database is created and dropped)
$env:TEST_POSTGRES_CONNECTION = "Host=localhost;Port=5433;Username=postgres;Password=postgres"
```

Tests that reproduce open defects are tagged `Category=KnownBug` and a `Bug` id (see the QA report). They fail until the bug is fixed.

```powershell
dotnet test ApartmentBooking.slnx --filter "Category!=KnownBug"   # green regression suite
dotnet test ApartmentBooking.slnx --filter "Category=KnownBug"    # open defects (expected to fail)
dotnet test ApartmentBooking.slnx --filter "Level=E2E"            # only end-to-end journeys
```
