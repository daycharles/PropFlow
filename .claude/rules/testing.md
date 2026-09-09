---
paths:
  - "tests/**"
  - ".github/workflows/ci.yml"
---

# Test suites and what belongs where

Three suites with three different jobs. Putting a test in the wrong one is the common mistake.
Citations verified 2026-09-09; counts measured the same day — re-measure, do not quote.

## The suite map

| Project | Kind | Run with | What belongs in it |
|---|---|---|---|
| `tests/PropFlow.FoundationChecks` | `Exe`, not a test project | `dotnet run --project …` | A minimal boot smoke test. 12 checks today. **Do not grow it.** |
| `tests/PropFlow.UnitTests` | xUnit | `dotnet test …` | Pure domain and application logic, no I/O. 79 tests today. |
| `tests/PropFlow.IntegrationTests` | xUnit + Testcontainers | `dotnet test …`, Docker required | Database, RLS, and HTTP behavior against a real PostgreSQL and a real host. 54 tests today. |

`FoundationChecks` has `<OutputType>Exe</OutputType>` and references only
`PropFlow.Application` (`PropFlow.FoundationChecks.csproj`). It is run, not discovered — a new
assertion added there is not covered by `dotnet test` and does not belong there anyway.

## Unit tests

One `public sealed class <Subject>Tests` per file, named after what it tests
(`CapabilitiesTests.cs:7`, `TemplateRendererTests.cs:6`, and the nine siblings). No fixtures, no
container, no `TimeProvider.System` — inject a fake clock.

## Integration tests

- Every class carries `[Collection("PostgreSQL")]` (`AuthenticationTests.cs:9`,
  `AdministrationTests.cs:11`, …). The collection is defined once at
  `DatabaseFixture.cs:22-23`, so **one** PostgreSQL 17 container is shared by the whole suite.
  A class without the attribute runs outside the fixture and will not have a database.
- `DatabaseFixture` exposes **two** connection strings and the difference matters:
  - `AdminConnection` (`DatabaseFixture.cs:29`) — the container owner. It **bypasses nothing**
    but holds the privileges to seed and to assert. Use it to arrange state and to check the
    database directly.
  - `RuntimeConnection` (`DatabaseFixture.cs:30,38`) — the restricted `propflow_app` role, and
    what `ApplicationFactory` hands the API (`DatabaseFixture.cs:53,97`). This is the connection
    under test.
  `Scenario` (`DatabaseFixture.cs:64,94`) is the per-test world and exposes both:
  `AdminStore(org)` vs `Store(org)` (`:142-143`), `CommunicationsStore` likewise (`:145,147`).
- Method names are `Snake_case_sentences` describing the guarantee —
  `Rls_blocks_filter_bypass_raw_reads_and_bulk_writes`,
  `Pooled_connections_do_not_leak_previous_tenant` (`IsolationTests.cs:67,138`).

## Isolation claims need raw SQL

**An isolation test that only goes through EF proves nothing** — it may be re-testing the query
filter it is supposed to be independent of. `docs/architecture.md:39`: "Tests bypass EF query
filters and execute raw SQL/bulk operations to verify the database boundary still applies."
`IsolationTests.cs` is the model: raw reads, raw cross-tenant inserts, bulk writes, and a
missing-tenant-context case. A new RLS policy gets a test in that shape.

## What CI runs

`verify` (`.github/workflows/ci.yml:6-23`) runs restore `--locked-mode`, Release build,
foundation checks, unit tests, integration tests, and uploads the `.trx` files. `web` and `e2e`
are separate jobs — see `web.md`. Read the counts in the output, not just the exit code: a run
that discovers zero tests exits 0.
