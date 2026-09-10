# Implementation milestones

This file defines the milestones. The epic and task-level breakdown for the
remaining work (milestones 3–7, including the cross-cutting hardening track),
lives in [backlog.md](backlog.md) and is the source for GitHub milestones and issues.

## 1 — Repository and foundation (implemented)

Initialize local Git, pin the SDK, create domain/application/API boundaries, add fail-closed tenant resolution and capability policy wiring, domain assignment/audit semantics, centralized HTTP errors, structured console logs, health endpoint, Compose database configuration, CI, and executable foundation checks. No login or persistent operations are represented as complete.

## 2 — Tenant-aware identity and persistence (implemented)

Add ASP.NET Identity, organization membership, capability mappings, cookie login/logout, CSRF protection, EF Core/PostgreSQL context, migrations, query/write isolation, composite tenant foreign keys, and database readiness. Add real PostgreSQL integration tests proving cross-tenant reads, writes, assignments, and spoofed tenant inputs are blocked.

Delivered: explicit administrative provisioning, revocable secure-cookie sessions, current-membership capability checks, PostgreSQL RLS with a restricted runtime role, centrally scoped EF reads/writes, real migrations, single-item assignment with atomic append-only history, OpenAPI, local setup instructions, dependency locks and database-backed CI. The Next.js UI, bulk assignment and full property/demo data were left to milestone 3, which has since delivered them.

## 3 — First usable vertical slice (implemented)

Create Next.js UI, property/space/vendor/work persistence, seed two organizations for isolation tests, and implement login → work list → multi-select → assign vendor → confirm → updated timeline. Provide search/filtering and a unified work detail workspace. Test rollback, unauthorized assignment, stale updates, and successful audit creation end to end.

