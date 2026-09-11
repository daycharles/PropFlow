# Full-suite scope

PropFlow's Milestone-1-through-7 roadmap (see [milestones.md](milestones.md) and
[backlog.md](backlog.md)) delivered the first usable release: login, work orders, bulk
actions, automation, assets, and integrations, now at `v1.0.0-rc.2`. This document tracks the
larger, full-suite roadmap that sits on top of that release — the "build it in sections, end up
comparable in scope to Yardi/AppFolio/Buildium" plan — as GitHub epics (`FS-Ex`) and stories
(`FS-Sxx`), organized into five **Gates**, each split into an **A** (platform/backend) and
**B** (workflows/UX) track so two developers can work a gate in parallel.

Counts and status here drift; re-measure with `gh issue list --state all` rather than
propagating them by hand.

## Gate → epic → milestone map

| Gate | Epic | A track (milestone) | B track (milestone) |
| --- | --- | --- | --- |
| 1 — Core PMS | FS-E1: Core PMS foundation and administration (#184) / FS-E2: Leasing and resident lifecycle (#185, done) | `FS-G1A` — Platform foundation | `FS-G1B` — Core PMS workflows |
| 2 — Financial | FS-E3: Financial operations (#186) | `FS-G2A` — Financial platform | `FS-G2B` — Financial workflows |
| 3 — Operations | FS-E4: Property operations expansion (#187) | `FS-G3A` — Operations platform | `FS-G3B` — Operations workflows |
| 4 — Ecosystem | (integrations/engagement, split across E4/E5) | `FS-G4A` — Ecosystem platform | `FS-G4B` — Engagement and insight |
| 5 — Release | FS-E5: Engagement, insight, integrations, and release hardening (#188) | `FS-G5A` — Release engineering | `FS-G5B` — Full-suite acceptance |

Story issues (`FS-S01`–`FS-S20`) hang off these epics; each is assigned to the `A` or `B`
milestone that matches its platform-vs-workflow split. A story becomes a set of task issues
(`PF-Sxx.NN`) the same way `docs/backlog.md` turned each M3–M7 epic into `PF-x.xx` tasks — write
the task breakdown into this file as each story starts, and cut the GitHub issues from it.

## Gate 1 — Core PMS

### FS-G1A — Platform foundation (#184 epic, #189 FS-S01, #191 FS-S03)

**FS-S01 — Identity, organizations, permissions, and audit (#189).** Extends the Milestone-2
identity foundation (`IdentityStore`, `MembershipAccess`, `SessionAuthentication`,
`OrganizationSlug` — plain string `Role` per membership, capabilities derived from a fixed
`Capabilities.ForRole` switch) rather than replacing it. Acceptance: role matrix, invitation
flow, negative authorization tests, tenant isolation tests, audit history, browser coverage.

**All of PF-S01.01–.09 is now build- and test-verified**, as of the invite/accept UI + Playwright
commits on this branch: `dotnet build` is clean, `PropFlow.UnitTests` is 228/228,
`PropFlow.IntegrationTests` is 188/188, and the full Playwright e2e suite is 19/19 (all run locally
against a real Postgres and a real dev stack — the agent session that wrote this code could not
run any of this itself, since `dotnet restore` there is blocked by an organizational egress policy
on `api.nuget.org`; verification happened afterward, on a real machine). That first real run also
caught a genuine bug, not just an unverified-code gap: `DatabaseProvisioner.ConfigureRuntimeAsync`
never granted the restricted `propflow_app` runtime role write access to any of the six new tables
(`Invitations`, `RoleCapabilityOverrides`, `Teams`, `TeamMemberships`, `UserSessions`,
`AuditEntries`), or to `identity.Memberships`/`AspNetUsers` for the new write paths
invitation-acceptance and membership-management need. It had been silently broken since
PF-S01.02/03 — nothing exercised it until PF-S01.06 made every login write a `UserSession` row,
which turned it into every integration test's login helper 500ing. Fixed in the same commit as the
EF migration. A second bug turned up while building PF-S01.09's UI: `GET
/api/organizations/current/invitations` was returning the raw invitation `Token` in the
pending-invitations list, contradicting its own code comment that the token is shown exactly once,
at creation — fixed alongside the new UI.

Task breakdown (status as of this writing):

| Task | Scope | Status |
| --- | --- | --- |
| PF-S01.01 | Invitation token generation, canonical-email matching, and expiry validation (pure logic, no persistence) | ✅ landed this pass |
| PF-S01.02 | `Invitation` entity + `IdentityStore` mapping | ✅ build- and test-verified |
| PF-S01.03 | `InvitationService` (create / list-pending / accept) + `GET`/`POST /api/organizations/current/invitations`, `POST /api/invitations/{token}/accept`, `Identity.ManageMembers` capability (granted wherever `Capabilities.All` is — see PF-S01.04) | ✅ build- and test-verified |
| PF-S01.04 | Configurable role → capability matrix: additive per-organization grants/revocations layered on `Capabilities.ForRole` via `RoleCapabilityMatrix.Effective` (pure logic, tested), a new `RoleCapabilityOverride` entity/table, `RoleCapabilityService` (wired into `SessionAuthentication` so login and cookie-refresh both use the effective set), and `GET /api/organizations/current/roles` / `PUT /api/organizations/current/roles/{role}/capabilities` | ✅ build- and test-verified. `Roles.All` (the role catalog the endpoint/UI needs) is hand-kept in sync with `Capabilities.ForRole`'s switch arms; no code shares them yet |
| PF-S01.05 | Teams: `Team` + `TeamMembership` entities/mapping, `TeamService` (create, list, add/remove member — requires an existing active `OrganizationMembership`), `GET`/`POST /api/organizations/current/teams`, `GET`/`POST`/`DELETE .../teams/{id}/members/{userId}` (`Identity.ManageMembers`) | ✅ build- and test-verified. Grouping/addressing only — scoping work or property access by team is explicitly deferred, not implied by this pass |
| PF-S01.06 | Session/device management: `UserSession` entity/mapping, `session_id` claim issued at login and carried in the cookie, `UserSessionService` (start/touch/list/revoke), `SessionAuthentication.ValidateCookieAsync` rejects a cookie whose session was revoked (additive to the existing `SecurityStamp` check — a cookie predating this feature has no `session_id` claim and is unaffected), `GET`/`DELETE /api/sessions` for self-service list/revoke of the caller's own devices | ✅ build- and test-verified. Deliberately does not touch `LogoutAsync`'s existing all-sessions revoke (bumping `SecurityStamp`); the two mechanisms are independent and both work |
| PF-S01.07 | Audit history beyond the Work timeline: `IdentityAuditEntry` (flat, append-only, mirroring `TimelineEntry`'s "no event-specific columns" shape) + `IdentityAuditLog` (record/list), wired into invitation create/accept and role-capability overrides; new `MembershipManagementService` (change role / remove member — the only two membership writes that previously had no endpoint at all, needed so there was something to audit) with the same manager-lockout guard as PF-S01.04, `GET`/`PUT .../members/{userId}/role`/`DELETE .../members/{userId}`, and `GET /api/organizations/current/audit` | ✅ build- and test-verified |
| PF-S01.08 | Negative-authorization + tenant-isolation integration test suite for the above: `IdentityAdministrationTests` (12 tests covering read-only lockout, expired/already-accepted/unknown invitation tokens, tenant confinement of invitations/audit/teams/memberships, last-manager-removal refusal, and self-service session revoke) | ✅ build- and test-verified |
| PF-S01.09 | Browser (Playwright) coverage for invite → accept → login: a minimal `/settings/members` admin page (invite form, one-time invite-link banner, pending list, active members with inline role change/remove) and a public `/accept-invite/[token]` page, plus `member-invite.spec.ts` driving the full flow in a real browser (send invite as admin → accept in a fresh unauthenticated context → sign in as the new user → confirm the admin's list reflects the move from pending to active) | ✅ build- and test-verified |

**FS-S01 is now fully complete** per this task breakdown.

**FS-S03 — Configuration and workflow administration (#191).** Custom fields, statuses,
categories (categories already exist per-M3/M5; this generalizes past work categories),
templates, numbering, approvals, business hours, time zones, notification settings.

**Two deliberate scope decisions before the task list, because both raw feature words in the
story are bigger than they look:**

- **"Statuses" does not mean configurable/custom work statuses.** `WorkStatus` is a fixed
  domain enum threaded through invariants (`RefuseWhenTerminal`, `ChangeStatus`'s transition
  rules, `Reopen`) and the automation engine's `WorkStatusEquals` condition. Turning it into a
  per-organization open set is a design change on the scale of PF-5.04 (the automation engine
  itself), not a sub-task of this story. Deferred; revisit as its own story once a second
  workflow (leasing, maintenance-ops) needs a genuinely different status set — the same
  "finalize the vocabulary before building" call this repo already made for automation
  triggers/conditions/actions.
- **Custom fields stay a closed set of field *types*, not an open schema.** A per-organization
  definition (name, type, and — for the closed set — a controlled option list) attached to one
  entity type at a time, validated against its type at write time. Not a JSON-schema engine,
  not user-authored validation expressions; the same reasoning that kept `AutomationCondition`/
  `AutomationAction` a closed vocabulary instead of a scripting surface.
- **"Templates" here means the numbering/notification templates below, not a new document/
  message-template system.** `MessageTemplate` (PF-4.03) already covers resident communication
  templates; generalizing templates to documents is FS-S17 (Gate 4), not this story.

| Task | Scope | Status |
| --- | --- | --- |
| PF-S03.01 | Custom field definitions: `CustomFieldDefinition` (org-scoped, `AppliesTo` entity-type enum starting with `WorkItem`, `FieldType` — Text/Number/Date/Boolean/SingleSelect — plus a bounded option list for `SingleSelect`), forced RLS, `GET`/`POST`/`PUT`/`POST .../archive /api/settings/custom-fields` behind a new `Settings.ManageConfiguration` capability. Pure validation logic (key format, type/option-list well-formedness, `Accepts()` for the PF-S03.02 write-time check) is unit-tested independent of persistence, mirroring `AutomationCondition.Validate()` | ✅ build- and test-verified — `CustomFieldDefinitionTests` (24 unit), `CustomFieldTests` (5 integration: CRUD, SingleSelect round-trip, 400/409 on a bad/duplicate key, read-only-denied, tenant isolation via a second login) plus a raw-SQL cross-tenant test in `IsolationTests` |
| PF-S03.02 | Custom field values on `WorkItem`: `CustomFieldValue` (org, `WorkId`, `CustomFieldDefinitionId`, a single validated-string `Value`), enforced against the live definition at write time via `CustomFieldDefinition.Accepts()`, exposed as `customFields` (keyed by definition `key`) on `POST`/`PUT /api/work` and the work detail response. A blank/absent value clears the field rather than storing an empty row; `IsRequired` is enforced only on a write that names the key, not retroactively across every work item — deliberately, so marking a field required doesn't break existing work that predates it | ✅ build- and test-verified — `CustomFieldValueTests` (6 unit + 5 integration: create/detail round-trip, set-then-clear on update, 400 on unknown/archived/invalid, atomicity — a rejected custom field leaves no partial work item) plus a raw-SQL cross-tenant test in `IsolationTests`. Deliberately out of scope: the work **list** endpoint does not surface custom fields, only create/update/detail |
| PF-S03.03 | Generalize categories: extend `WorkCategory`/`/api/categories` with an `AppliesTo` entity-type tag (defaulting existing rows to `WorkItem`) so a category list can be shared by a future entity type without a second CRUD surface; existing `Settings.ManageCategories` callers and the `/settings/categories` UI keep working unchanged | not started |
| PF-S03.04 | Numbering: `NumberingSequence` (org, entity type, prefix, zero-padded width, next value), an atomic allocator (row-locked increment inside the triggering transaction — no gaps-tolerant `SELECT MAX`), applied to `WorkItem` as an optional `DisplayNumber` set at `Publish()`; existing work keeps `DisplayNumber = null` (backfill is a decision for whoever turns numbering on, not implied here). `GET`/`PUT /api/settings/numbering` behind `Settings.ManageConfiguration` | not started |
| PF-S03.05 | Approvals: a single-step `ApprovalRequest` (subject type + id, requested by, status Pending/Approved/Rejected, decided by/at, reason), `ApprovalService` (request/decide), audited via `IdentityAuditLog` (PF-S01.07). One decision, not a chain — a multi-step approval workflow is a follow-on story if the demand appears. `GET`/`POST /api/approvals`, `POST /api/approvals/{id}/decide` behind `Settings.ManageConfiguration` for the decide step; requesting only needs the capability the subject action itself needs | not started |
| PF-S03.06 | Business hours + org default time zone: `OrganizationSettings` (day-of-week open/close windows, nullable = closed that day, default `TimeZoneId` for properties that don't set their own — `Property.TimeZoneId`, PF-7.04, still wins when set). Store + `GET`/`PUT /api/settings/organization` behind `Settings.ManageConfiguration`. No consumer wired yet (no "closed for business" check anywhere) — storage and validation only, same as templates existed before dispatch did (PF-4.03 vs PF-4.05) | not started |
| PF-S03.07 | Staff notification preferences: `NotificationPreference` (per-user, per-event-type opt in/out — starting with the event types that already exist: work assigned to me, automation applied, invitation received). **No delivery channel is wired to this yet** — today only residents have a messaging pipeline (`IResidentMessenger`); a staff-facing channel is out of scope here. This task is the preference model and its API only, explicitly not a promise of delivery | not started |
| PF-S03.08 | Settings admin UI: one `/settings/configuration` page (or additions to existing `/settings/*` pages) covering custom fields, numbering, business hours, and notification preferences CRUD, gated on `Settings.ManageConfiguration` | not started |
| PF-S03.09 | Negative-authorization + tenant-isolation integration tests for PF-S03.01–.07 (mirroring `IdentityAdministrationTests`), plus one e2e spec covering a custom field defined, set on a work item, and round-tripped through the API | not started |

### FS-G1B — Core PMS workflows

Landed directly to `develop`/`main` (PR #209, plus follow-ons in #214) while this file still
said "not yet broken into tasks" — reconcile this section properly (task-by-task, the way
FS-S01's is) the next time FS-G1B work resumes. For now, what shipped: FS-S02 Portfolio and
property management (#190, done — includes `PropertyAmenity` from the #214 follow-on), FS-S04
Marketing, listings, availability, and showings (#192, done), FS-S06 Leases, renewals, notices,
and resident lifecycle (#194, done — includes the `ResidentPayment`↔`LeaseCharge` link from the
#214 follow-on), FS-S07 Resident portal and service requests (#195, done), tracked under a new
epic **FS-E2: Leasing and resident lifecycle (#185, done)** that this document had not caught
up to. FS-S05 (Applications and screening, #193) is the one FS-E2/Gate-4-adjacent story still
open and unbroken.

## Gates 2–5

### FS-G3A — Operations platform (#187 epic, #201 FS-S13, #203 FS-S15)

**FS-S13 — Preventive maintenance and asset lifecycle (#201).** The platform slice is split
into service-plan definition, recurrence calculation, generated work, meter readings, lifecycle
costs, replacement planning, warranty/compliance dates, tenant security, and role/browser tests.
The recurrence engine must be deterministic and idempotent: a plan can be evaluated repeatedly
for the same property/asset/date without duplicate generated work. Generated work retains the
plan occurrence key so retries are safe. Date-only rules are evaluated in the property's IANA
zone and persisted work schedules remain UTC instants.

| Task | Scope | Status |
| --- | --- | --- |
| PF-S13.01 | Service-plan and recurrence domain model; deterministic occurrence keys and next-due calculation | 🔄 in progress |
| PF-S13.02 | Tenant-scoped persistence, RLS/grants, asset/plan relationship, and idempotent generation transaction | ⏳ queued |
| PF-S13.03 | Meter readings, lifecycle-cost rollups, replacement candidates, warranty/compliance alerts | ⏳ queued |
| PF-S13.04 | Manager/supervisor API and role-negative integration tests | ⏳ queued |
| PF-S13.05 | Preventive-maintenance calendar/detail UI and browser coverage | ⏳ queued |

**FS-S15 — Compliance, incidents, and risk (#203).** The platform slice is split into recurring
obligations, licenses/inspections/safety checks, violation and incident records, remediation,
evidence and retention, escalation, tenant security, and dashboard/browser tests. Evidence access
must use the existing attachment permission/retention controls; incident history is append-only
and every status transition is auditable.

| Task | Scope | Status |
| --- | --- | --- |
| PF-S15.01 | Obligation and incident domain model; due/overdue/escalation rules and audit transitions | 🔄 in progress |
| PF-S15.02 | Tenant-scoped persistence, RLS/grants, evidence links, and retention enforcement | ⏳ queued |
| PF-S15.03 | Remediation/violation workflows and recurring-obligation generation | ⏳ queued |
| PF-S15.04 | Compliance API, risk dashboard queries, and role-negative integration tests | ⏳ queued |
| PF-S15.05 | Compliance dashboard/detail UI and browser coverage | ⏳ queued |

The two stories remain In progress until all tasks above are build-, integration-, and
browser-verified. This breakdown is the source of truth for follow-on task issues.
