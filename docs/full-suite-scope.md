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
| 1 — Core PMS | FS-E1: Core PMS foundation and administration (#184) | `FS-G1A` — Platform foundation | `FS-G1B` — Core PMS workflows |
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

Task breakdown (status as of this writing):

| Task | Scope | Status |
| --- | --- | --- |
| PF-S01.01 | Invitation token generation, canonical-email matching, and expiry validation (pure logic, no persistence) | ✅ landed this pass |
| PF-S01.02 | `Invitation` entity + `IdentityStore` mapping | Entity/mapping written. **The EF migration itself is not generated** — run `dotnet ef migrations add AddInvitations --project src/PropFlow.Infrastructure --context IdentityStore` (see local-development.md) before merging; this session has no .NET SDK to run it or verify the model builds |
| PF-S01.03 | `InvitationService` (create / list-pending / accept) + `GET`/`POST /api/organizations/current/invitations`, `POST /api/invitations/{token}/accept`, `Identity.ManageMembers` capability (granted wherever `Capabilities.All` is — see PF-S01.04) | Written, wired into `Program.cs`. **Not build-verified** — same SDK caveat as PF-S01.02. No integration test yet (needs a real Postgres + the migration above) |
| PF-S01.04 | Configurable role → capability matrix: additive per-organization grants/revocations layered on `Capabilities.ForRole` via `RoleCapabilityMatrix.Effective` (pure logic, tested), a new `RoleCapabilityOverride` entity/table, `RoleCapabilityService` (wired into `SessionAuthentication` so login and cookie-refresh both use the effective set), and `GET /api/organizations/current/roles` / `PUT /api/organizations/current/roles/{role}/capabilities` | Written; matrix logic unit-tested. **Not build-verified** — same SDK caveat as PF-S01.02/03, and needs the same EF migration pass (one migration covering `Invitation` + `RoleCapabilityOverride` is fine — see local-development.md). `Roles.All` (the role catalog the endpoint/UI needs) is hand-kept in sync with `Capabilities.ForRole`'s switch arms; no code shares them yet |
| PF-S01.05 | Teams (group memberships under a named team, scope work/property bindings to a team) | Not started |
| PF-S01.06 | Session/device management admin view (list + revoke active sessions) | Not started |
| PF-S01.07 | Audit history beyond the Work timeline (identity/membership changes: invites sent, roles changed, members removed) | Not started |
| PF-S01.08 | Negative-authorization + tenant-isolation integration test suite for the above | Not started |
| PF-S01.09 | Browser (Playwright) coverage for invite → accept → login | Not started |

**FS-S03 — Configuration and workflow administration (#191).** Custom fields, statuses,
categories (categories already exist per-M3/M5; this generalizes past work categories),
templates, numbering, approvals, business hours, time zones, notification settings. Not yet
broken into tasks — do that before starting PF-S03.01.

### FS-G1B — Core PMS workflows

Not yet broken into tasks.

## Gates 2–5

Not yet broken into tasks. Break each gate's stories down here, epic by epic, before starting
code — the same discipline `docs/backlog.md` used for M3–M7 — so the task list in this file
stays the source GitHub issues are cut from, not the other way around.
