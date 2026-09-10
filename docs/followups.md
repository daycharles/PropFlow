# PropFlow follow-ups (parking lot)

Single tracking list of everything the team needs to come back to. Maintained as work lands —
close an item here when the change that resolves it merges. The authorities remain
[`docs/audit-security.md`](audit-security.md), [`docs/audit-communications.md`](audit-communications.md),
[`docs/backlog.md`](backlog.md), and [`docs/milestones.md`](milestones.md); this file only
aggregates their open threads plus the tooling/process gaps.

Owner `daycdev` = the M3 vertical-slice work (currently pushed directly to `main`, on an
unmerged fork). "unassigned" = no owner yet.

---

## Blocked (waiting on M3)

M4 tasks that cannot start until the M3 slice (work API, generalized timeline, bulk actions,
Next.js work list/detail) lands on `main`.

The slice itself is complete on the `feat/pf-3-m3-vertical-slice` branch — bulk vendor
assignment (PF-3.16), the work list/detail UI (PF-3.19) and the demo seed (PF-3.22) all exist
there. These rows stay blocked only until that branch is promoted to `main`, except PF-4.06,
which additionally needs the `TimelineEntry` generalization finished.

| Item | Source | Owner | Unblocks when |
| --- | --- | --- | --- |
| PF-4.06 — communication records + delivery status as timeline entries with explicit visibility | backlog.md M4; audit-communications C11 | unassigned | `TimelineEntry` fully generalized (PF-3.10) — today it is half-done: `Create(...)` overload exists but is unused/dead, `From(VendorAssigned)` still writes legacy `PreviousVendorId`/`VendorId` |
| PF-4.06 (atomicity half) — enqueue the outbox row in the same transaction as the triggering Operations change | audit-communications C11 (accepted) | unassigned | work events start producing messages; needs the M3 work-use-case layer + idempotent real providers |
| PF-4.07 — extended bulk work actions (schedule, status, priority, note, tag, close, reopen; bounded + all-or-nothing) | backlog.md M4 | unassigned | the bulk vendor-assignment endpoint (PF-3.16) exists on `main` |
| PF-4.08 — web "assign & notify" flow (vendor + schedule window + resident message + confirm + summary) | backlog.md M4 | unassigned | PF-4.07 + PF-4.03 + the M3 Next.js bulk vendor flow (PF-3.20) |
| PF-4.09 — mobile-responsive Work list and detail, large touch targets | backlog.md M4 | unassigned | the M3 Next.js work list/detail (PF-3.19) |

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
| Response DTOs for the non-work read endpoints | backlog.md PF-3.15 | unassigned | Work responses now carry an explicit `version`, and the list returns a flat DTO. `/api/residents`, `/api/assets` and `/api/saved-views` still return domain entities straight out |
| Global search (`EfGlobalSearch`) issues 9 sequential round-trips (one per entity type) and ranks in memory | PF-6.08; `src/PropFlow.Infrastructure/Persistence/EfGlobalSearch.cs` | unassigned | Fine at seed-data scale. Revisit with a single `UNION ALL` query (or a materialized search view) if p95 latency or DB load warrants. Also: `limit` is applied per type then again globally, so a type can crowd out others up to `limit`; a per-type floor may be wanted once the PF-6.09 UI groups results |

---

## Tech debt (M3 slice — daycdev)

Flagged in the 2026-09-09 code-quality audit, left for the M3 owner.

| Item | Source | Owner | Notes |
| --- | --- | --- | --- |
| `TimelineEntry` generalization incomplete | code audit 2026-09-09; `src/PropFlow.Domain/Timeline/TimelineEntry.cs` | daycdev | Carries legacy `PreviousVendorId`/`VendorId` alongside `EventType`/`Changes`; `From()` leaves `Changes = "{}"`, `Create()` sets `Changes` + a fresh GUID and skips the typed columns and is never called (dead). Readers get two shapes |
| Tailwind is a dependency but nothing uses it | code audit 2026-09-09; `apps/web/package.json:24`, `apps/web/app/styles.css:1` | daycdev | PF-3.01 called for Tailwind. `styles.css:1` has `@import "tailwindcss"` but there is no PostCSS config and every rule below it is hand-written CSS. Either wire the build up or drop the dependency and the import |
| `WorkItem.Schedule` / `Edit` don't guard terminal states | code audit 2026-09-09; `src/PropFlow.Domain/Work/WorkItem.cs` | daycdev | **Narrowed by PF-3.27.** `AssignVendor` and `AssignEmployee` now refuse `Completed`/`Cancelled` work via `RefuseWhenTerminal()`, surfaced as `AssignmentOutcome.NotAssignable` and a 400; `Schedule` and `Edit` still skip the guard, so a terminal item can still be re-scheduled or edited. `CreatedAt` is also only set in `Publish`, so a `Draft` row carries `default(DateTimeOffset)` |
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
| `main` branch protection not configured | Slack #agent-updates 2026-09-09; CLAUDE.md ("Protected: `verify` must be green") | unassigned | `verify` is not a required check, so direct pushes to `main` bypass CI. This already broke `main` once (commit `8c88b20`, recovered by PR #79). Blocked because the PAT lacks the Administration scope |
| PAT scope gaps | Slack #agent-updates 2026-09-09 | unassigned | Fine-grained PAT lacks `workflow` (blocks the CI fix above), Administration (blocks branch protection), and **Projects** (an agent session on the macOS box can assign/close issues but cannot move Projects-v2 cards — `gh project …` → `Resource not accessible by personal access token`). Board relies on built-in *item closed → Done* / auto-add workflows in the meantime. Needs a re-scoped token |
| `gh` on the macOS box was hand-installed to `~/.local/bin/gh` (2.63.2) | 2026-09-09 agent session | unassigned | Not from a package manager, not on `PATH` by default in every shell, older than the 2.97.0 the workflow doc assumed. Pin/upgrade deliberately or add to the documented toolchain setup |
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
