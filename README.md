# PropFlow

A property operations platform focused on work, bulk actions, resident communication, and operational history. Property-management systems remain systems of record behind integration adapters.

## Status

Milestone 1 foundation. This is a buildable repository, not yet a usable MVP. Login, persistent work orders, the web interface, EF migrations, and message delivery are planned in subsequent milestones. No external messages are sent.

## Local development

Prerequisites: .NET 10 SDK (10.0.3xx), Node.js for the future web application, and Docker Compose for PostgreSQL.

```powershell
dotnet build PropFlow.slnx
dotnet run --project tests/PropFlow.FoundationChecks
dotnet run --project src/PropFlow.Api --urls http://localhost:5080
```

`GET http://localhost:5080/health/live` returns process health. It does not claim database readiness. `GET /api/session` requires an authenticated session and returns 401 before login is implemented. No development bypass or client-supplied tenant header is accepted.

For the database, copy `.env.example` to `.env`, set a local password, then run `docker compose up -d database`. The API is not connected to the database yet. `.env` is ignored by Git.

## Repository

- `src/PropFlow.Api`: HTTP host, error handling, authentication/authorization boundary.
- `src/PropFlow.Domain`: tenant-scoped entities and domain events, no infrastructure dependencies.
- `src/PropFlow.Application`: tenant access and event dispatch contracts.
- `tests/PropFlow.FoundationChecks`: executable, dependency-free foundation checks.
- `apps/web`: reserved Next.js application boundary; no placeholder UI.
- `docs`: architecture, milestones, risks, and API contract.

See [architecture](docs/architecture.md), [milestones](docs/milestones.md), and [API contract](docs/api.md).

Design and implementation are independent. Competitor products are market references only; their source, UI, and proprietary workflows are not copied.
