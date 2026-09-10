# PropFlow HTTP API

The contract for every endpoint that exists today: the milestone-3 slice (auth, session, work,
assignment, bulk vendor, reference data, saved views), the milestone-4 communications surfaces
(residents, templates, the other bulk work actions) and the milestone-6 API-only surfaces (assets,
global search, integrations). Section headings name the milestone each group came from.

Use HTTPS and retain cookies. API responses are JSON except successful 204s and the minimal readiness endpoint. API session/data responses use Cache-Control: no-store. Authentication failures return 401, authorization failures return 403; no HTML login redirects are used.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /health/live | 200 process liveness JSON |
| GET | /health/ready | 200 when database security/schema checks pass, otherwise 503 |
| GET | /api/auth/csrf | Public; returns `{ "token": "..." }` and secure antiforgery cookie |
| POST | /api/auth/login | CSRF required; organizationSlug/email/password; 204 success, 400 invalid input/CSRF, 401 rejected credentials or membership, 429 rate limit |
| POST | /api/auth/logout | Auth + CSRF; 204; revokes all sessions for the current user |
| GET | /api/session | Auth; userId, organizationId, role and capabilities |
| GET | /api/work/ | Work.Read; filtered, sorted, paginated tenant-scoped work list (see below) |
| GET | /api/work/{id} | Work.Read; `{ "item": {...}, "version": <uint> }`, or 404 including foreign-tenant IDs |
| GET | /api/work/{id}/timeline | Work.Read; chronological audit entries, or 404 |
| POST | /api/work/ | Work.Create + Work.Read + CSRF; creates and publishes a work item; 201 with `{ item, version }` |
| PUT | /api/work/{id} | Work.Update + Work.Read + CSRF; full replace of the editable fields plus optional status/schedule; 200 `{ item, version }`, 400, 404, 409 |
| POST | /api/work/{id}/vendor | Work.AssignVendor + Work.Read + CSRF; accepts vendorId and optional version; atomically saves assignment and audit |
| POST | /api/work/{id}/employee | Work.AssignEmployee + Work.Read + CSRF; accepts employeeId and version; atomically saves assignment and audit |
| POST | /api/work/bulk/vendor | Work.AssignVendor + Work.Read + CSRF; one vendor across 1–100 work items, all-or-nothing |
| POST | /api/work/bulk/status | Work.Update + Work.Read + CSRF; one target status across 1–100 items, all-or-nothing |
| POST | /api/work/bulk/priority | Work.Update + Work.Read + CSRF; one priority across 1–100 items |
| POST | /api/work/bulk/schedule | Work.Update + Work.Read + CSRF; one schedule window across 1–100 items (each must be open and assigned) |
| POST | /api/work/bulk/note | Work.Update + Work.Read + CSRF; append one timeline note to 1–100 items |
| POST | /api/work/bulk/reopen | Work.Update + Work.Read + CSRF; reopen 1–100 completed/cancelled items |
| GET | /api/saved-views/ | Work.Read; the caller's own saved views, default first then by name |
| GET | /api/saved-views/{id} | Work.Read; one of the caller's saved views, or 404 |
| POST | /api/saved-views/ | Work.Read + CSRF; creates a saved view for the caller; 201, or 400 on invalid name/JSON |
| PUT | /api/saved-views/{id} | Work.Read + CSRF; replaces name/filters/columns/default; 200, 400, or 404 |
| DELETE | /api/saved-views/{id} | Work.Read + CSRF; 204 or 404 |
| GET | /api/vendors/ | Work.Read; tenant-scoped vendor lookup; optional `?q=` name filter; flat array capped at 500 |
| GET | /api/vendors/{id} | Work.Read; vendor lookup, or 404 |
| GET | /api/employees/ | Work.Read; tenant-scoped employee lookup; optional `?q=` name filter; capped at 500 |
| GET | /api/properties/ | Work.Read; tenant-scoped property lookup; optional `?q=` name filter; capped at 500 |
| GET | /api/properties/{id} | Work.Read; property with its buildings and spaces, or 404 |
| GET | /api/categories/ | Work.Read; tenant-scoped categories ordered by sort order/name |
| POST | /api/categories/ | Settings.ManageCategories + CSRF; creates a category |
| PUT | /api/categories/{id} | Settings.ManageCategories + CSRF; renames/reorders a category |
| POST | /api/categories/{id}/archive | Settings.ManageCategories + CSRF; archives a category |
| GET | /openapi/v1.json | Authenticated generated OpenAPI contract |

