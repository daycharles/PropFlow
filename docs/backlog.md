# PropFlow backlog

Epics and tasks for the remaining roadmap (milestones 3–6 from [milestones.md](milestones.md)).
Milestones 1 and 2 (repository/foundation, tenant-aware identity + PostgreSQL persistence) are
implemented. This document is the source for GitHub milestones and issues.

## How this maps to GitHub

- Each **epic** below becomes one GitHub **milestone** (`M3` … `M7`) and one tracking **issue**
  labelled `epic`.
- Each **task** (`PF-3.01`, …) becomes one **issue**, assigned to the epic's milestone, linked
  from the epic issue's checklist.
- Suggested labels: `epic`, `area:web`, `area:api`, `area:application`, `area:domain`,
  `area:infra`, `area:tests`, `area:ci`, `type:feature`, `type:chore`, `type:test`.
- Estimates: `S` ≈ ≤1 day, `M` ≈ 2–3 days, `L` ≈ 4–8 days, `XL` needs a split before starting.

Task IDs are stable references; do not renumber. Add new tasks with the next free number.

---

## Epic M3 — First usable vertical slice

**Milestone:** `M3`
**Goal:** Login → open Work → multi-select → assign vendor → confirm → see updated timeline,
running end to end through a real Next.js UI against persisted property/vendor/work data.
**Definition of done:** the demo workflow above works in a browser against seeded data;
integration + e2e tests cover the success and failure paths; CI builds and tests the web app.

| ID | Task | Area | Est | Depends on |
| --- | --- | --- | --- | --- |
| PF-3.01 | Scaffold `apps/web` Next.js app (TypeScript, App Router, Tailwind, ESLint/Prettier, env config) | web | M | — |
| PF-3.02 | API client + session layer in web: CSRF token fetch/refresh, `no-store`, 401/403 handling, cookie passthrough | web | M | PF-3.01 |
| PF-3.03 | Auth UI: login page (email / password / organizationId), logout, session bootstrap on load | web | M | PF-3.02 |
| PF-3.04 | Capability-gated routing/navigation in web (hide/deny routes by `capabilities` from `/api/session`) | web | S | PF-3.03 |
| PF-3.05 | Domain + persistence: `Portfolio`, `Property`, `Building`, `Unit/Space` tenant entities with the Organization→Portfolio→Property→Building→Unit hierarchy; not apartment-specific | domain | L | — |
| PF-3.06 | Migration + RLS policies/grants for the property hierarchy tables (follow the per-table RLS rule in architecture.md) | infra | M | PF-3.05 |
| PF-3.07 | Expand `Vendor` (contact info, trade/category, active flag) and add `Employee`/staff entity; migrations + RLS | domain | M | — |
| PF-3.08 | Expand `WorkItem`: description, workType, category, priority, status, property/building/unit/resident refs, scheduledStart/End, dueDate, createdDate, completedDate, cost, internal vs resident-visible notes | domain | L | PF-3.05, PF-3.07 |
| PF-3.09 | Migration + RLS for the expanded work schema; composite tenant FKs for new work relationships | infra | M | PF-3.08 |
| PF-3.10 | Generalize the timeline: `TimelineEntry` records arbitrary event type, actor, old value, new value, related object refs (not just `VendorAssigned`) | domain | M | PF-3.08 |
| PF-3.11 | Application use cases: create work, update work (low-risk field edits), with `Work.Create` / `Work.Update` capabilities mapped centrally | application | M | PF-3.08 |
| PF-3.12 | Emit timeline events for work created, status changed, priority changed, scheduled, vendor assigned, employee assigned | application | M | PF-3.10, PF-3.11 |
| PF-3.13 | API: work create/update endpoints; vendor list/detail; property-hierarchy read endpoints; employee list — all tenant-scoped, CSRF on mutations | api | M | PF-3.11 |
| PF-3.14 | API: work list filtering + search (category, status, priority, property/unit, text) with sort and pagination beyond the current top-100 | api | M | PF-3.13 |
| PF-3.15 | Client-facing concurrency token: expose the work version (xmin) in work responses; require it on updates; 409 on mismatch | api | S | PF-3.13 |
| PF-3.16 | API: bulk vendor assignment endpoint — bounded batch size, all-or-nothing transaction, per-item concurrency check, one timeline entry per item, `Work.AssignVendor` | api | L | PF-3.15, PF-3.12 |
| PF-3.17 | Server-side saved views (named filter + column sets, tenant + user scoped): entity, migration, CRUD endpoints | api | M | PF-3.14 |
| PF-3.18 | Web: Work list view — table, filters, text search, sortable columns, multi-select, bulk-action toolbar | web | L | PF-3.04, PF-3.14 |
| PF-3.19 | Web: unified Work detail workspace — details, scheduling, vendor, employee, internal notes, timeline; autosave low-risk fields, explicit confirm for consequential actions | web | L | PF-3.13, PF-3.15 |
| PF-3.20 | Web: bulk "assign vendor" flow — select → assign vendor → confirm → success summary → refreshed timeline | web | M | PF-3.18, PF-3.16 |
| PF-3.21 | Web: saved views UI (create/apply/delete, default view) | web | S | PF-3.18, PF-3.17 |
| PF-3.22 | Seed data: two organizations for isolation plus a minimal property/vendor/employee/work dataset covering each status and priority | infra | M | PF-3.09 |
| PF-3.23 | Integration tests: assignment rollback on audit failure, unauthorized assignment (403), stale update (409), audit entry created, cross-tenant read/write blocked, bulk all-or-nothing | tests | L | PF-3.16 |
| PF-3.24 | e2e (Playwright): the full vertical-slice workflow against seeded data, in CI | tests | M | PF-3.20, PF-3.22 |
| PF-3.25 | CI: add web install/lint/build/unit steps and the Playwright job to `.github/workflows/ci.yml` | ci | S | PF-3.01 |
| PF-3.26 | Update `docs/api.md` and `docs/local-development.md` for the new endpoints and the web dev server | chore | S | PF-3.13 |

