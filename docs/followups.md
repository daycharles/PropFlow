# PropFlow follow-ups (parking lot)

Single tracking list of everything the team needs to come back to. Maintained as work lands —
close an item here when the change that resolves it merges. The authorities remain
[`docs/audit-security.md`](audit-security.md), [`docs/audit-communications.md`](audit-communications.md),
[`docs/backlog.md`](backlog.md), and [`docs/milestones.md`](milestones.md); this file only
aggregates their open threads plus the tooling/process gaps.

Owner `daycdev` = the M3 vertical-slice work. "unassigned" = no owner yet.

---

## M4 — what's left

M3 is complete (all `#6`–`#31` closed, promoted to `main`); PF-3.27 shipped after the issues
were cut and so has none. **PF-4.01–4.07 are done** (PF-4.03 + PF-4.06 landed in PR #108).

`develop` and `main` are level, nothing is flag-gated and nothing is held back on a feature branch,
so `main` is the whole truth. Re-check with
`git rev-list --left-right --count origin/main...origin/develop` rather than trusting this line.

What remains in M4:

| Item | Source | Owner | Notes |
| --- | --- | --- | --- |
| PF-4.06 (atomicity half) — enqueue the outbox row in the same transaction as the triggering Operations change | audit-communications C11 (accepted) | unassigned | `POST /api/work/{id}/message` enqueues atomically *within that call*, but there is still no path that enqueues from a **work status/schedule change**. That (and idempotent real providers) is where C11 bites — needs the M5 event wiring (PF-5.03) |
| PF-4.08 (#39) — web "assign & notify" flow (vendor + schedule window + resident message + confirm + summary) | backlog.md M4 | unassigned | Buildable: PF-4.07 ✓, PF-4.03 ✓, M3 web bulk-vendor flow on `main`. Substantial front-end feature |
| PF-4.09 (#40) — mobile-responsive Work list and detail, large touch targets | backlog.md M4 | unassigned | Buildable now — a responsiveness pass over the M3 `apps/web` work list and `work/[id]` detail |
| PF-4.10 (#41) — full Tidewater demo seed (~3 properties, ~80 spaces, ~70 residents, 6 vendors, ~100 work items, ~60 assets, ≥18 pest-control) | backlog.md M4 | unassigned | All the domain exists (residents/occupancy/assets/hierarchy). Standalone; unblocks PF-4.11. `infra` / the `PropFlow.Admin` seed |
| PF-4.11 (#42) — e2e: bulk pest-control assignment + notify demo workflow in CI | backlog.md M4 | unassigned | Needs PF-4.08 + PF-4.10 |
| PF-4.12 (#43) — tests: outbox idempotency, template rendering, consent respected | backlog.md M4 | mday440 | Outbox idempotency + rendering + consent are covered (`CommunicationsTests`, `ResidentMessageTests`, `TemplateRendererTests`). Left: an explicit "no duplicate send on retry after a transient failure" scenario end-to-end |

---

## Needs a decision

| Item | Source | Owner | Notes |
| --- | --- | --- | --- |
| `identity` schema has no RLS — `Memberships`, `Organizations` (runtime role holds `SELECT ON ALL TABLES IN SCHEMA identity`) | audit-security M-1 (open) | daycdev (design call) | Not exploitable today (no listing endpoint, `MembershipAccess` methods are scoped). Must be decided before any member-list or invitation endpoint. Recommendation: forced RLS + `organization_id` policy on `Memberships`/`Organizations`; `AspNetUsers` stays global |

See also the product decisions below, which gate feature work.

---

## Deferred (deployment / hardening)

Recorded and accepted; revisit during the M7 hardening track or when a feature forces the issue.

| Item | Source | Owner | Notes |
| --- | --- | --- | --- |
| Tenant GUC is session-scoped, not transaction-scoped (`set_config(..., false)`) | audit-security L-1 (accepted) | unassigned | Sound today (interceptor re-sets per checkout, Npgsql resets pooled connections). Fragile to a future Npgsql multiplexing / reset-disabled change; fix is `SET LOCAL` per transaction or `RESET` on close |
| Runtime role holds unused `DELETE` on `WorkItems` / `OutboxMessages` | audit-security L-2 (accepted) | unassigned | No endpoint deletes either; RLS still confines any delete to the tenant. Drop from the grants when confirming least-privilege, re-add per feature |
| Web CSP + HTTPS redirection at the edge | audit-security L-4 (API side done) | daycdev (web) / deployment | API now sends HSTS (non-Dev), `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options: DENY`, `Cross-Origin-Resource-Policy`, and `Content-Security-Policy: default-src 'none'`. Still open: `apps/web` CSP and http→https redirect (left to the ingress/proxy) |
| Login rate-limiter collapses to one global bucket when `RemoteIpAddress` is null; `/api/auth/csrf` is unrated | audit-security L-5 (accepted) | unassigned | Per-account lockout (5/15min) partially compensates. Fix needs care around the existing rate-limit test + trusted-proxy config |
| Capped-exponential backoff + transient/permanent error classification for real providers | audit-communications C3 (deferred) → PF-7.05 | unassigned | Current retry is fixed 2-min spacing, cap 8 (~14 min outage tolerance); config-bound |
| Message consent + provider callback + retry/retention controls for real communication providers | backlog.md PF-7.05; audit-security L-3 residue | unassigned | Context-aware template encoding + a recipient/egress policy for the real provider adapter |
| Templates do not flag an unterminated `{{` (`"arriving at {{time"` renders literally) | audit-communications C12 (accepted) | unassigned | Low impact for authored templates; revisit if templates become user-generated at scale |
| `M3Operations` migration is not safe against a populated milestone-2 database | milestones.md M3 migration note; audit-communications C-audit M6 | daycdev | Adds NOT NULL `PropertyId`/`CreatorId` + FK in one step with a `Guid.Empty` default and backfills `Vendors.IsActive = false`. Applies cleanly only to a DB with no pre-M3 `WorkItems`/`Vendors` rows. Squash/rewrite (nullable → backfill → NOT NULL → FK) before first real deployment |
| Response DTOs for the non-work read endpoints | backlog.md PF-3.15 | unassigned | Work responses carry an explicit `version`; the list and now the timeline (`TimelineItem`) return flat DTOs. `/api/residents`, `/api/assets`, `/api/saved-views` and `/api/communication/templates` still return domain entities straight out |
| Global search (`EfGlobalSearch`) issues 9 sequential round-trips (one per entity type) and ranks in memory | PF-6.08; `src/PropFlow.Infrastructure/Persistence/EfGlobalSearch.cs` | unassigned | Fine at seed-data scale. Revisit with a single `UNION ALL` query (or a materialized search view) if p95 latency or DB load warrants. Also: `limit` is applied per type then again globally, so a type can crowd out others up to `limit`; a per-type floor may be wanted once the PF-6.09 UI groups results |
| Integration adapter — external records are tracked but not reconciled | PF-6.10; `src/PropFlow.Infrastructure/Integrations/EfIntegrationOperations.cs` | unassigned | A sync upserts one `ExternalRecordLink` per external record (id + content hash + sync state) but does not create/update the corresponding `Property` / `Space` / `Occupancy` / `WorkItem` / `Asset`. That mapping (and its conflict handling — the "unresolved conflicts" count PF-6.11 wants) is the next task. `SyncReport.failed` and per-record `Failed` state exist for it but stay 0 until then |
| Integration sync is manual-trigger only, and stale external records are never retired | PF-6.10 | unassigned | `POST /api/integrations/{id}/sync` is the only way to run a pull — no scheduler / background worker. A link whose external record disappears from a later snapshot keeps its last state forever (no "deleted upstream" marker) |
| Integration connections store no credentials | PF-6.10; `IntegrationConnection` | unassigned | The mock adapter needs none. A real adapter's secrets must come from the secret store keyed by connection id (ties into PF-7.02 managed secrets), not from a column |
| Full offset paging for the vendor/employee/property lookup lists | audit-2026-09-10 A-6 (narrowed) | unassigned | `/api/integrations/{id}/records` now pages properly; `/api/vendors`, `/api/employees`, `/api/properties` gained a `?q=` filter but keep the flat-array shape (the web client depends on it) capped at 500. Real `page`/`pageSize` for those three needs a coordinated `apps/web` change — do it when they get dedicated screens. The `?q=` value also has no length bound (audit B-8) |
| Work location refs are not hierarchy-checked | audit-2026-09-10 B-6; `EfWorkOperations.EnsureChildReferencesAsync` / `WorkItem.SetLocation` | unassigned | Create/update now verify `BuildingId`/`SpaceId`/`ResidentId`/`CategoryId` *exist* in the tenant but not that the space/building actually belongs to the named property. Add a `SpaceId → PropertyId` (and building) consistency check |
| Bulk `WorkNote` visibility is a marker, not enforced | audit-2026-09-10 B-7; PF-5.06 | unassigned | `POST /api/work/bulk/note` stores `internal`/`resident` in the timeline entry's `Changes`, but nothing filters resident-visible vs internal on reads yet (PF-5.06). Note text also has no control-char guard (renders in the React timeline, which escapes) |
| `WorkReopened.EventId` is dead, and `Reopen` doesn't set `CreatedAt` | audit-2026-09-10 B-9 | unassigned | The timeline entry gets its own id via `Event()`, so `WorkReopened.EventId` is unused. Reopening a never-published `Draft → Cancelled` item leaves `CreatedAt` at `default` — folds into the existing "`CreatedAt` only set in `Publish`" debt below |

---

## Tech debt (M3 slice — daycdev)

Flagged in the 2026-09-09 code-quality audit, left for the M3 owner.

| Item | Source | Owner | Notes |
| --- | --- | --- | --- |
| Tailwind is a dependency but nothing uses it | code audit 2026-09-09; `apps/web/package.json:24`, `apps/web/app/styles.css:1` | daycdev | PF-3.01 called for Tailwind. `styles.css:1` has `@import "tailwindcss"` but there is no PostCSS config and every rule below it is hand-written CSS. Either wire the build up or drop the dependency and the import |
| `CreatedAt` is only set in `Publish` | code audit 2026-09-09; `src/PropFlow.Domain/Work/WorkItem.cs` | daycdev | A `Draft` row carries `default(DateTimeOffset)` for `CreatedAt`. (The terminal-state half of this row is **closed** — PF-3.27 guarded the assignment overloads, audit A-1 guarded `Edit`/`Schedule`, PF-4.07 guarded `SetPriority` and added the sanctioned `Reopen`.) |
| M3 domain types have two public constructors, no private-EF-ctor + factory | code audit 2026-09-09 | daycdev | `WorkItem`, `WorkCategory`, hierarchy types — the C5 fix pattern (private ctor + static `Create`) was not applied |
| `Vendors.IsActive` backfilled `false`, `Employees.IsActive` added with no default | code audit 2026-09-09; `M3Operations.cs` | daycdev | Domain default is `true`; pre-M3 vendors silently deactivated; inconsistent between the two tables |
| `OperationsStore` + `CommunicationsStore` duplicate the tenant-convention loop + `GuardWrites` verbatim | code audit 2026-09-09 | daycdev | Extract the shared base |
| No formatting analyzer / `dotnet format` in CI; M3 slice packs whole methods per line | code audit 2026-09-09 | daycdev | Inconsistent with the rest of the codebase |
| `WorkItem(orgId, id, title)` sets `CreatorId = id` (the work's own id) | audit-security (low); `Domain/Work/WorkItem.cs` | daycdev | Fails closed (`CanDelete`) but wrong |
| Slug login integration tests are happy-path only | code audit 2026-09-09 | daycdev | No wrong-slug / case-insensitivity coverage despite #79 being about slug test gaps |

---

## Tooling / process

| Item | Source | Owner | Unblocks when |
| --- | --- | --- | --- |
| `main` branch protection not configured | Slack #agent-updates 2026-09-09; CLAUDE.md ("Protected: `verify` must be green") | unassigned | `verify` is not a required check, so direct pushes to `main` bypass CI (broke `main` once, commit `8c88b20`, recovered by PR #79). The keyring `gh` token has `repo` but not Administration, so an agent still can't set it — a human does it in repo Settings → Branches |
| `gh` (2.63.2) and Node (`~/.local/node`, v22.14) on the macOS box are hand-installed, not on `PATH` by default | 2026-09-09 / 2026-09-10 agent sessions | unassigned | Neither is from a package manager or in the documented toolchain. `gh` auth: keyring `mday440` token (`project`/`workflow`/`repo`) — do not set `GH_TOKEN`. Pin the tools or fold into `docs/local-development.md` |
| `daycdev` pushes M3 directly to `main` rather than via PR into `develop` | CLAUDE.md branching rules; git log (`8c88b20` direct) | daycdev | Process drift; ties into branch protection above |

---

## Open product questions

From `docs/backlog.md` "Open questions / decisions needed" — each gates the referenced task.

| Question | Gates | Owner |
| --- | --- | --- |
| SLA model — per-priority duration, per-category policy, or per-organization configurable? | PF-6.06 attention queue | unassigned |
| Unit-turn workflow — a work type, a distinct entity with checklist steps, or out of scope for now? | PF-6.06 "unit turn at risk" | unassigned |
| Saved-view sharing scope — user-private only, or shareable / organization-default? | PF-3.17 | unassigned |
| Portfolio nesting depth — single portfolio layer, or arbitrary nesting for enterprise hierarchies? | PF-3.05 | unassigned |
| Tagging vocabulary — free-text tags or a managed per-organization vocabulary? | PF-4.07 "add tag" | unassigned |
| Automation action set — finalize the predefined triggers, conditions, and actions before building the engine | PF-5.04 | unassigned |

---

## Closed

Kept so the same gap is not re-filed. Each row names the evidence checked when it was closed.

| Item | Closed | Evidence |
| --- | --- | --- |
| Response DTOs — expose the `xmin` concurrency token on work responses | 2026-09-09, M3 slice | `GET /api/work/` returns a flat work DTO carrying `version`; `GET /api/work/{id}` returns `{ item, version }`; `PUT` and the assignment routes require the version and answer 409 on a mismatch. Narrowed to the remaining non-work read endpoints under *Deferred* |
| `apps/web` has no lockfile and is outside CI | 2026-09-09, M3 slice | `apps/web/package-lock.json` exists (218 KB); `.github/workflows/ci.yml:24-40` is the `web` job — `npm ci`, `npm run format`, `npm run lint`, `npm run build` — and `ci.yml:41-88` is the Playwright `e2e` job |
| `apps/web` has no ESLint/Prettier | 2026-09-09, M3 slice | `apps/web/eslint.config.mjs` extends `eslint-config-next/core-web-vitals`; `.prettierrc.json` + `.prettierignore` exist; `package.json` declares `eslint` 9.39.1, `eslint-config-next` 16.3.4 and `prettier` 3.6.2 |
| `"lint": "next lint"` is broken under Next 16 | 2026-09-09, M3 slice | `apps/web/package.json` now runs `"lint": "eslint ."` and `"format": "prettier --check ."` |
| Stale `apps/web` README ("not runnable"), `useState<any>` | 2026-09-09, M3 slice | `apps/web/README.md` documents the running client and the `/api` proxy; no `any` remains in `apps/web/app` or `apps/web/lib` |
| No unit tests for any M3 domain type | 2026-09-09, M3 slice | `tests/PropFlow.UnitTests/M3DomainTests.cs` covers the property hierarchy + IANA time-zone validation (`:14`), extended `WorkItem` fields and the cost guard (`:28`), a non-work `TimelineEntry` (`:46`), and the vendor/employee contact lifecycle (`:61`), alongside `CapabilitiesTests.cs`. Not everything the original row listed is covered — the `Publish`/`Schedule`/`ChangeStatus` transitions and `WorkCategory` still have no direct unit test — but the blanket "none covered" claim no longer holds |
| CI does not run `tests/PropFlow.UnitTests`; no web or e2e job | 2026-09-09, M3 slice | `.github/workflows/ci.yml:17` runs the unit tests inside `verify`; the `web` (`:24`) and `e2e` (`:41`) jobs exist. PF-3.25 is done |
| `seed-demo` covers only four of the eight `WorkStatus` values | 2026-09-09, PF-3.22 | `tools/PropFlow.Admin/Program.cs` now seeds 12 work items per organization spanning all eight statuses and all four priorities, four of them left in `New` |
| Work create/update accept unvalidated `BuildingId`/`SpaceId`/`ResidentId`/`CategoryId` (audit A-5) | 2026-09-10, audit follow-ups | `EfWorkOperations.EnsureChildReferencesAsync` validates all four (tenant-filtered) on create + update; `UpdateAsync` also checks `PropertyId`; test `Creating_or_updating_work_rejects_a_child_reference_outside_the_tenant` |
| Global search trigram branch is not index-backed (audit A-7) | 2026-09-10, audit follow-ups | `EfGlobalSearch` filters with `q <% col` (`TrigramsAreWordSimilar`) under a `SET LOCAL pg_trgm.word_similarity_threshold = 0.25`; the `<%` operator uses `gin_trgm_ops`. Threshold and recall unchanged |
| Bulk endpoints: null `items` entry → 500, undefined enum accepted, note skipped concurrency, schedule no-op noise (audit 2nd pass B-1..B-5) | 2026-09-10, PF-4.07 review | `ValidBatch` rejects null/empty-guid entries; `Enum.IsDefined` guards on `bulk/status`, `bulk/priority`, and `POST`/`PUT /api/work`; `BulkApplyAsync` does an explicit up-front `xmin` check for every item; `ApplyOne` skips a true schedule no-op. Tests in `BulkWorkActionsTests` |
| `TimelineEntry` generalization was incomplete (PF-3.10 tech debt) | 2026-09-10, PF-3.10 finish | `TimelineEntry` now has one shape — `Record` + typed `From(VendorAssigned/EmployeeAssigned/WorkReopened)` factories, all populating `EventType`/`OldValue`/`NewValue`/`Changes`. Dead `Create()` removed; legacy `PreviousVendorId`/`VendorId` columns dropped (`20260910122538_TimelineDropLegacyVendorColumns`). Unblocks PF-4.06 |