Delivered: the Organization→Portfolio→Property→Building→Space hierarchy, expanded `Vendor`/`Employee`/`WorkItem` entities with migrations and forced RLS, the generalized `TimelineEntry`, work create/update behind `Work.Create`/`Work.Update`, a filtered/sorted/paginated work list with the `xmin` version on every row, single and bulk vendor assignment plus employee assignment, user-scoped saved views, a demo seed covering every status and priority, the Next.js work list/detail/saved-views UI, and the `web` and `e2e` CI jobs. All 27 M3 issues are closed (epic `#1`, tasks `#6`–`#31`) and the slice was promoted to `main` in `b31b864`. **PF-3.27** — refuse vendor/employee assignment on `Completed`/`Cancelled` work, plus `docs/demo-script.md` — also shipped against this milestone (`baf4498`, PR #98); it was written after the M3 issues were cut, so it is the one M3 task with no tracker issue. Still open against this milestone: the `TimelineEntry` generalization is only half applied (`From()` still writes the legacy `PreviousVendorId`/`VendorId` columns — `src/PropFlow.Domain/Timeline/TimelineEntry.cs:44`), Tailwind is a declared dependency (`apps/web/package.json:24`) imported by `apps/web/app/styles.css:1` but nothing processes it — there is no PostCSS config and no `@tailwindcss/postcss` — and the migration note below stands.

> **Migration note:** the `M3Operations` migration adds required `WorkItems` columns (`PropertyId`, `CreatorId`) and their foreign keys in one step with a `Guid.Empty` default, and backfills `Vendors.IsActive` to `false`. It applies cleanly only to a database with no pre-M3 `WorkItems`/`Vendors` rows. Before the first real deployment the M3 migration set must be squashed or rewritten to be safe against a populated milestone-2 database (nullable column → backfill → set NOT NULL → add FK).

## 4 — Polished pest-control workflow (in flight)

Bulk scheduling and assignment, resident templates, mock SMS/email, durable outbox, idempotent dispatch, success summary, saved views, and mobile interaction. Seed Tidewater Residential Management with 3 properties, approximately 10 buildings, 80 spaces, 70 residents, 6 vendors, 5 employees, 100 work items, and 60 assets. Include at least 18 pest-control requests.

Delivered (5 of 12 tasks closed): the resident/occupancy domain — `Resident` and `Occupancy` with contact fields, per-channel SMS/email consent and `AllowsContact`, `operations`-schema tables with forced RLS, and the read plus `People.Manage` write endpoints (PF-4.01 `#32`, PF-4.02 `#33`); the communication provider abstraction with recording mock SMS and email senders (PF-4.04 `#35`); the `communications`-schema transactional outbox with an idempotency key and an `xmin`-claimed at-most-once dispatch worker (PF-4.05 `#36`); and the extended bulk work actions — `bulk/status`, `bulk/priority`, `bulk/schedule`, `bulk/note`, `bulk/reopen`, each bounded to 100 items in one all-or-nothing transaction with a per-item version check (PF-4.07 `#38`, `20838a9`). `add tag` is carved out of PF-4.07 pending the tag-vocabulary decision.

Remaining: template wiring to resident data (PF-4.03 `#34`), communication records and delivery status as timeline entries (PF-4.06 `#37`, blocked on finishing the PF-3.10 timeline generalization), the web assign-and-notify flow (PF-4.08 `#39`), mobile-responsive work list and detail (PF-4.09 `#40`), the Tidewater seed (PF-4.10 `#41`), the bulk pest-control e2e spec (PF-4.11 `#42`), and the outbox-idempotency/template/consent test pass (PF-4.12 `#43`).

## 5 — Events and field workflows

Add technician On The Way workflow, predefined configurable automation rules, communication status in the timeline, notes with explicit visibility, and remaining authorized bulk actions.

**Not started.** All 11 tasks (`#44`–`#54`) are open, and no commit, file or type in the tree implements any of them — `Technician` and `Vendor` still map to no capabilities (`src/PropFlow.Application/Capabilities.cs:27`), which PF-5.01/PF-5.02 exist to change. Milestone 6 was started ahead of this one, so milestone order is not milestone sequence here: read each milestone's own status rather than inferring it from the number.

## 6 — Assets and attention (started ahead of milestone 5)

Asset maintenance history, configurable repeat-repair detection (initial example: 3 repairs in 120 days), actionable attention queue, fuzzy global search, integration adapters and integration health foundation. Keep unsupported reports or integrations out of navigation until usable.

Delivered (3 of 13 tasks closed): the `Asset` domain — type, manufacturer, model, serial, install date, warranty expiration, expected service life, condition, replacement cost estimate and notes, with its migration, forced RLS and read/write API behind `Assets.Manage`; photos are deferred to PF-7.01 (PF-6.01 `#55`). Fuzzy global search — `GET /api/search` behind `Work.Read`, `pg_trgm` substring plus `word_similarity` ranking over GIN trigram indexes, tenant-scoped across properties, buildings, spaces, residents, vendors, employees, categories, work orders and assets (PF-6.08 `#62`). And the integration adapter abstraction — `IIntegrationAdapter` with canonical Property/Space/Occupancy/WorkOrder/Asset records, a `MockIntegrationAdapter`, the `integrations` schema (`IntegrationConnection` + `ExternalRecordLink`) with forced RLS and external-id / source-system / last-sync / per-record sync-state tracking, and `/api/integrations` behind `Integrations.Manage` (PF-6.10 `#64`, `b9e7e67`).

**All three are API and domain only — there is no milestone 6 UI.** `apps/web` still has exactly three routes (work list, `work/[id]` detail, category settings): no asset detail page (PF-6.03), no global search UI (PF-6.09), no attention queue (PF-6.06, PF-6.07) and no integration health screen (PF-6.11). Integration sync also tracks external records without reconciling them into the domain tables — see [followups.md](followups.md).

## 7 — Deployment and platform hardening (cross-cutting)

Close the production gaps called out in [architecture.md](architecture.md). Pull tasks forward into earlier milestones when a feature forces the issue: attachment/photo storage with tenant-scoped access control and retention (PF-7.01); multi-instance readiness — shared encrypted Data Protection keys, trusted proxy configuration, managed secrets and PostgreSQL TLS (PF-7.02); an observability pass over structured logs, end-to-end request trace IDs and basic metrics/health dashboards (PF-7.03); a scheduling model storing UTC instants plus property IANA time zones and rendering local times in web (PF-7.04); and consent, provider callback and retry/retention controls for real communication providers (PF-7.05).

Delivered (1 of 6 tasks closed): the RLS/grants review checklist is enforced in the PR template for every new business table (PF-7.06 `#73`). The other five are open.

Each milestone must build and pass its relevant checks. Do not implement the full product in one pass.