---

## Epic M4 — Polished pest-control workflow

**Milestone:** `M4`
**Goal:** the bulk pest-control demo — filter to a category, select ~12 work orders,
assign a vendor, schedule a window, notify residents, confirm — completes as one fast operation
with a success summary, durable communication dispatch, and a full seeded demo dataset.

| ID | Task | Area | Est | Depends on |
| --- | --- | --- | --- | --- |
| PF-4.01 | `Resident`/`Person` domain + occupancy (unit ↔ resident over time); migrations + RLS | domain | L | PF-3.05 |
| PF-4.02 | Contact channels (SMS/email) with per-channel consent state on residents | domain | M | PF-4.01 |
| PF-4.03 | Resident message templates: entity + CRUD endpoints + variable substitution (work, property, schedule fields) | api | M | PF-4.01 |
| PF-4.04 | Communication provider abstraction with mock SMS + mock email implementations (no real send) | application | M | PF-4.02 |
| PF-4.05 | Transactional outbox + idempotent dispatch worker for communications | infra | L | PF-4.04 |
| PF-4.06 | Communication records + delivery status surfaced as timeline entries with explicit visibility | application | M | PF-4.05, PF-3.10 |
| PF-4.07 | Extend bulk actions: schedule, change status, change priority, add note, add tag, close, reopen — bounded + all-or-nothing | api | L | PF-3.16 |
| PF-4.08 | Bulk "assign & notify" flow in web: vendor + schedule window + resident message + confirm + success summary | web | L | PF-4.07, PF-4.03, PF-3.20 |
| PF-4.09 | Mobile-responsive Work list and detail; large touch targets for field use | web | M | PF-3.19 |
| PF-4.10 | Seed: Tidewater Residential Management — 3 properties, ~10 buildings, ~80 spaces, ~70 residents, 6 vendors, 5 employees, ~100 work items, ~60 assets, ≥18 pest-control requests, mix of emergency/overdue/completed | infra | M | PF-4.01, PF-3.22 |
| PF-4.11 | e2e: bulk pest-control assignment + notify demo workflow in CI | tests | M | PF-4.08, PF-4.10 |
| PF-4.12 | Tests: outbox idempotency (no duplicate sends on retry), template rendering, consent respected | tests | M | PF-4.05 |

---

## Epic M5 — Events and field workflows

**Milestone:** `M5`
**Goal:** technicians work assigned jobs from a phone (including an "On The Way" status that
notifies the resident), and admins configure simple automation rules instead of hard-wired
workflows.

| ID | Task | Area | Est | Depends on |
| --- | --- | --- | --- | --- |
| PF-5.01 | Property- and assignment-level authorization scope model (regional / technician / vendor); define before field access ships | application | L | PF-3.04 |
| PF-5.02 | Grant `Technician` and `Vendor` capabilities scoped to assigned work / assigned property | application | M | PF-5.01 |
| PF-5.03 | Technician "On The Way" status workflow: status transition, timeline event, triggers resident communication | application | M | PF-4.06, PF-5.02 |
| PF-5.04 | Automation rules engine: persisted WHEN/IF/THEN rules over domain events, a predefined action set, evaluation on dispatch | application | XL (split) | PF-4.06 |
| PF-5.05 | Automation rules admin UI (list, create, enable/disable) — predefined triggers/conditions/actions only | web | L | PF-5.04 |
| PF-5.06 | Notes with explicit visibility (internal vs resident-visible) across API and web, consistently enforced | api | M | PF-3.19 |
| PF-5.07 | Communication status (queued / sent / delivered / failed) shown inline in the work timeline | web | S | PF-4.06 |
| PF-5.08 | Remaining authorized bulk actions from the handoff (send resident message, assign employee) wired to the bulk toolbar | api | M | PF-4.07 |
| PF-5.09 | Technician mobile view: my assigned work, status changes, add note/photo | web | L | PF-5.02, PF-4.09 |
| PF-5.10 | e2e: technician On The Way demo workflow in CI | tests | M | PF-5.03, PF-5.09 |
| PF-5.11 | Tests: scope model denies cross-property/cross-assignment access for technician + vendor | tests | M | PF-5.01 |

