# PropFlow HTTP API

The contract for every endpoint that exists today: the milestone-3 slice (auth, session, work,
assignment, bulk vendor, reference data, saved views), the milestone-4 communications surfaces
(residents, templates, the other bulk work actions), the milestone-5 automation rules, the
milestone-6 API-only surfaces (assets, global search, integrations) and the milestone-7
attachment storage. Section headings name the milestone each group came from.

Use HTTPS and retain cookies. API responses are JSON except successful 204s and the minimal readiness endpoint. API session/data responses use Cache-Control: no-store. Authentication failures return 401, authorization failures return 403; no HTML login redirects are used.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /health/live | 200 process liveness JSON |
| GET | /health/ready | 200 when database security/schema checks pass, otherwise 503 |
| GET | /api/auth/csrf | Public; returns `{ "token": "..." }` and secure antiforgery cookie |
| POST | /api/auth/login | CSRF required; organizationSlug/email/password; 204 success, 400 invalid input/CSRF, 401 rejected credentials or membership, 429 rate limit (10/min/IP; raised to 200/min under the Development posture so the e2e suite is not throttled — Testing and Production keep 10) |
| POST | /api/auth/password-recovery/request | CSRF required; accepts organizationSlug/email and always returns 202; matching active members receive an expiring reset token through the tenant communications outbox |
| POST | /api/auth/password-recovery/reset | CSRF required; accepts organizationSlug/email/token/newPassword; 204 on success or 400 for an invalid/expired/used token |
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
| GET | /api/portfolios/ | Work.Read; tenant-scoped portfolio lookup; optional `?q=` name filter; flat array capped at 500 |
| POST | /api/portfolios/ | Properties.Manage + CSRF; creates a tenant-scoped portfolio |
| PUT | /api/portfolios/{id} | Properties.Manage + CSRF; renames a tenant-scoped portfolio |
| POST | /api/portfolios/{id}/archive | Properties.Manage + CSRF; archives a portfolio from active lookups |
| POST | /api/portfolios/{id}/restore | Properties.Manage + CSRF; restores an archived portfolio |
| GET | /api/properties/ | Work.Read; tenant-scoped property lookup; optional `?q=` name filter; capped at 500 |
| GET | /api/properties/{id} | Work.Read; property with its buildings and spaces, or 404 |
| POST | /api/properties/ | Properties.Manage + CSRF; creates a property under a tenant-scoped portfolio |
| PUT | /api/properties/{id} | Properties.Manage + CSRF; updates a property's name and IANA time zone |
| POST | /api/properties/{id}/archive | Properties.Manage + CSRF; archives a property from active lookups |
| POST | /api/properties/{id}/restore | Properties.Manage + CSRF; restores an archived property |
| POST | /api/properties/{propertyId}/buildings | Properties.Manage + CSRF; creates a building in a property |
| PUT | /api/properties/{propertyId}/buildings/{buildingId} | Properties.Manage + CSRF; renames a building in a property |
| POST | /api/properties/{propertyId}/buildings/{buildingId}/archive | Properties.Manage + CSRF; archives a building from property detail |
| POST | /api/properties/{propertyId}/buildings/{buildingId}/restore | Properties.Manage + CSRF; restores an archived building |
| POST | /api/properties/{propertyId}/spaces | Properties.Manage + CSRF; creates a space, optionally under a building |
| PUT | /api/properties/{propertyId}/spaces/{spaceId} | Properties.Manage + CSRF; updates a space code and building assignment |
| GET/POST | /api/properties/{propertyId}/contacts | Read or add tenant-scoped property contacts |
| GET/POST | /api/properties/{propertyId}/documents | Read or add tenant-scoped property document links |
| GET | /api/properties/{propertyId}/amenities | Work.Read; active property amenities |
| POST/PUT | /api/properties/{propertyId}/amenities[/{amenityId}] | Properties.Manage + CSRF; creates or updates a property amenity |
| POST | /api/properties/{propertyId}/amenities/{amenityId}/archive | Properties.Manage + CSRF; archives an amenity |
| POST | /api/properties/{propertyId}/spaces/{spaceId}/archive | Properties.Manage + CSRF; archives a space from property detail |
| POST | /api/properties/{propertyId}/spaces/{spaceId}/restore | Properties.Manage + CSRF; restores an archived space |
| GET | /api/marketing/listings/ | Work.Read; tenant-scoped listings, optionally filtered by property or status; published unit listings are excluded once current occupancy exists and include `isAvailable` |
| GET | /api/marketing/listings/{id} | Work.Read; listing detail, or 404 |
| POST | /api/marketing/listings/ | Leasing.Manage + CSRF; creates a draft listing with availability and rent |
| PUT | /api/marketing/listings/{id} | Leasing.Manage + CSRF; updates listing content and availability |
| POST | /api/marketing/listings/{id}/publish | Leasing.Manage + CSRF; publishes a listing |
| POST | /api/marketing/listings/{id}/unpublish | Leasing.Manage + CSRF; returns a published listing to draft |
| GET/POST | /api/marketing/listings/{id}/inquiries | Read or submit an inquiry for an available published listing with optional lead-source attribution; active duplicate email inquiries return 409 |
| PUT | /api/marketing/listings/{id}/inquiries/{inquiryId}/status | Leasing.Manage + CSRF; advances inquiry status |
| GET/POST | /api/marketing/listings/{id}/showings | Read or request a showing for a published listing |
| PUT | /api/marketing/listings/{id}/showings/{showingId}/status | Leasing.Manage + CSRF; updates showing status |
| GET | /api/leasing/leases/ | Work.Read; tenant-scoped leases, optionally filtered by resident or space |
| GET | /api/leasing/leases/{id} | Work.Read; lease detail, or 404 |
| POST | /api/leasing/leases/ | Leasing.Manage + CSRF; creates a draft lease with resident, space, dates, and rent |
| POST | /api/leasing/leases/{id}/activate | Leasing.Manage + CSRF; activates a draft lease |
| POST | /api/leasing/leases/{id}/renew | Leasing.Manage + CSRF; records a renewal term and rent |
| POST | /api/leasing/leases/{id}/notice | Leasing.Manage + CSRF; records renewal/move-out notice and due date |
| POST | /api/leasing/leases/{id}/move-out | Leasing.Manage + CSRF; ends the lease on the supplied date |
| POST | /api/leasing/leases/{id}/transfer | Leasing.Manage + CSRF; transfers an active lease and moves its occupancy |
| GET | /api/leasing/leases/{id}/notices | Work.Read; notices attached to the lease |
| GET/POST | /api/leasing/leases/{id}/parties | Read or add additional tenant-scoped lease parties |
| GET/POST | /api/leasing/leases/{id}/documents | Read or add lease document links |
| POST | /api/leasing/leases/{id}/documents/{documentId}/send | Leasing.Manage + CSRF; sends a draft lease document |
| POST | /api/leasing/leases/{id}/documents/{documentId}/sign | Leasing.Manage + CSRF; records a signature and timestamp |
| GET | /api/portal/me | ResidentPortal.Read; the bound resident, current occupancy, leases, and resident-visible requests |
| POST | /api/portal/service-requests | ResidentPortal.Request + CSRF; creates a maintenance request only for the resident's current space |
| PUT | /api/portal/profile | ResidentPortal.Request + CSRF; updates the authenticated resident's own contact profile |
| PUT | /api/portal/preferences | ResidentPortal.Request + CSRF; updates the authenticated resident's email/SMS consent choices |
| POST | /api/portal/household-members | ResidentPortal.Request + CSRF; adds a household member to the authenticated resident's profile |
| GET | /api/portal/documents | ResidentPortal.Read; lists resident-visible documents attached to the resident's work |
| GET | /api/portal/documents/{attachmentId} | ResidentPortal.Read; downloads a document only when it belongs to the authenticated resident |
| GET | /api/portal/lease-documents | ResidentPortal.Read; lists resident-visible lease document links for the authenticated resident |
| GET/POST | /api/portal/payments | ResidentPortal.Read/Request; lists or submits a payment for the authenticated resident's own lease; optional `chargeId` requires an open charge and an exact amount match |
| GET | /api/leasing/leases/payments | Work.Read; lists tenant-scoped resident payments |
| POST | /api/leasing/leases/payments/{paymentId}/settle | Leasing.Manage + CSRF; settles a submitted payment and applies its amount to the linked charge atomically |
| GET/POST | /api/leasing/leases/{id}/charges | Read or add recurring/one-time lease charges |
| GET | /api/portal/charges | ResidentPortal.Read; lists charges for the authenticated resident's leases |
| GET | /api/billing/leases/{leaseId}/balance | Billing.Manage; ledger-consistent lease balance — charged, applied, outstanding, credits issued/applied/remaining, payments settled/refunded/applied |
| GET | /api/billing/leases/{leaseId}/charges | Billing.Manage; charges for the lease including `amountApplied` and `outstanding` |
| GET/POST | /api/billing/leases/{leaseId}/recurring-charges, POST /api/billing/recurring-charges | Billing.Manage + CSRF; reads or creates a monthly rent schedule (`dayOfMonth` 1–28) |
| POST | /api/billing/recurring-charges/{id}/pause\|resume | Billing.Manage + CSRF; suspends or restarts generation for a schedule |
| POST | /api/billing/recurring-charges/run | Billing.Manage + CSRF; generates charges through `through`. Idempotent — a repeat run for the same period generates nothing |
| GET/POST | /api/billing/leases/{leaseId}/credits | Billing.Manage + CSRF; lists or issues a lease credit |
| POST | /api/billing/credits/{creditId}/apply | Billing.Manage + CSRF; applies credit value to one charge on the same lease; 409 when the credit is exhausted |
| GET/POST | /api/billing/late-fee-rules | Billing.Manage + CSRF; one rule per scope — `propertyId` null is the organization default; 409 on a duplicate scope |
| POST | /api/billing/late-fees/run | Billing.Manage + CSRF; raises a `LateFee` charge for each overdue charge past its grace period. Idempotent — a charge is stamped `lateFeeAppliedOn` and never assessed twice |
| POST | /api/billing/charges/{chargeId}/payments | Billing.Manage + CSRF; takes a full or **partial** payment against a charge. 201 accepted, 402 declined (payment recorded as failed), **503 on provider outage with nothing written**, 409 when the amount exceeds the outstanding balance |
| GET/POST | /api/billing/payments/{paymentId}/refunds | Billing.Manage + CSRF; lists or issues a refund, reversing the amount on the linked charge. Replaying a `providerReference` returns 200 with `duplicate: true` and writes no second row |
| POST | /api/billing/payment-callback | **Unauthenticated, HMAC-SHA256 signed** via `X-PropFlow-Signature` over the raw body with `Billing:ProviderCallbackSecret`; CSRF-exempt. 201 on first delivery, **200 with `duplicate: true` on replay** — the unique index on (`OrganizationId`, `ProviderReference`) makes a replay a no-op, never a second payment |
| GET | /api/portal/announcements | ResidentPortal.Read; lists active published announcements for the resident's organization |
| GET | /api/announcements | Work.Read; manager announcement records |
| POST | /api/announcements | Leasing.Manage + CSRF; creates a draft announcement |
| POST | /api/announcements/{id}/publish | Leasing.Manage + CSRF; publishes an announcement to the resident portal |
| POST | /api/announcements/{id}/archive | Leasing.Manage + CSRF; archives an announcement |
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
`assetId`, `dueDate`, `cost`, `internalNotes` and `residentVisibleNotes`. The item is created
and published in one step, so it comes back in `New` status with a `createdAt`, and a
`WorkCreated` timeline entry is written in the same transaction. An empty or unknown/foreign
`propertyId` returns 400 and 404 respectively; a domain-invariant violation (title length,
negative cost) returns 400. An unknown/foreign `buildingId` / `spaceId` / `categoryId` /
`residentId` / `assetId` returns 400, as does an `assetId` for an asset at a different property.