Login body:

```json
{
  "organizationSlug": "<organization slug printed during provisioning>",
  "email": "you@example.com",
  "password": "<your private password>"
}
```

Fetch a CSRF token before login, send it in X-CSRF-TOKEN, then fetch a fresh token after login. Anonymous tokens cannot authorize authenticated writes. Tokens are bound to the antiforgery cookie and current identity.

Enums serialize as their names everywhere in the API — a single `JsonStringEnumConverter` is
registered globally (`src/PropFlow.Api/Program.cs:68-69`) — so a work status is `"InProgress"`,
not `4`.

### Work list

`GET /api/work/` accepts these query-string parameters, all optional:

| Parameter | Meaning |
| --- | --- |
| `search` | case-insensitive substring over title and description |
| `categoryId`, `propertyId`, `spaceId` | exact GUID filters |
| `status` | one of `Draft`, `New`, `Assigned`, `Scheduled`, `InProgress`, `OnHold`, `Completed`, `Cancelled` |
| `priority` | one of `Low`, `Normal`, `High`, `Critical` |
| `sort` | `title` (default), `status`, `priority`, `dueDate` (`due` is an accepted alias), `created`; matching is case-insensitive and an unrecognized value falls back to `title` |
| `descending` | `true` reverses the sort; default `false` |
| `page` | 1-based; values below 1 are clamped to 1. Default 1 |
| `pageSize` | clamped to 1–100. Default 25 |

Every sort breaks ties on ID, so paging is stable. The response is a page of **flat** work
objects — not the entity graph — each carrying its own concurrency token:

```json
{
  "items": [
    {
      "id": "…", "title": "Pest inspection", "description": "Seeded demo work item",
      "status": "New", "priority": "Normal", "workType": "WorkOrder",
      "propertyId": "…", "propertyName": "Harbor View",
      "buildingId": "…", "spaceId": "…",
      "categoryId": "…", "categoryName": "Pest control",
      "vendorId": null, "vendorName": null, "employeeId": null,
      "dueDate": "2026-09-12T09:00:00+00:00", "createdAt": "2026-09-09T13:45:00+00:00",
      "version": 12345
    }
  ],
  "totalCount": 12, "page": 1, "pageSize": 25
}
```

`totalCount` is the count before paging. `GET /api/work/{id}` keeps its own shape,
`{ "item": <full work record>, "version": <uint> }`.

### Work create and update

`POST /api/work/` takes `title` and `propertyId` (both required) plus the optional
`description`, `workType`, `categoryId`, `priority`, `buildingId`, `spaceId`, `residentId`,
`dueDate`, `cost`, `internalNotes` and `residentVisibleNotes`. The item is created and published
in one step, so it comes back in `New` status with a `createdAt`, and a `WorkCreated` timeline
entry is written in the same transaction. An empty or unknown/foreign `propertyId` returns 400
and 404 respectively; a domain-invariant violation (title length, negative cost) returns 400.

`PUT /api/work/{id}` replaces the editable fields and requires the `version` read from a
previous response. It also accepts an optional `status` and `scheduledStart`/`scheduledEnd`;
a status change goes through the domain state machine, so moving out of `Completed`/`Cancelled`
or back to `Draft` is rejected with 400, and scheduling without a vendor or employee is
rejected with 400. Title, priority, status and schedule changes each append their own timeline
entry. A stale `version` returns 409 and requires a reload.

`GET /api/work/{id}/timeline` returns entries oldest first, all one shape:
`{ id, organizationId, workId, actorId, occurredAt, eventType, changes, relatedObjectType,
relatedObjectId, oldValue, newValue }`. `eventType` is the event name (`WorkCreated`,
`VendorAssigned`, `EmployeeAssigned`, `StatusChanged`, `PriorityChanged`, `Scheduled`,
`WorkNote`, `WorkReopened`, …); `oldValue` / `newValue` are the human-readable before/after
(a name, a status, an id); `changes` is a JSON *string* holding an object with the same pair.
There are no event-specific fields — in particular no `vendorId` / `previousVendorId`; a vendor
assignment reports the vendor through `newValue` and `changes` like every other event.

