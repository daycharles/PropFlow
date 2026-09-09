# Implementation milestones

This file defines the milestones. The epic and task-level breakdown for the
remaining work (milestones 3 onward), including a cross-cutting hardening track,
lives in [backlog.md](backlog.md) and is the source for GitHub milestones and issues.

## 1 — Repository and foundation (implemented)

Initialize local Git, pin the SDK, create domain/application/API boundaries, add fail-closed tenant resolution and capability policy wiring, domain assignment/audit semantics, centralized HTTP errors, structured console logs, health endpoint, Compose database configuration, CI, and executable foundation checks. No login or persistent operations are represented as complete.

## 2 — Tenant-aware identity and persistence (implemented)

Add ASP.NET Identity, organization membership, capability mappings, cookie login/logout, CSRF protection, EF Core/PostgreSQL context, migrations, query/write isolation, composite tenant foreign keys, and database readiness. Add real PostgreSQL integration tests proving cross-tenant reads, writes, assignments, and spoofed tenant inputs are blocked.

Delivered: explicit administrative provisioning, revocable secure-cookie sessions, current-membership capability checks, PostgreSQL RLS with a restricted runtime role, centrally scoped EF reads/writes, real migrations, single-item assignment with atomic append-only history, OpenAPI, local setup instructions, dependency locks and database-backed CI. The Next.js UI, bulk assignment and full property/demo data are still milestone 3.

## 3 — First usable vertical slice

Create Next.js UI, property/space/vendor/work persistence, seed two organizations for isolation tests, and implement login → work list → multi-select → assign vendor → confirm → updated timeline. Provide search/filtering and a unified work detail workspace. Test rollback, unauthorized assignment, stale updates, and successful audit creation end to end.

## 4 — Polished pest-control workflow

Bulk scheduling and assignment, resident templates, mock SMS/email, durable outbox, idempotent dispatch, success summary, saved views, and mobile interaction. Seed Tidewater Residential Management with 3 properties, approximately 10 buildings, 80 spaces, 70 residents, 6 vendors, 5 employees, 100 work items, and 60 assets. Include at least 18 pest-control requests.

## 5 — Events and field workflows

Add technician On The Way workflow, predefined configurable automation rules, communication status in the timeline, notes with explicit visibility, and remaining authorized bulk actions.

## 6 — Assets and attention

Asset maintenance history, configurable repeat-repair detection (initial example: 3 repairs in 120 days), actionable attention queue, fuzzy global search, integration adapters and integration health foundation. Keep unsupported reports or integrations out of navigation until usable.

Each milestone must build and pass its relevant checks. Do not implement the full product in one pass.