`PUT /api/work/{id}` replaces the editable fields (including `assetId` — `null` clears the link)
and requires the `version` read from a previous response. It also accepts an optional `status`
and `scheduledStart`/`scheduledEnd`; a status change goes through the domain state machine, so
moving out of `Completed`/`Cancelled` or back to `Draft` is rejected with 400, and scheduling
without a vendor or employee is rejected with 400. Title, priority, status, schedule and asset
changes each append their own timeline entry (`WorkUpdated`, `PriorityChanged`, `StatusChanged`,
`Scheduled`, `AssetLinked`). A stale `version` returns 409 and requires a reload.

`GET /api/work/{id}/timeline` returns entries oldest first, all one shape:
`{ id, eventType, occurredAt, actorId, oldValue, newValue, relatedObjectType, relatedObjectId,
changes, residentVisible }`. `eventType` is the event name (`WorkCreated`, `WorkUpdated`,
`VendorAssigned`, `EmployeeAssigned`, `StatusChanged`, `PriorityChanged`, `Scheduled`,
`AssetLinked`, `WorkNote`, `WorkReopened`, and the communication events `MessageQueued` /
`MessageSent` / `MessageFailed`); `oldValue` / `newValue` are the human-readable before/after
(a name, a status, an id); `changes` is a JSON
object with the details. `residentVisible` is `false` for work-history events and `true` for a
resident communication. Communication entries are folded in from the outbox on read — there is
no separate write — so a `MessageQueued` entry becomes `MessageSent` in place once the
dispatcher delivers it.