`actorId` is null for a system-generated entry, and `workId` is null for one that references the
work item only through `relatedObjectType: "WorkItem"` + `relatedObjectId`. The route returns
both kinds: an entry matches on `workId` **or** on that related-object reference.

### Assignment

Vendor assignment body — `version` is optional on the single-item route and omitting it skips
the concurrency check:

```json
{ "vendorId": "<vendor GUID>", "version": 12345 }
```

Employee assignment (`POST /api/work/{id}/employee`) takes the same shape with `employeeId`,
and requires the `Work.AssignEmployee` capability.

Success is `{ "changed": true }`; repeating the same assignment returns `{ "changed": false }` and creates no duplicate timeline entry. Unknown work/vendor/employee IDs, including other organizations' IDs, return 404. Invalid IDs return 400. A database concurrency conflict returns 409 and requires a reload. Assigning a vendor or employee to work in `New` moves it to `Assigned`; any other status is left alone. Tenant, actor and permissions come only from the session; extra JSON fields, query strings and tenant headers cannot override them.

**Terminal work takes no assignment, edit, status change or schedule.** Work in `Completed` or
`Cancelled` returns 400, and nothing is written, for the vendor route, the employee route, the
`PUT`, and every bulk route. The **only** way out of a terminal state is `POST
/api/work/bulk/reopen` (see below). Repeating an assignment that a work item already carries is
still `{ "changed": false }` rather than a 400, so replaying a completed action is not an error.

`POST /api/work/bulk/vendor` applies one vendor to a bounded batch:

```json
{ "vendorId": "<vendor GUID>", "items": [ { "workId": "…", "version": 12345 } ] }
```

`items` must hold 1 to 100 entries with distinct `workId`s; anything else is 400 (empty/oversized
batch) or 404 (duplicate IDs). The batch runs in one transaction: a stale `version` on **any**
item returns 409 and writes nothing at all, and an unknown work item or vendor returns 404 and
writes nothing. On success the response is a summary — `{ "changed": <int>, "unchanged": <int>,
"total": <int> }` — where `unchanged` counts items that already held that vendor. One timeline
entry is appended per item that actually changed.

### Other bulk work actions (PF-4.07)

`POST /api/work/bulk/{status|priority|schedule|note|reopen}` follow the same envelope and rules
as `bulk/vendor` — `items` is 1–100 entries with distinct `workId`s, one transaction, a stale
`version` on any item is a 409 that writes nothing, and the response is the same
`{ changed, unchanged, total }` summary. All five need `Work.Update`.

| Route | Body (besides `items`) | Item must be | Notes |
| --- | --- | --- | --- |
| `bulk/status` | `"status": "<WorkStatus>"` | not terminal | `Draft` target is 400; an item already in that status counts as `unchanged` |
| `bulk/priority` | `"priority": "<WorkPriority>"` | not terminal | a same-priority item counts as `unchanged` |
| `bulk/schedule` | `"scheduledStart"`, optional `"scheduledEnd"` | not terminal **and** have a vendor or employee | `end` before `start` is 400; moves each item to `Scheduled` |
| `bulk/note` | `"note"` (1–2000 chars), optional `"internal": true` | any (including terminal) | appends a `WorkNote` timeline entry carrying the text and `internal`/`resident` visibility |
| `bulk/reopen` | — | **all** completed or cancelled | moves each back to `Assigned` (if it has an assignee) or `New`, clears `completedAt`, writes a `WorkReopened` entry — the only sanctioned exit from a terminal state |

A batch that mixes assignable and non-assignable items is refused whole with a 400 whose title
names the requirement; nothing is written.

`add tag` from the original PF-4.07 list is not shipped — the tag model / vocabulary is still an
open product question (`docs/backlog.md`).

### Saved views

A saved view is scoped to the organization **and** to the calling user: the list, read, update
and delete routes only ever see the caller's own rows, so another user's view ID returns 404.
The body is `{ "name", "filters", "columns", "isDefault" }`, where `filters` and `columns` are
arbitrary JSON values the client defines (they are stored verbatim and validated only as
well-formed JSON; a name outside 1–100 characters or malformed JSON returns 400). Setting
`isDefault` on a view clears the flag on the caller's other views, so at most one default
exists per user. Listing returns the default first, then the rest by name.

