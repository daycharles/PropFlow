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
| `apps/web` has no lockfile and is outside CI | audit-security M-3 (open) | daycdev (M3 web work) | Non-reproducible frontend, no `npm audit` / `next build` in CI. See also the CI item under Tooling / process |
| Capped-exponential backoff + transient/permanent error classification for real providers | audit-communications C3 (deferred) → PF-7.05 | unassigned | Current retry is fixed 2-min spacing, cap 8 (~14 min outage tolerance); config-bound |
| Message consent + provider callback + retry/retention controls for real communication providers | backlog.md PF-7.05; audit-security L-3 residue | unassigned | Context-aware template encoding + a recipient/egress policy for the real provider adapter |
| Templates do not flag an unterminated `{{` (`"arriving at {{time"` renders literally) | audit-communications C12 (accepted) | unassigned | Low impact for authored templates; revisit if templates become user-generated at scale |
| `M3Operations` migration is not safe against a populated milestone-2 database | milestones.md M3 migration note; audit-communications C-audit M6 | daycdev | Adds NOT NULL `PropertyId`/`CreatorId` + FK in one step with a `Guid.Empty` default and backfills `Vendors.IsActive = false`. Applies cleanly only to a DB with no pre-M3 `WorkItems`/`Vendors` rows. Squash/rewrite (nullable → backfill → NOT NULL → FK) before first real deployment |
| Response DTOs — expose the `xmin` concurrency token on work responses; stop returning entities directly | backlog.md PF-3.15 | unassigned | Endpoints currently return domain entities (`Resident`, work) straight out |
| Global search (`EfGlobalSearch`) issues 9 sequential round-trips (one per entity type) and ranks in memory | PF-6.08; `src/PropFlow.Infrastructure/Persistence/EfGlobalSearch.cs` | unassigned | Fine at seed-data scale. Revisit with a single `UNION ALL` query (or a materialized search view) if p95 latency or DB load warrants. Also: `limit` is applied per type then again globally, so a type can crowd out others up to `limit`; a per-type floor may be wanted once the PF-6.09 UI groups results |

---

## Tech debt (M3 slice — daycdev)

Flagged in the 2026-09-09 code-quality audit, left for the M3 owner.

| Item | Source | Owner | Notes |
| --- | --- | --- | --- |
| `TimelineEntry` generalization incomplete | code audit 2026-09-09; `src/PropFlow.Domain/Timeline/TimelineEntry.cs` | daycdev | Carries legacy `PreviousVendorId`/`VendorId` alongside `EventType`/`Changes`; `From()` leaves `Changes = "{}"`, `Create()` sets `Changes` + a fresh GUID and skips the typed columns and is never called (dead). Readers get two shapes |
| `apps/web` quality gaps | code audit 2026-09-09; `apps/web/` | daycdev | No Tailwind despite PF-3.01; `"lint": "next lint"` broken (removed in Next 16); no `package-lock.json`; stale README ("not runnable"); `useState<any>`; no ESLint/Prettier; `next.config.ts` proxy defaults to self-signed `https://localhost:5001` (undocumented) |
| No unit tests for any M3 domain type | code audit 2026-09-09; CLAUDE.md test-gate | daycdev | `WorkItem` state machine (`Publish`/`Schedule`/`ChangeStatus`/`Edit`), `WorkCategory`, `PropertyHierarchy` timezone validation, generalized `TimelineEntry` — none covered. M4 comms is well covered by contrast |
| `WorkItem.Schedule` / `Edit` don't guard terminal states | code audit 2026-09-09; `src/PropFlow.Domain/Work/WorkItem.cs` | daycdev | `ChangeStatus` enforces the terminal-state guard; `Schedule`/`Edit` skip it. `CreatedAt` is only set in `Publish` |
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
| CI does not run `tests/PropFlow.UnitTests` | code audit H1; `.github/workflows/ci.yml`; CLAUDE.md step 3 | unassigned | 85+ xUnit tests are compiled but never executed in CI (`verify` runs FoundationChecks + IntegrationTests only). One-line `ci.yml` fix; blocked because the PAT lacks the `workflow` scope — needs a token with Workflows:write or a manual apply. Also missing: an `apps/web` install/lint/build job and the Playwright e2e job (PF-3.25) |
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