There are no event-specific fields — in particular no `vendorId` / `previousVendorId`; a vendor
assignment reports the vendor through `newValue` and `changes` like every other event. `actorId`
is null for a system-generated entry. A stored entry is included whether it carries the work
item in its own `WorkId` **or** references it through `relatedObjectType: "WorkItem"` +
`relatedObjectId`, so an entry with no `WorkId` of its own still appears on the work item's
timeline.

`POST /api/work/{id}/message` (`Communications.SendMessage` + CSRF) queues a resident message
about the work item: body `{ "templateId": "<guid>" }`. It resolves the work item's resident,
checks per-channel consent, renders the template's subject/body against
`resident.name` / `work.title` / `work.status` / `property.name` / `schedule.start` /
`schedule.end` (the two schedule values in the property's IANA time zone, `""` when the item is
not scheduled) and `note.text` (the note body when the message was enqueued by a `WorkNoteAdded`
rule, `""` here), and enqueues on the template's channel. 202 with `{ "queued": true }` on
success; 404 for an unknown work item or template; 409 when the work item has no resident, the
template is inactive, or the resident has not consented to that channel; 400 for a template
placeholder with no value or a control character reaching a rendered subject. The same template
sent to the same work item twice within an hour is de-duplicated (the template implies the
channel) — `{ "queued": false }`, 202, nothing enqueued.

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
| `bulk/note` | `"note"` (1–2000 chars), optional `"internal": true` | any (including terminal) | appends a `WorkNote` timeline entry carrying the text and `internal`/`resident` visibility; a resident-visible note fires the `WorkNoteAdded` automation trigger |
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

