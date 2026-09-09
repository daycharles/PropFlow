# Milestone 2 API

Use HTTPS and retain cookies. API responses are JSON except successful 204s and the minimal readiness endpoint. API session/data responses use Cache-Control: no-store. Authentication failures return 401, authorization failures return 403; no HTML login redirects are used.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | /health/live | 200 process liveness JSON |
| GET | /health/ready | 200 when database security/schema checks pass, otherwise 503 |
| GET | /api/auth/csrf | Public; returns `{ "token": "..." }` and secure antiforgery cookie |
| POST | /api/auth/login | CSRF required; email/password/organizationId; 204 success, 400 invalid input/CSRF, 401 rejected credentials or membership, 429 rate limit |
| POST | /api/auth/logout | Auth + CSRF; 204; revokes all sessions for the current user |
| GET | /api/session | Auth; userId, organizationId, role and capabilities |
| GET | /api/work/ | Work.Read; up to 100 tenant-scoped work items ordered by title and ID |
| GET | /api/work/{id} | Work.Read; work record, or 404 including foreign-tenant IDs |
| GET | /api/work/{id}/timeline | Work.Read; chronological audit entries, or 404 |
| POST | /api/work/{id}/vendor | Work.AssignVendor + Work.Read + CSRF; accepts vendorId; atomically saves assignment and audit |
| GET | /openapi/v1.json | Authenticated generated OpenAPI contract |

Login body:

```json
{
  "email": "you@example.com",
  "password": "<your private password>",
  "organizationId": "<organization GUID printed during provisioning>"
}
```

Fetch a CSRF token before login, send it in X-CSRF-TOKEN, then fetch a fresh token after login. Anonymous tokens cannot authorize authenticated writes. Tokens are bound to the antiforgery cookie and current identity.

Vendor assignment body:

```json
{ "vendorId": "<vendor GUID>" }
```

Success is `{ "changed": true }`; repeating the same assignment returns `{ "changed": false }` and creates no duplicate timeline entry. Unknown work/vendor IDs, including other organizations' IDs, return 404. Invalid IDs return 400. A database concurrency conflict returns 409 and requires a reload. Tenant, actor and permissions come only from the session; extra JSON fields, query strings and tenant headers cannot override them.

The API does not yet expose work/vendor creation or bulk assignment. Integration tests provision data explicitly; the web workflow and demo dataset arrive in milestone 3. There is no public registration endpoint. Use the administrative bootstrap command to create the first organization and account.

Application errors use Problem Details with a request trace ID; unexpected details stay in server logs. Routing/authentication 401/403/404 and readiness responses may be empty/plain-text. Never log passwords, cookies or connection strings. OpenAPI documents endpoint shapes; these CSRF and capability requirements also apply even where generated metadata does not express them.

## Communications (milestone 4, in progress)

Enums serialize as their names (for example `"Sms"`, `"Email"`, `"Pending"`).

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
