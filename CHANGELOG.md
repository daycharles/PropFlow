# Changelog

All notable changes to PropFlow are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Every line traces to a `PF-x.yy` task id from [docs/backlog.md](docs/backlog.md), which is the
task source and whose ids are stable references. Per-milestone narrative lives in
[docs/milestones.md](docs/milestones.md); open threads live in [docs/followups.md](docs/followups.md).

## [1.0.0-rc.1] - 2026-09-11

First named release. All seven milestones are delivered and on `main`: task issues `#6`–`#73`
and epic issues `#1`–`#5` are closed. This is a **release candidate**, not a final release —
the `M3Operations` migration set is still unsquashed, so a fresh install is supported and an
upgrade from an older database is not. See *Known limitations* below and
[docs/RELEASE-TESTING.md](docs/RELEASE-TESTING.md) to stand the stack up.

Milestones 1 and 2 (repository/foundation; tenant-aware ASP.NET Identity login, secure cookies
and CSRF, organization membership and capability checks, PostgreSQL persistence and migrations,
database readiness) predate this changelog and are implemented.

### Added — milestone 3, first usable vertical slice (`#6`–`#31`, PF-3.01–PF-3.28)

- The Organization → Portfolio → Property → Building → Space hierarchy, with IANA time zones on
  properties (PF-3.02–PF-3.06).
- Expanded `Vendor` / `Employee` / `WorkItem` entities with migrations and forced RLS, and the
  generalized `TimelineEntry` — one shape with typed factories, the legacy vendor columns
  dropped (PF-3.07, PF-3.09, PF-3.10).
- Work create and update behind `Work.Create` / `Work.Update`, and a filtered, sorted, paginated
  work list carrying the `xmin` version on every row (PF-3.08, PF-3.11, PF-3.15).
- Single and bulk vendor assignment plus employee assignment, all-or-nothing with a per-item
  version check (PF-3.12, PF-3.13).
- User-scoped saved views (PF-3.17).
- A demo seed covering every `WorkStatus` and `WorkPriority` (PF-3.22).
- The Next.js work list, work detail and saved-views UI in `apps/web` (PF-3.18–PF-3.21).
- The `web` and `e2e` CI jobs (PF-3.25).
- Vendor/employee assignment refused on `Completed` / `Cancelled` work, plus
  `docs/demo-script.md` (PF-3.27 — the one M3 task with no tracker issue).
- Work-detail schedule times rendered in local time (PF-3.28).

### Added — milestone 4, polished pest-control workflow (`#32`–`#43`, PF-4.01–PF-4.12)

- The resident/occupancy domain — `Resident` and `Occupancy` with contact fields, per-channel
  SMS/email consent and `AllowsContact`, forced-RLS `operations` tables, and the read plus
  `People.Manage` write endpoints (PF-4.01, PF-4.02).
- The communication provider abstraction with recording mock SMS and email senders (PF-4.04).
- The `communications`-schema transactional outbox with an idempotency key and an `xmin`-claimed
  at-most-once dispatch worker (PF-4.05).