There is no public registration endpoint. Use the administrative bootstrap command to create the first organization and account. The `seed-demo` command populates two organizations with a property hierarchy, vendor, employee, categories and work covering every status and priority (see [local development](local-development.md)).

Application errors use Problem Details with a request trace ID; unexpected details stay in server logs. Routing/authentication 401/403/404 and readiness responses may be empty/plain-text. Never log passwords, cookies or connection strings. OpenAPI documents endpoint shapes; these CSRF and capability requirements also apply even where generated metadata does not express them.

## Communications (milestone 4, in progress)

Enums serialize as their names (for example `"Sms"`, `"Email"`, `"Pending"`).

### Residents

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/residents | `Work.Read`; up to 200 tenant-scoped residents ordered by name |
| GET | /api/residents/{id} | `Work.Read`; one resident (name, email, phone, per-channel consent), or 404 including foreign-tenant IDs |
| GET | /api/residents/{id}/occupancies | `Work.Read`; the resident's occupancies (space, move-in/out dates) newest first, or 404 |
| POST | /api/residents | `People.Manage` + CSRF; `fullName`, `email`, `phone`; 201, 400 on invalid contact text |
| PUT | /api/residents/{id} | `People.Manage` + CSRF; `fullName`, `email`, `phone`; 200, 400, or 404 |
| PUT | /api/residents/{id}/consent | `People.Manage` + CSRF; `channel` (`Sms`/`Email`), `granted` (bool); records the decision with a server timestamp; 200 or 404 |
| POST | /api/residents/{id}/occupancies | `People.Manage` + CSRF; `spaceId`, `movedInOn` (`date`); 201, 400 for an unknown/foreign space, or 404 |
| PUT | /api/residents/{id}/occupancies/{occupancyId}/end | `People.Manage` + CSRF; `movedOutOn` (`date`); 200, 400 if it precedes move-in, or 404 |

A resident carries `smsConsent` / `emailConsent` (`Unknown` / `Granted` / `Revoked`) with the
decision timestamp. A message is only sent on a channel with an explicit `Granted` and a
matching address. Occupancy dates are `date` values; the current occupancy has no move-out
date. `People.Manage` is granted to Organization Admin and Property Manager. Contact fields
reject control characters (a resident name is a template substitution value).

### Assets

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/assets | `Work.Read`; up to 500 tenant-scoped assets ordered by name; optional `?propertyId=` filter |
| GET | /api/assets/{id} | `Work.Read`; one asset, or 404 including foreign-tenant IDs |
| POST | /api/assets | `Assets.Manage` + CSRF; `kind`, `name`, `propertyId`, `spaceId`, and the optional make/model/serial, `installedOn`/`warrantyExpiresOn`/`expectedServiceLifeYears`, `condition`, `replacementCostEstimate`, `notes`; 201, 400 for invalid text / a warranty before installation / a foreign or unknown property or space |
| PUT | /api/assets/{id} | `Assets.Manage` + CSRF; same body; the property and space are fixed at creation; 200, 400, or 404 |

`kind` is one of `Hvac`, `WaterHeater`, `Appliance`, `Roof`, `ElectricalPanel`,
`PlumbingFixture`, `Generator`, `Other`; `condition` is `Unknown`/`New`/`Good`/`Fair`/`Poor`/
`EndOfLife`. Dates are `date` values; `replacementCostEstimate` is money rounded to cents.
`Assets.Manage` is granted to Organization Admin, Property Manager, Regional Manager and
Maintenance Supervisor.

### Templates

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/communication/templates | `Communications.ManageTemplates`; tenant-scoped message templates ordered by name |
| GET | /api/communication/templates/{id} | `Communications.ManageTemplates`; one template, or 404 |
| POST | /api/communication/templates | `Communications.ManageTemplates` + CSRF; `name`, `channel` (`Sms`/`Email`), `subject` (required for email, rejected for SMS), `body` (may contain `{{ placeholder }}` tokens); 201 with the template, 400 on invalid content |
| PUT | /api/communication/templates/{id} | `Communications.ManageTemplates` + CSRF; `name`, `subject`, `body`, `isActive`; the channel is immutable; 200, 400, or 404 |
| DELETE | /api/communication/templates/{id} | `Communications.ManageTemplates` + CSRF; 204 or 404 |

