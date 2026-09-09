# Milestone 3 API

Use HTTPS and retain cookies. API responses are JSON except successful 204s and the minimal readiness endpoint. API session/data responses use Cache-Control: no-store. Authentication failures return 401, authorization failures return 403; no HTML login redirects are used.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /health/live | 200 process liveness JSON |
| GET | /health/ready | 200 when database security/schema checks pass, otherwise 503 |
| GET | /api/auth/csrf | Public; returns `{ "token": "..." }` and secure antiforgery cookie |
| POST | /api/auth/login | CSRF required; organizationSlug/email/password; 204 success, 400 invalid input/CSRF, 401 rejected credentials or membership, 429 rate limit |
| POST | /api/auth/logout | Auth + CSRF; 204; revokes all sessions for the current user |
| GET | /api/session | Auth; userId, organizationId, role and capabilities |
| GET | /api/work/ | Work.Read; up to 100 tenant-scoped work items ordered by title and ID |
| GET | /api/work/{id} | Work.Read; work record, or 404 including foreign-tenant IDs |
| GET | /api/work/{id}/timeline | Work.Read; chronological audit entries, or 404 |
| POST | /api/work/{id}/vendor | Work.AssignVendor + Work.Read + CSRF; accepts vendorId; atomically saves assignment and audit |
| GET | /api/vendors/ | Work.Read; tenant-scoped vendor lookup |
| GET | /api/vendors/{id} | Work.Read; vendor lookup, or 404 |
| GET | /api/employees/ | Work.Read; tenant-scoped employee lookup |
| GET | /api/properties/ | Work.Read; tenant-scoped property lookup |
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

Vendor assignment body:

```json
{ "vendorId": "<vendor GUID>" }
```

Success is `{ "changed": true }`; repeating the same assignment returns `{ "changed": false }` and creates no duplicate timeline entry. Unknown work/vendor IDs, including other organizations' IDs, return 404. Invalid IDs return 400. A database concurrency conflict returns 409 and requires a reload. Tenant, actor and permissions come only from the session; extra JSON fields, query strings and tenant headers cannot override them.

There is no public registration endpoint. Use the administrative bootstrap command to create the first organization and account. Work create/update, filtered/paginated list, client-facing concurrency tokens, bulk assignment, saved views, and the demo-data command are delivered as their M3 tasks land; do not rely on undocumented shapes while those changes are being integrated.

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

Matching is case-insensitive. A substring match (backed by `pg_trgm` GIN indexes) always
outranks a trigram-similarity-only match, so a query that is a typo of a name still finds it
but ranks below any literal substring hit. Residents also match on email and phone, assets on
serial number and model, employees on email. Every query runs through the tenant query filter
and row-level security, so results never cross an organization. The result set is capped at
`limit` hits total across all types.