---

## Epic M6 — Assets and attention

**Milestone:** `M6`
**Goal:** assets carry maintenance history, repeat repairs are flagged automatically, the home
screen is an actionable attention queue rather than a chart wall, global search spans all
entities, and the integration abstraction exists (mocked).

| ID | Task | Area | Est | Depends on |
| --- | --- | --- | --- | --- |
| PF-6.01 | `Asset` domain: type, manufacturer, model, serial, install date, warranty expiration, expected service life, condition, replacement cost estimate, notes, photos; migrations + RLS | domain | L | PF-3.06 |
| PF-6.02 | Link work items to an optional asset; expose on work create/update and detail workspace | api | M | PF-6.01, PF-3.13 |
| PF-6.03 | Asset detail page with complete maintenance history (all linked work + costs) | web | M | PF-6.02 |
| PF-6.04 | Configurable repeat-repair detection (default: 3 repairs on an asset within 120 days; category-similarity option) | application | M | PF-6.02 |
| PF-6.05 | "Repeat Repair Warning" surface: repair count, total repair cost, asset age; shown on work create and asset page | web | M | PF-6.04 |
| PF-6.06 | Attention queue backend: rules for unassigned emergencies, overdue work, SLA breach, waiting-on-vendor, waiting-on-resident, repeat repair, unit-turn-at-risk | application | L | PF-6.04 |
| PF-6.07 | "Needs Your Attention" home screen: Critical / Warning / Informational cards, each click-through to a filtered work view | web | L | PF-6.06 |
| PF-6.08 | Fuzzy global search across properties, buildings, units, residents, vendors, work orders, assets | api | L | PF-6.01 |
| PF-6.09 | Global search UI (keyboard-first) in web | web | M | PF-6.08 |
| PF-6.10 | Integration adapter abstraction: canonical Property/Space/Person-Occupancy/Work/Asset objects, external ID + source system + last sync + sync status tracking; one mock adapter | application | L | PF-6.01 |
| PF-6.11 | Integration Health screen foundation: connected system, status, last successful sync, failure count, unresolved conflicts | web | M | PF-6.10 |
| PF-6.12 | Keep unusable reports/integrations out of main navigation until functional | web | S | PF-6.11 |
| PF-6.13 | e2e: repeat HVAC repair demo workflow in CI | tests | M | PF-6.05 |

---

## Epic M7 — Deployment and platform hardening (cross-cutting)

**Milestone:** `M7`
**Goal:** close the production gaps called out in architecture.md. Pull tasks forward into
earlier milestones when a feature forces the issue.

| ID | Task | Area | Est | Depends on |
| --- | --- | --- | --- | --- |
| PF-7.01 | Attachment/photo storage service: upload, tenant-scoped access control, resident vs internal visibility, retention | infra | L | PF-3.19 |
| PF-7.02 | Multi-instance readiness: shared encrypted Data Protection keys, trusted proxy configuration, managed secrets, PostgreSQL TLS | infra | L | — |
| PF-7.03 | Observability pass: structured log review, request trace IDs end to end, basic metrics/health dashboards | infra | M | — |
| PF-7.04 | Scheduling model: store UTC instants plus property IANA time zones; render local times in web | domain | M | PF-3.08 |
| PF-7.05 | Message consent + provider callback + retry/retention controls for real communication providers | application | L | PF-4.05 |
| PF-7.06 | RLS/grants review checklist enforced in PR template for every new business table | ci | S | — |

---

## Open questions / decisions needed

- **SLA model** (referenced by PF-6.06): is SLA a per-priority duration, a per-category policy,
  or configurable per organization? Needed before the attention queue.
- **Unit-turn workflow** (PF-6.06 "unit turn at risk"): is a unit turn a work type, a distinct
  entity with checklist steps, or out of scope until a later milestone?
- **Saved views scope** (PF-3.17): user-private only, or shareable/organization-default views?
- **Portfolio depth** (PF-3.05): single portfolio layer, or arbitrary nesting for enterprise
  hierarchies?
- **Tagging** (PF-4.07 "add tag"): free-text tags or a managed tag vocabulary per organization?
- **Automation action set** (PF-5.04): finalize the predefined triggers, conditions, and actions
  for the first version before building the engine.