- Resident messaging — `POST /api/work/{id}/message` renders a template against the work item's
  resident (schedule values in the property's time zone) and queues it; outbox rows carry
  `WorkId` and `ResidentVisible`, and `GET /api/work/{id}/timeline` folds them in (PF-4.03,
  PF-4.06).
- The extended bulk work actions — `bulk/status`, `bulk/priority`, `bulk/schedule`, `bulk/note`,
  `bulk/reopen` — each bounded to 100 items in one all-or-nothing transaction with a per-item
  version check (PF-4.07).
- The **Assign & notify** flow: one vendor, an optional visit window, an optional resident
  message, applied as `bulk/vendor` → `bulk/schedule` → per-item send with a per-step outcome
  summary (PF-4.08).
- The mobile-responsive work list and detail (PF-4.09).
- The deterministic Tidewater Residential Management seed — 3 properties, 10 buildings, ~80
  spaces, ~70 residents with mixed consent, 6 vendors, 5 employees, 6 categories, ~70 assets and
  100 work items across every status and priority, 30 of them pest control (PF-4.10).
- The bulk pest-control demo e2e spec, and the outbox-idempotency / template-rendering / consent
  test pass (PF-4.11, PF-4.12).

### Added — milestone 5, events and field workflows (`#44`–`#54` and later, PF-5.01–PF-5.14)

- The property/assignment authorization scope model (`WorkAccessScope`) and the scoped
  `Technician` / `Vendor` grants — a field role is inert until its membership names the
  employee or vendor, then narrowed to assigned work (PF-5.01, PF-5.02).
- The "On The Way" status workflow with its timeline event and the `Work.MarkOnTheWay`
  capability (PF-5.03).
- The persisted WHEN/IF/THEN automation rules with a predefined action set and their admin
  screen at `/settings/automation` behind `Settings.ManageAutomationRules` (PF-5.04, PF-5.05).
- The automation evaluator: `EfWorkOperations` raises `WorkCreated` / `WorkStatusChanged` after
  the change commits, `IAutomationEngine` matches the tenant's enabled rules and applies
  `SetPriority` / `SendResidentMessage` idempotently, each rule fenced and isolated (PF-5.13).
- The `WorkNoteAdded` trigger with a consent-honoring resident notification — a resident-visible
  note fires a rule whose template can render `{{ note.text }}`, deduped through the outbox,
  with queued/skipped status on the timeline (PF-5.14).
- Visibility-scoped notes across API and web (PF-5.06).
- Inline communication status on the work timeline (PF-5.07).
- The message and employee bulk actions on the toolbar, later joined by a "Bulk edit…" flow for
  status, priority, schedule, note and reopen (PF-5.08, PF-5.12).
- A technician mobile view, the `technician-on-the-way.spec.ts` e2e, and the scope-model denial
  tests (PF-5.09–PF-5.11).

### Added — milestone 6, assets and attention (`#55`–`#67`, PF-6.01–PF-6.15)

- The `Asset` domain with forced RLS and read/write API behind `Assets.Manage`; the optional
  same-property `WorkItem.AssetId` link with an `AssetLinked` timeline entry; and the
  `/assets/[id]` detail page over `GET /api/assets/{id}/history` with
  `workOrderCount` / `totalCost` / `ageInYears` roll-ups (PF-6.01–PF-6.03).
- Configurable repeat-repair detection — a per-org `RepeatRepairPolicy` (default 3 repairs /
  120 days) upserted via `GET`/`PUT /api/assets/repeat-repair-policy`, with
  `GET /api/assets/{id}/repeat-repair` and the `RepeatRepairWarning` surface on the asset and
  work-detail pages (PF-6.04, PF-6.05).
- The attention queue — `AttentionRules` evaluates seven rules over every open work item,
  `GET /api/attention` returns them most-urgent-first with per-severity distinct-work counts,
  and `/attention` renders Critical / Warning / Informational cards that filter the list
  (PF-6.06, PF-6.07, PF-6.14).
- Fuzzy global search — `GET /api/search` behind `Work.Read` using `pg_trgm` substring plus
  `word_similarity` over GIN trigram indexes, and the `CommandSearch` palette
  (`Cmd`/`Ctrl+K`, `/`, or the header button) with arrow-key navigation (PF-6.08, PF-6.09).
- Integrations — the `IIntegrationAdapter` abstraction with canonical records, a
  `MockIntegrationAdapter`, the forced-RLS `integrations` schema (`IntegrationConnection` +
  `ExternalRecordLink`) with per-record sync state, `/api/integrations` behind
  `Integrations.Manage`, and the `/integrations` Health screen (PF-6.10, PF-6.11).
- Capability-driven navigation curation, so a role sees only usable destinations and no unbuilt
  surface can slip in (PF-6.12).
- The `repeat-hvac-demo.spec.ts` e2e and e2e spec robustness work (PF-6.13, PF-6.15).

### Added — milestone 7, deployment and platform hardening (`#68`–`#73`, PF-7.01–PF-7.06)

- **Attachments** — the `Attachment` domain entity, a forced-RLS `Attachments` table and
  `IAttachmentStorage` (`LocalAttachmentStorage`, path-traversal guarded).
  `/api/work/{id}/attachments` upload (25 MB; PDF, JPEG, PNG, WebP, HEIC), list, stream-download
  and delete behind `Work.ManageAttachments`, every route narrowed by the field-role scope check
  so a Technician only touches their own work. A `residentVisible` flag, and `retainUntil`
  enforced by `AttachmentRetentionSweep` (a per-tenant hosted service that removes blob and row).
  Web: the attachments panel on work detail and an "Add photo" flow on the technician view
  (PF-7.01).
- **Multi-instance readiness** — shared encrypted Data Protection keys, the trusted-proxy /
  forwarded-headers trust boundary, `Attachments:RootPath` and secret configuration gates, and
  PostgreSQL TLS, with `DeploymentConfigurationTests` (PF-7.02).
- **Observability** — structured logs, end-to-end request trace IDs, and a `/health/metrics`
  snapshot, with `ObservabilityTests` (PF-7.03).
- **Scheduling model** — UTC instants plus property IANA time zones, projected to local times in
  web (PF-7.04).
- **Real communication providers** — message consent, signed provider callbacks, and retry /
  retention controls (PF-7.05).
- **RLS/grants review checklist** enforced in the PR template for every new business table
  (PF-7.06).

### Release mechanics (this release)

- `VersionPrefix` / `VersionSuffix` in `Directory.Build.props`, applying `1.0.0-rc.1` to all 8
  projects; `version` in `apps/web/package.json`.
- This `CHANGELOG.md`, and `docs/RELEASE-TESTING.md` — a macOS/zsh-first walkthrough for
  standing the stack up from a tag and exercising it.

### Fixed — documentation drift

- `docs/backlog.md` and `README.md` said milestone 7 was "the remaining track"; every M7 issue
  is closed.
- `docs/local-development.md` said the API listens on `https://localhost:7080`; `.env.example`,
  `next.config.ts:7`, `docs/demo-script.md` and the CI `e2e` job all say `https://localhost:5001`.
  Standardized on 5001, and replaced the note that documented the mismatch with the actual
  host-name constraint.
- `docs/local-development.md` said the Tidewater seed creates 3 message templates;
  `tools/PropFlow.Admin/TidewaterSeed.cs:281-292` seeds 4 — the M5 "Technician on the way"
  template was missing from the prose.
- `.github/workflows/ci.yml` comment citations for the 20-character runtime password, the
  `__Host-` cookie policy, the Data Protection key-path gate and HSTS all pointed at lines that
  have since moved. Behaviour unchanged.

### Known limitations

- Resident messaging defaults to the **mock outbox** — messages are recorded, not delivered.
  Real providers are configurable (PF-7.05) but unconfigured in a default install.
- The `M3Operations` migration set is **not squashed** ([docs/followups.md](docs/followups.md),
  *Deferred*). Fresh installs are fine; there is no proven upgrade path from an older database.
- The `identity` schema has **no RLS** (audit-security M-1). Not exploitable today — there is no
  member-listing endpoint and `MembershipAccess` is scoped — but it must be decided before one
  exists.
- The attention queue and global search are **unpaged**, and global-search hits for
  building / resident / vendor / employee dead-end at a title-only work search.
- There is **no settings screen** for the repeat-repair policy (API only), and integration sync
  tracks external records without reconciling them into the domain tables.
- **No container image and no hosted environment** — this is a source build only.
- The `apps/web` CSP and the http→https redirect are left to an ingress or proxy. The API itself
  sends HSTS (outside Development), `X-Content-Type-Options`, `Referrer-Policy`,
  `X-Frame-Options: DENY`, `Cross-Origin-Resource-Policy` and
  `Content-Security-Policy: default-src 'none'`.

[1.0.0-rc.1]: https://github.com/daycharles/PropFlow/releases/tag/v1.0.0-rc.1