### Attachments (PF-7.01)

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/work/{workId}/attachments | `Work.Read`; the work item's files, newest first; `{ id, fileName, contentType, length, residentVisible, createdAt, retainUntil }` |
| GET | /api/work/{workId}/attachments/{id} | `Work.Read`; streams the file with its original name and content type, or 404 |
| POST | /api/work/{workId}/attachments | `Work.ManageAttachments` + CSRF; `multipart/form-data` with `file` (required), `residentVisible` (`true`/`false`, default false), `retainUntil` (optional future timestamp); 201 with the record |
| DELETE | /api/work/{workId}/attachments/{id} | `Work.ManageAttachments` + CSRF; removes the row and the stored blob; 204 or 404 |

Uploads are capped at 25 MB and limited to `application/pdf`, `image/jpeg`, `image/png`,
`image/webp`, `image/heic` (a larger file is 413, a wrong type 400). Every route resolves the
work item through the tenant filter and then the field-role scope check, so a Technician only
sees and manages the attachments on their own assigned work. `Work.ManageAttachments` is granted
to Organization Admin, Property Manager, Regional Manager, Maintenance Supervisor, and a bound
Technician. `retainUntil` is an opt-in expiry: a background sweep deletes an attachment (row and
blob) once that time passes; a null retention never expires.

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
| GET | /api/assets/{id}/history | `Work.Read`; the asset plus every work item linked to it (newest first) and the roll-ups — `{ asset, ageInYears, underWarranty, workOrderCount, totalCost, history: [{ id, title, status, priority, categoryName, vendorName, createdAt, completedAt, cost }] }`; 404 for an unknown or foreign asset |
| POST | /api/assets | `Assets.Manage` + CSRF; `kind`, `name`, `propertyId`, `spaceId`, and the optional make/model/serial, `installedOn`/`warrantyExpiresOn`/`expectedServiceLifeYears`, `condition`, `replacementCostEstimate`, `notes`; 201, 400 for invalid text / a warranty before installation / a foreign or unknown property or space |
| PUT | /api/assets/{id} | `Assets.Manage` + CSRF; same body; the property and space are fixed at creation; 200, 400, or 404 |
| GET | /api/assets/repeat-repair-policy | `Work.Read`; the organization's repeat-repair thresholds — `{ repairThreshold, windowDays, matchByCategory }`; returns the defaults (`3`, `120`, `false`) until one is set |
| PUT | /api/assets/repeat-repair-policy | `Assets.Manage` + CSRF; upserts the single per-org policy; `repairThreshold` 2–50, `windowDays` 7–3650; 200 with the saved policy, or 400 out of range |
| GET | /api/assets/{id}/repeat-repair | `Work.Read`; assess one asset against the current policy — `{ repairThreshold, windowDays, matchByCategory, repairCount, since, totalCostInWindow, ageInYears, isRepeatRepair }`; counts published work linked to the asset with `createdAt` within the window; optional `?categoryId=` narrows the count to matching work when `matchByCategory` is on; 404 for an unknown or foreign asset |

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

