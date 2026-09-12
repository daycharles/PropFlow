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
| PF-S03.03 | Generalize categories: extend `WorkCategory`/`/api/categories` with an `AppliesTo` entity-type tag (defaulting existing rows to `WorkItem`) so a category list can be shared by a future entity type without a second CRUD surface; existing `Settings.ManageCategories` callers and the `/settings/categories` UI keep working unchanged | ✅ build- and test-verified. `CustomFieldDefinition`'s `AppliesTo` enum was renamed `ConfigurationEntityType` and moved to its own file so PF-S03.01 and this task share one vocabulary instead of each keeping its own (stored as the same string values, so no data migration for the rename itself — only for the new `WorkCategories.AppliesTo` column, which backfills existing rows to `WorkItem`). Unique index moved to `(OrganizationId, AppliesTo, Name)`; `GET /api/categories` gained an optional `?appliesTo=` filter defaulting to `WorkItem`. `WorkCategoryTests` (3 unit) + 2 new `CategoryEndpointTests` (default-value round-trip, duplicate-name-per-type 409) |
| PF-S03.04 | Numbering: `NumberingSequence` (org, entity type, prefix, zero-padded width, next value), an atomic allocator applied to `WorkItem` at creation; existing work keeps `DisplayNumber = null` (no backfill implied) | ✅ build- and test-verified. The allocator is a single `UPDATE … RETURNING` statement (`EfWorkOperations.AllocateDisplayNumberAsync`) run inside an explicit transaction shared with the work-item insert — never a read-`NextValue`-into-memory-then-write, which would let two concurrent creates allocate the same number. `WorkItems.DisplayNumber` also carries its own unique-when-set index as a second backstop. `GET`/`PUT /api/settings/numbering/{appliesTo}` behind `Settings.ManageConfiguration`; `PUT` upserts a scheme's `prefix`/`width` but never `nextValue` (no way to rewind the counter through the API). Tests: 11 unit (`NumberingSequenceTests`, `WorkItemDisplayNumberTests`), 8 integration (`NumberingTests`: configure → assign → sequential numbers → reconfigure preserves `nextValue`, read-only denied, invalid width 400, **and a 10-way concurrent-creation test asserting 10 distinct numbers**, run 3× to rule out a lucky pass) + a raw-SQL tenant-isolation test |
| PF-S03.05 | Approvals: a single-step `ApprovalRequest` (subject type + id, requested by, status Pending/Approved/Rejected, decided by/at, reason). One decision, not a chain — a multi-step approval workflow is a follow-on story if the demand appears. `GET`/`POST /api/approvals`, `POST /api/approvals/{id}/decide` behind `Settings.ManageConfiguration` for the decide step; requesting only needs `Work.Read` | ✅ build- and test-verified. Two deviations from the original plan, both deliberate: (1) audited via `TimelineEntry` (`ApprovalRequested`/`ApprovalDecided`), not `IdentityAuditLog` — `ApprovalRequest` is operations-schema business data in the same `OperationsStore` context as everything else it might gate, and `IdentityAuditLog` lives in the separate `IdentityStore` context, which would have made the audit write non-atomic with the request/decision itself; (2) `SubjectType` is a validated free-text string, not a closed enum like `ConfigurationEntityType` — the set of future subjects an approval might gate isn't enumerable today, the same reasoning `TimelineEntry.RelatedObjectType` already uses. Does **not** replace `Budget`'s own `Submit`/`Approve`/`Reject` state machine (FS-S10) or any other feature's bespoke approval flow — this exists for the next feature that has none. `Decide` refuses a self-decision (the requester can never be their own approver) and refuses deciding twice. Tests: 9 unit (`ApprovalRequestTests`), 8 integration (`ApprovalTests`: request/list/filter, approve, reject, decide-twice 409, free-text subject type, read-only can request+read but not decide, tenant scoping) + a raw-SQL isolation test |
| PF-S03.06 | Business hours + org default time zone: `OrganizationSettings` (day-of-week open/close windows, nullable = closed that day, default `TimeZoneId` for properties that don't set their own — `Property.TimeZoneId`, PF-7.04, still wins when set). Store + `GET`/`PUT /api/settings/organization` behind `Settings.ManageConfiguration`. No consumer wired yet (no "closed for business" check anywhere) — storage and validation only, same as templates existed before dispatch did (PF-4.03 vs PF-4.05) | ✅ build- and test-verified. One row per organization (a unique index on `OrganizationId`, not a natural key — `Id` is still a real, generated GUID, same shape as every other `TenantEntity`); `GET` never 404s, reporting `defaultTimeZoneId: null` and all seven days closed before anything is saved, so a settings page can render before the first save. `PUT` is a full-week upsert — all seven `DayOfWeek` entries required every time, a day missing or duplicated is a 400, and an open time without a matching close (or open ≥ close) is a 400 — a partial update is rejected outright rather than defaulting the missing day to closed, so a client bug can't silently erase a day nobody meant to touch. `BusinessHours` stored `jsonb` (an array of `{ day, open, close }`), same shape as `CustomFieldDefinition.Options`/`AutomationRule.Conditions`. Tests: 8 unit (`OrganizationSettingsTests`), 7 integration (`OrganizationSettingsTests`: default-before-save, round-trip, second `PUT` updates the same row not a second one, invalid zone 400, missing-day 400, read-only can read but not save, tenant scoping) + a raw-SQL isolation test |
| PF-S03.07 | Staff notification preferences: `NotificationPreference` (per-user, per-event-type opt in/out — starting with the event types that already exist: work assigned to me, automation applied, invitation received). **No delivery channel is wired to this yet** — today only residents have a messaging pipeline (`IResidentMessenger`); a staff-facing channel is out of scope here. This task is the preference model and its API only, explicitly not a promise of delivery | ✅ build- and test-verified. `NotificationEventType` is a closed vocabulary of three values (`WorkAssigned`, `AutomationApplied`, `InvitationReceived`) naming events that already fire elsewhere in the codebase. Unlike every other PF-S03 configuration task, this one is personal, not org-wide: `GET`/`PUT /api/settings/notification-preferences[/{eventType}]` are gated on plain `Work.Read` (the same as `/api/saved-views`), not `Settings.ManageConfiguration` — any staff member, including Read Only, manages their own preferences, always scoped to their own user id from the session. Opt-out by default: a missing row reads `enabled: true`, so a brand-new user is fully subscribed rather than fully silent. Tests: 6 unit (`NotificationPreferenceTests`), 6 integration (`NotificationPreferenceTests`: no-rows-means-subscribed, disabling one type leaves the others alone, a second `PUT` updates the same row, unknown event type 400, scoped to the caller not the org, tenant scoping) + a raw-SQL isolation test |
| PF-S03.08 | Settings admin UI: one `/settings/configuration` page (or additions to existing `/settings/*` pages) covering custom fields, numbering, business hours, and notification preferences CRUD, gated on `Settings.ManageConfiguration` | ✅ landed — `apps/web/app/settings/configuration/page.tsx` plus the `Configuration` entry in `navigation.ts` (commit `68149a0`, PR #234; nav e2e spec updated in `1f4862c`) |
| PF-S03.09 | Negative-authorization + tenant-isolation integration tests for PF-S03.01–.07 (mirroring `IdentityAdministrationTests`), plus one e2e spec covering a custom field defined, set on a work item, and round-tripped through the API | ✅ build- and test-verified. `ConfigurationAdministrationTests.cs` is the consolidated sweep: one comprehensive Read-Only-is-forbidden test across every PF-S03.01–.06 write endpoint (with the two deliberate exceptions called out inline — requesting an approval needs only `Work.Read`, and notification preferences are personal so a Read Only user manages their own), plus the specific gaps individual feature files didn't cover: categories confined cross-tenant over HTTP (not just raw SQL), numbering sequences proven independent per organization (no cross-org collision or inherited state), and custom-field-value validation proven not to leak a definition across organizations (org B referencing org A's field key is a 400, not an accidental accept). `e2e/custom-field-round-trip.spec.ts` defines a field through the real Settings UI (PF-S03.08), sets it on a work item and reads it back through the API (no UI exists yet for setting a value — deliberately, per PF-S03.02). Both verified against the real local stack, not just CI. **This closes out FS-S03** — `.01`–`.09` are all done. |

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
open; it is milestoned into `FS-G4A` and broken down under [Gate 4 — Ecosystem](#gate-4--ecosystem)
below.
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
| PF-S13.01 | Service-plan and recurrence domain model; deterministic occurrence keys and next-due calculation | ✅ landed — `PreventiveMaintenancePlan`/`PreventiveMaintenanceOccurrence`, `PreventiveMaintenanceLifecycleTests` |
| PF-S13.02 | Tenant-scoped persistence, RLS/grants, asset/plan relationship, and idempotent generation transaction | ✅ landed — `EfPreventiveMaintenanceStore`, grants at `DatabaseProvisioner.cs:108-109` |
| PF-S13.03 | Meter readings, lifecycle-cost rollups, replacement candidates, warranty/compliance alerts | ✅ landed — `MeterReading`, `AssetLifecycleCost`, `AssetLifecycleService` |
| PF-S13.04 | Manager/supervisor API and role-negative integration tests | ✅ landed — `AssetEndpoints`, `AssetIntegrationTests` |
| PF-S13.05 | Preventive-maintenance calendar/detail UI and browser coverage | ⚠️ **not landed** — there is no preventive-maintenance page under `apps/web/app` and no PM e2e spec. #201 was closed 2026-09-11 with `.05` outstanding; re-open as its own task if the calendar is wanted |

**FS-S15 — Compliance, incidents, and risk (#203).** The platform slice is split into recurring
obligations, licenses/inspections/safety checks, violation and incident records, remediation,
evidence and retention, escalation, tenant security, and dashboard/browser tests. Evidence access
must use the existing attachment permission/retention controls; incident history is append-only
and every status transition is auditable.

| Task | Scope | Status |
| --- | --- | --- |
| PF-S15.01 | Obligation and incident domain model; due/overdue/escalation rules and audit transitions | ✅ landed — `ComplianceObligation`, `Incident`, `IncidentAuditEntry`, `ComplianceRiskTests` |
| PF-S15.02 | Tenant-scoped persistence, RLS/grants, evidence links, and retention enforcement | ✅ landed — grants at `DatabaseProvisioner.cs:110-112`, `EvidenceRetentionPolicy` |
| PF-S15.03 | Remediation/violation workflows and recurring-obligation generation | ✅ landed — `Violation`, `Remediation`, `ComplianceRecurrenceGenerator` |
| PF-S15.04 | Compliance API, risk dashboard queries, and role-negative integration tests | ✅ landed — `ComplianceEndpoints`, `ComplianceAuthorizationTests` |
| PF-S15.05 | Compliance dashboard/detail UI and browser coverage | ⚠️ **not landed** — there is no compliance page under `apps/web/app` and no compliance e2e spec. #203 was closed 2026-09-11 with `.05` outstanding; re-open as its own task if the dashboard is wanted |

**#201 and #203 are both closed on GitHub (2026-09-11) but each has an outstanding `.05` UI row.**
Verified 2026-09-12 against the tree: `apps/web/app` has no preventive-maintenance or compliance
route and `apps/web/e2e` has no spec for either. The tracker is ahead of the code on exactly those
two rows; nothing else in the two breakdowns is overstated.

## Gate 4 — Ecosystem

### FS-G4A — Ecosystem platform (#188 epic, #193 FS-S05, #207 FS-S19)

`FS-G4A` is a **milestone, not an issue**. Its sibling `FS-G4B — Engagement and insight`
(milestone 15, #204–#206) is a separate track and is not covered here.

**FS-E5 (#188) is a milestone rollup, not a buildable story.** Its body spans engagement,
reporting, e-signature, subscription administration and release hardening — work that lives in
`FS-G4B` (#204–#206) and Gate 5 (#208). The gate→epic map at the top of this file places FS-E5
against **Gate 5** while GitHub milestones it into `FS-G4A`; the map is the correct reading, and
#188 is closed as a rollup once FS-S05 and FS-S19 land rather than being broken into tasks of its
own.

**FS-S05 — Applications and screening (#193).** Rental application intake, applicant consent,
third-party screening, and an auditable approve/deny decision trail.

Two scope decisions taken before the task list:

- **`RentalApplication` is a new aggregate; `Applicant` stays the *person*.** `Applicant` carries
  a unique-per-inquiry index (`OperationsStore.cs`), so extending it in place would permanently
  block joint applications and re-applications by the same person.
- **PropFlow stores the verdict, not the raw file.** Name, email, phone, monthly income,
  employment status, the screening recommendation, a score and a bounded summary are persisted.
  SSN, date of birth, driver's licence, the raw credit report and bank details are **not** — the
  applicant supplies those to the provider through a provider-hosted flow and PropFlow keeps the
  idempotency key and the verdict. Same posture as payments (`IPaymentGateway`) and
  `IntegrationConnection`. Storing more would force encryption-at-rest, a retention sweep and a
  deletion API that this story does not fund.

Two capabilities, not one: `Applications.Manage` (intake, consent, screening, approve/deny) and
`Applications.ReadPii` (unmasked contact, income, screening detail). With a single capability the
story's own "PII access tests" would be vacuous — asserting a capability against itself.

| Task | Scope | Status |
| --- | --- | --- |
| PF-S05.01 | `RentalApplication` aggregate (Draft → Submitted → ConsentGranted → Screening → UnderReview → Approved/Denied, plus Withdrawn), `ApplicationConsent`, `ScreeningRequest` retry state machine, `ApplicationDecision`; all pure domain, unit-tested without a database. `BeginScreening` refuses anything but `ConsentGranted` — the consent gate is a domain invariant so an endpoint cannot forget it | ⏳ queued |
| PF-S05.02 | Persistence: six tables in `operations`, forced RLS in the migration, grants in `DatabaseProvisioner`, readiness-floor bump in `DatabaseHealth`. `ApplicationConsents`, `ScreeningResults` and `ApplicationDecisions` are append-only at all three rungs (EF `GuardWrites`, `GRANT SELECT, INSERT` only, and a PostgreSQL trigger) because a denial record is adverse-action evidence | ⏳ queued |
| PF-S05.03 | `IScreeningProvider` port on the `IPaymentGateway` template plus a `ConfiguredScreeningProvider` stand-in. Narrow and request/response by design — it is deliberately **not** an `IIntegrationAdapter`, whose `PullAsync` takes no subject and returns a whole-source snapshot | ⏳ queued |
| PF-S05.04 | `Applications.Manage` / `Applications.ReadPii` capabilities. Admin/PM inherit both through the existing `All.Where(...)` arm; Regional Manager gets `Manage` only, spread at the call site rather than by mutating the shared `CategoryManagement` array; `Read Only` stays exactly `[ReadWork]` | ⏳ queued |
| PF-S05.05 | Intake, consent and withdrawal endpoints in a new `ApplicationEndpoints.cs`; list/detail responses are a masked projection where the PII property is **absent from the JSON**, with `GET /api/applications/{id}/pii` behind `ReadApplicantPii` carrying the full record. `docs/api.md` updated in the same change | ⏳ queued |
| PF-S05.06 | Screening request, retry and provider-outage handling. An outage is a **503 with nothing written** (the `BillingEndpoints` idiom); retry exhaustion moves the request to `Abandoned` | ⏳ queued |
| PF-S05.07 | Approve/deny plus decision history. A `Fail` screening recommendation does **not** hard-block approval — that would make individualized assessment impossible — but approving over a `Fail` requires a non-null override note on the decision, so the override is never silent. The legacy `PUT /api/marketing/.../applicants/{id}/status` route returns 409 once a `RentalApplication` exists for the applicant | ⏳ queued |
| PF-S05.08 | Integration test pass: consent gate, PII masking by capability, decision trail immutability, tenant isolation. Builds its own listing → inquiry → applicant through the existing HTTP routes rather than adding seed ids to `DatabaseFixture` | ⏳ queued |
| PF-S05.09 | Rewire the existing Applicants panel on `/marketing/listings` onto the application flow, plus Playwright coverage. **This is a correctness fix, not polish**: the shipped "Send to screening" / "Approve" buttons drive `applicantStatus`, which after FS-S05 would reach Approved with no consent check and no decision row | ⏳ queued |

**FS-S19 — Real integrations and reconciliation (#207).** Closes the gap recorded at
`docs/followups.md`: a sync upserts one `ExternalRecordLink` per external record but never creates
or updates the corresponding `Property`/`Space`/`Occupancy`/`WorkItem`/`Asset`, and
`SyncReport.Failed` is hard-coded 0.

**This story delivers the machinery, not nine live provider integrations.** Every acceptance
criterion on #207 names machinery — sandbox adapter *contract*, replay-safe *sync*, conflict
*queue*, mapping *validation*, provider-outage *recovery*, *tenant isolation*. None names a
vendor. Structurally: all five canonical shapes in `CanonicalRecords.cs` are PMS shapes, so an
accounting or banking adapter has nothing to reconcile *into*; and nine vendor SDKs means nine
`packages.lock.json` regenerations under `--locked-mode`. FS-S19 ships the contract, **two**
adapters (the existing deterministic mock plus one sandbox adapter speaking a PropFlow-defined
JSON-over-HTTP contract on the already-registered `IHttpClientFactory`), and the whole
reconciliation engine. The nine categories become a closed `IntegrationCategory` enum plus one
follow-on story each.

**A correction to `milestones.md`, which claims PF-7.02 delivered "managed secrets".** Against the
code it did not: what exists is refuse-to-boot configuration gating (`DeploymentConfiguration`),
global provider credentials as plain config properties (`CommunicationsOptions`), and a
deployment-time generator that disclaims itself (`DeploymentSecrets.cs` — *"Nothing here is a
production secret-management story."*). There is no `ISecretStore` in `src/`. FS-S19 introduces
`IIntegrationSecretStore` with a configuration-backed implementation keyed by connection id.
**No credential column on `integrations."Connections"`** — a UI where an admin types a credential
needs envelope encryption and key rotation, which is a later story.

| Task | Scope | Status |
| --- | --- | --- |
| PF-S19.01 | Adapter contract v2: closed `IntegrationCategory` taxonomy, a capability descriptor per adapter, `IIntegrationSecretStore`. Also fixes `IntegrationCatalog`'s `ToDictionary` with no duplicate handling — latent with one adapter, reachable the moment a second registers | ⏳ queued |
| PF-S19.02 | `MappingProfile` + `MappingRule` domain and pure validation. The profile supplies the facts the canonical records cannot: `TargetPortfolioId` (`Property`'s constructor requires a portfolio; `CanonicalProperty` has no portfolio field), `DefaultCreatorId` (`WorkItem` requires a creator; an external work order has no PropFlow user) and `DefaultTimeZoneId` (`Property.ValidateTimeZone` rejects anything without a `/`). `Mode` defaults to **`ReportOnly`**, never `AutoApply` — a reconciler that silently rewrites a tenant's property names on first connect is a support incident. Rules are a closed structure, not a JSON column and not an expression language, the same call already made for `AutomationCondition` | ⏳ queued |
| PF-S19.03 | `SyncRun` + `Conflict` domain and the `ExternalRecordLink` reconciliation fields (`InternalId`, `ReconciledHash`, `LastRunId`, `LastReconciledAt`, `Reconcile()`/`Retire()`). Ends with a **mandatory model-freeze review**: every column any later task needs must be in the PF-S19.04 migration | ⏳ queued |
| PF-S19.04 | Persistence, forced RLS, grants, readiness floor — **the serialization point for this track**. `Conflicts` carries the index the story turns on: `UNIQUE (OrganizationId, ConnectionId, Kind, ExternalId, Reason, Field) WHERE "Status" = 'Open'`, without which "conflict queue" and "replay-safe sync" contradict each other. Conflicts get `SELECT, INSERT, UPDATE` and **no `DELETE`** — a resolved conflict is an audit fact. No new `operations` grants are needed; every target table is already `SELECT, INSERT, UPDATE` | ⏳ queued |
| PF-S19.05 | The reconciler and the stale-record retirement sweep. Writes are ordered domain-first, hash-last: `IntegrationStore` and `OperationsStore` are separate contexts on separate connections and this codebase has no distributed transaction anywhere, so a crash between them leaves `ReconciledHash` stale, the next run re-decides `Update`, re-applies the same mapped values to the same `InternalId`, and converges | ⏳ queued |
| PF-S19.06 | Sync runs, retry, outage recovery and the scheduler. The background dispatcher registers under `if (!builder.Environment.IsEnvironment("Testing"))`, verbatim to the outbox poller, and tests drive the relay directly | ⏳ queued |
| PF-S19.07 | Sandbox HTTP adapter (a PropFlow-defined JSON-over-HTTP contract on `IHttpClientFactory`) and the configuration-backed secret store | ⏳ queued |
| PF-S19.08 | API: conflicts, mappings, runs, retire, plus `docs/api.md`. `Integrations.Manage` already exists, so `Capabilities.cs` is untouched | ⏳ queued |
| PF-S19.09 | Web: conflict queue, sync health and mapping panel, plus Playwright. `/integrations` is already in `PRIMARY_NAV` with a shipped page, so `navigation.ts` is untouched | ⏳ queued |
| PF-S19.10 | Acceptance suite and docs reconciliation. `IntegrationReplayTests` carries the three facts that prove replay safety, the first of which asserts **set equality on full row tuples, not count equality** — count equality would pass if a row were deleted and re-created with a new id, which is exactly the failure the test exists to catch | ⏳ queued |

Sub-tasks `PF-S05.NN` / `PF-S19.NN` deliberately get **no GitHub issues** — only the FS-level story
has one. The id lives in the branch name, every commit subject and the PR title.

The two tracks run in parallel: their EF migrations land on different contexts (`OperationsStore`
vs `IntegrationStore`), with different snapshots and different history tables. They collide only on
the shared merge points listed in `.claude/rules/workflow.md`, where PF-S05.02 lands first and
PF-S19.04 rebases onto it.