`Communications.ManageTemplates` is granted to Organization Admin and Property Manager only.

The transactional outbox and its dispatcher are internal: messages are enqueued in the tenant
transaction that produces them, then delivered out of band by a background worker that polls
each organization on its own tenant context. Milestone 4 ships mock SMS and email providers
that record rather than send. No HTTP endpoint enqueues resident messages yet; that arrives
with the work-event wiring in a later task.

Dispatcher behavior is configured under `Communications` (`PollInterval`, `RetryDelay`,
`MaxDeliveryAttempts`, `StaleClaimTimeout`); defaults suit a single instance with mock
providers.

## Global search (milestone 6)

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/search | `Work.Read`; `?q=` (2–100 characters after trimming, else 400) and optional `?limit=` (1–50, default 20); returns a ranked JSON array of hits |

Each hit is `{ "type", "id", "label", "sublabel", "score" }`. `type` is one of `Property`,
`Building`, `Space`, `Resident`, `Vendor`, `Employee`, `Category`, `Asset`, `Work`. `sublabel`
is a secondary identifier where one exists (a resident's email or phone, an asset's serial
number, an employee's email) and is otherwise `null`.

Matching is case-insensitive and index-backed: both the substring match and the
word-similarity (typo) match ride the `gin_trgm_ops` indexes — the similarity floor is 0.25,
applied per search via a transaction-local `pg_trgm.word_similarity_threshold`. A substring
match always outranks a similarity-only match, so a query that is a typo of a name still finds
it but ranks below any literal substring hit. Residents also match on email and phone, assets on
serial number and model, employees on email. Every query runs through the tenant query filter
and row-level security, so results never cross an organization. The result set is capped at
`limit` hits total across all types.

## Integrations (milestone 6)

Adapters pull records from external property-management systems and PropFlow tracks what each
one has seen. Milestone 6 ships the abstraction and one mock adapter; it records the external
side only — reconciling those records into Properties / Spaces / Work / Assets is a later task.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/integrations/sources | `Integrations.Manage`; the adapters this deployment knows (`{ sourceSystem, displayName }`) |
| GET | /api/integrations | `Integrations.Manage`; every connection with its health snapshot |
| GET | /api/integrations/{id} | `Integrations.Manage`; one connection's health, or 404 |
| GET | /api/integrations/{id}/records | `Integrations.Manage`; a page of the external records the connection tracks, newest sighting first; `?page=` / `?pageSize=` (1–200, default 50); `{ items, totalCount, page, pageSize }`, or 404 |
| POST | /api/integrations | `Integrations.Manage` + CSRF; `{ sourceSystem, displayName }`; 201, 400 for an unknown source system or invalid text, 409 if a connection to that source already exists |
| POST | /api/integrations/{id}/enable | `Integrations.Manage` + CSRF; 204 or 404 |
| POST | /api/integrations/{id}/disable | `Integrations.Manage` + CSRF; 204 or 404 |
| POST | /api/integrations/{id}/sync | `Integrations.Manage` + CSRF; runs a pull; 200 with a sync report, 404, or 409 if the connection is disabled |

A health snapshot is `{ id, sourceSystem, displayName, isEnabled, lastAttemptedAt,
lastSucceededAt, consecutiveFailures, lastError, trackedRecords, failedRecords }`. A sync report
is `{ outcome, seen, added, updated, failed, error }` where `outcome` is `Completed` / `Failed`
/ `NotFound` / `Disabled`. `added` + `updated` are relative to what the connection had already
seen, keyed by a content hash, so re-syncing an unchanged source reports zeros. `failed` is
always 0 until reconciliation lands. A tracked record is `{ kind, externalId, contentHash,
syncState, lastSeenAt, lastError }`; `kind` is `Property` / `Space` / `Occupancy` / `WorkOrder`
/ `Asset` and `syncState` is `Pending` / `Synced` / `Failed`.

`Integrations.Manage` is granted to Organization Admin and Property Manager only. The
Integrations tables live in their own `integrations` schema with forced RLS, so a connection and
its records never cross an organization.