## Automation rules (milestone 5)

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/automation/rules | `Settings.ManageAutomationRules`; the tenant's rules, name-ordered |
| POST | /api/automation/rules | `Settings.ManageAutomationRules` + CSRF; `{ name, trigger, conditions, actions }`; 201, or 400 on an invalid shape |
| POST | /api/automation/rules/{id}/enabled | `Settings.ManageAutomationRules` + CSRF; `{ "enabled": bool }`; 200, or 404 |

A rule is `{ trigger, conditions[], actions[] }` from a closed v1 vocabulary — `trigger` is
`WorkCreated`, `WorkStatusChanged` or `WorkNoteAdded` (a resident-visible note added; an
internal note never fires it); a `condition` is `WorkStatusEquals` (`status`) or
`CategoryEquals` (`categoryId`); an `action` is `SetPriority` (`priority`) or
`SendResidentMessage` (`templateId`). At least one action is required.

**Rules execute.** After a work create commits, an explicit status change commits (`PUT
/api/work/{id}`, `POST /api/work/bulk/status`), or a resident-visible note commits (`POST
/api/work/bulk/note` without `internal`), the evaluator runs the tenant's enabled rules
for that trigger, `AND`s every condition against the committed work item, and applies the
matches: `SetPriority` is skipped when the priority already matches or the work is terminal;
`SendResidentMessage` goes through the same path as `POST /api/work/{id}/message`, keyed so a
retry does not double-send. A `WorkNoteAdded` message can render `{{ note.text }}` with the note
body (the engine re-reads the committed note and refuses a non-resident-visible one); consent
and no-resident skips are recorded on the `AutomationApplied` timeline entry, which never
carries internal note text. Each rule is idempotent per occurrence (an `AutomationApplied`
timeline entry fences it), runs in its own transaction, and a failing rule is logged and
skipped without affecting the work write or other rules. Evaluation is post-commit and
in-process — there is no durable retry queue yet.

