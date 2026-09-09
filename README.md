# PropFlow

A modular property operations platform for work orders, bulk actions, resident communication, and operational history. It complements property-management systems through integration adapters.

## Current milestone

Milestone 2 implements tenant-aware Identity login, secure cookies and CSRF protection, organization membership and capability checks, PostgreSQL persistence/migrations, database readiness, and a minimal work/vendor-assignment API with an append-only timeline.

Milestone 3 adds the first usable vertical slice: the property hierarchy, expanded work/vendor/employee entities, a generalized timeline, work create/update, a filtered and paginated work list with client-facing concurrency tokens, bulk vendor assignment, saved views, demo seed data, and a running Next.js app in `apps/web` for login → work list → multi-select → assign vendor → timeline. Milestone 4 (communications and the transactional outbox) is in flight alongside it. There are no live resident messages or production PMS integrations.

## Get started

See [local development](docs/local-development.md) for first organization/admin setup, HTTPS, and runtime configuration. The API requires a migrated PostgreSQL database and a restricted database account. It will reject an owner/superuser connection.

```powershell
dotnet restore PropFlow.slnx --locked-mode
dotnet build PropFlow.slnx --configuration Release --no-restore
dotnet run --project tests/PropFlow.FoundationChecks --configuration Release --no-build
dotnet test tests/PropFlow.UnitTests --configuration Release --no-build
dotnet test tests/PropFlow.IntegrationTests --configuration Release --no-build
```

The web app has its own gate — in `apps/web`: `npm ci`, `npm run format` (`prettier --check`), `npm run lint`, `npm run build`.

Integration tests use disposable real PostgreSQL containers; Docker must be running. No existing database is changed. Dependencies are pinned and locked. GitHub Actions (`.github/workflows/ci.yml`) runs three jobs: `verify` (build, foundation checks, unit tests, integration tests), `web` (install, format check, lint, build), and `e2e` (Compose database, `migrate`/`configure-runtime`/`seed-demo`, API plus `next dev`, then Playwright).

## Repository

- `src/PropFlow.Api`: thin HTTP endpoints, session/CSRF policies, health and error handling.
- `src/PropFlow.Application`: capabilities, tenant context, work-use-case contracts and events.
- `src/PropFlow.Domain`: tenant entities, assignment semantics and immutable history.
- `src/PropFlow.Infrastructure`: Identity, EF Core, PostgreSQL RLS, migrations and persistence services.
- `tools/PropFlow.Admin`: explicit migration, runtime-role configuration and initial organization provisioning.
- `tests`: foundation boot checks, xUnit unit tests for domain/application logic, and PostgreSQL/API integration tests.
- `apps/web`: the Next.js 16 App Router client — login, work list with filters, sorting, saved views and bulk vendor assignment, work detail with timeline, and category settings. `next dev` proxies `/api/*` to the API; Playwright specs live in `apps/web/e2e`.
- `docs`: [architecture](docs/architecture.md), [milestones](docs/milestones.md), [backlog](docs/backlog.md), [API](docs/api.md), [security audit](docs/audit-security.md), [communications audit](docs/audit-communications.md), [follow-ups tracker](docs/followups.md), and original handoff.

Design and implementation are independent. Competitor products are market references only; their source, UI and proprietary workflows are not copied.
