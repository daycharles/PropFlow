# PropFlow

A modular property operations platform for work orders, bulk actions, resident communication, and operational history. It complements property-management systems through integration adapters.

## Where the project stands

Milestones 1 and 2 (foundation; tenant-aware Identity login, secure cookies and CSRF, organization membership and capability checks, PostgreSQL persistence/migrations, database readiness) are implemented. **Milestones 3, 4, 5, 6 and 7 are complete and on `main`** — every task issue `#6`–`#73` is closed. Milestone 6 was built in parallel with milestone 5, so milestone number is not milestone order; milestone 7 is the cross-cutting deployment and hardening track. [docs/milestones.md](docs/milestones.md) carries the per-milestone status and [docs/backlog.md](docs/backlog.md) the task-level detail; open threads are in [docs/followups.md](docs/followups.md).

Milestone 3 delivered the property hierarchy, expanded work/vendor/employee entities, a generalized timeline, work create/update, a filtered and paginated work list with client-facing concurrency tokens, bulk vendor assignment, saved views, demo seed data, and a running Next.js app in `apps/web` for login → work list → multi-select → assign vendor → timeline. Milestone 4 added the resident/occupancy domain with per-channel consent, mock SMS/email senders, the transactional outbox with idempotent at-most-once dispatch, the extended bulk work actions (schedule, status, priority, note, reopen), and resident messaging — templates rendered and queued per work item, surfaced back on the work timeline. Milestone 5 added the technician "on the way" workflow, the predefined automation-rules engine and its admin screen, visibility-scoped notes, inline communication status on the timeline, the field-role authorization scope model, and a technician mobile view. Milestone 6 added the `Asset` domain and the optional work↔asset link, the asset detail / maintenance-history page, configurable repeat-repair detection with its warning surface, the attention-queue backend and the "Needs your attention" screen, a `pg_trgm` fuzzy global search with a keyboard-first palette, the integration adapter abstraction and the Integration Health screen, and capability-driven navigation curation. Milestone 7 added tenant-scoped attachment/photo storage with field-role scope checks and retention enforcement, multi-instance readiness (shared encrypted Data Protection keys, the forwarded-headers trust boundary, configuration gates and PostgreSQL TLS), the observability pass with end-to-end request trace IDs and `/health/metrics`, the UTC-instant-plus-IANA scheduling model rendered as local times in web, and real-provider consent, signed provider callbacks and retry/retention controls. There are no live resident messages or production PMS integrations, and integration sync tracks external records without reconciling them into the domain tables.

**Deployment posture: source build only.** There is no container image and no hosted environment; the release artifact is a git tag. [docs/RELEASE-TESTING.md](docs/RELEASE-TESTING.md) is the macOS/Linux bash walkthrough for standing the stack up from a tag and exercising it, and [CHANGELOG.md](CHANGELOG.md) records what each release contains.

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