## Custom field definitions (FS-S03.01)

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/settings/custom-fields | `Work.Read`; the tenant's field definitions, sort-order then name |
| POST | /api/settings/custom-fields | `Settings.ManageConfiguration` + CSRF; `{ key, name, appliesTo, fieldType, options, isRequired, sortOrder }`; 201, 400 on an invalid shape, 409 on a duplicate `(appliesTo, key)` |
| PUT | /api/settings/custom-fields/{id} | `Settings.ManageConfiguration` + CSRF; `{ name, options, isRequired, sortOrder }` — `key`, `appliesTo` and `fieldType` are immutable after creation; 200, 400, or 404 |
| POST | /api/settings/custom-fields/{id}/archive | `Settings.ManageConfiguration` + CSRF; 204 or 404 |

`key` is a stable, lowercase machine identifier (`^[a-z][a-z0-9_]*$`, ≤ 50 chars), unique per
`(organization, appliesTo)`; `name` is the display label and may change freely. `appliesTo` is a
closed vocabulary — `WorkItem` today, the only entity type a custom field can attach to.
`fieldType` is one of `Text`, `Number`, `Date`, `Boolean`, `SingleSelect`; `options` is required
(≥ 1, unique, ≤ 100 chars each) for `SingleSelect` and rejected for every other type. Archiving
never deletes the row — a value already recorded against an archived definition stays readable.

Attaching a value to a work item (`CustomFieldValue`, validated against the live definition at
write time) is PF-S03.02, not yet built — this is the definition CRUD only.

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

## Attention queue (milestone 6)

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /api/attention | `Work.Read`; the actionable attention queue for the tenant — `{ items: [...], criticalCount, warningCount, informationalCount }` |

Each item is
`{ workId, title, propertyId, propertyName, status, priority, dueDate, severity, findings }`,
and each entry in `findings` is `{ reason, severity, detail }`. There is **one item per work
item**, however many rules it trips; `findings` carries them all, most urgent first, and the
item's `severity` is the most urgent among them. Items are ordered by `severity`
(Critical → Warning → Informational), then soonest `dueDate`, then work id.

The three counts are of **distinct work items** with at least one finding at that severity, so a
work item flagged both Critical and Warning is counted in each — and each count is exactly the
number of items whose `findings` contain that severity, which is what the `/attention` severity
cards filter the list to. The card and the list can therefore never disagree.

`reason` is one of:

| reason | severity | when |
| --- | --- | --- |
| `UnassignedEmergency` | Critical | `Critical` priority, open, with no vendor and no employee |
| `SlaBreach` | Critical for `Critical`/`High`, else Warning | still `New` past the first-response budget (Critical 4h, High 24h, Normal 72h, Low 120h from creation) |
| `Overdue` | Critical when the work is `Critical` priority, else Warning | `dueDate` in the past and not `Completed` |
| `WaitingOnVendor` | Warning | `OnHold` with a vendor assigned and no activity for 7 days |
| `WaitingOnResident` | Informational | `OnHold` with a resident (no vendor) and no activity for 7 days |
| `RepeatRepair` | Warning | open work on an asset over the repeat-repair count in the policy window |
| `UnitTurnAtRisk` | Critical when already past due, else Warning | a `UnitTurnTask` still `New`/`Assigned` and due within 5 days |

`Completed`, `Cancelled` and `Draft` work never appears. The thresholds are fixed in this
release (`AttentionThresholds`) — per-organization tuning is a follow-up. Every query runs
through the tenant query filter and row-level security.

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
## Billing acceptance workflows (FS-S08)

Billing also exposes tenant-scoped payment-method vault metadata (provider token and last four only), one immutable receipt per settled payment, idempotent provider-reference reconciliation records, and delinquency cases with run/contact/resolve transitions. Raw card or bank credentials are never stored.
