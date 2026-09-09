# Security audit

Full security audit of the codebase, 2026-09-09 (commit `a02bff7`). No Critical or High
findings; no working cross-tenant data path. Tenant isolation (forced RLS + tenant policy +
org FK + EF query filter + write guard on all 11 business tables, verified by the integration
suite against real PostgreSQL) is sound. Dependency scan (`dotnet list package --vulnerable
--include-transitive`) is clean.

Status: **resolved** in a follow-up change, **accepted** (recorded, deferred), **open**.

## Findings

### M-1 — `identity` schema has no row-level security (Medium) — open

`AspNetUsers`, `Memberships`, `Organizations` and the role tables carry no RLS, and the
runtime role has `GRANT SELECT ON ALL TABLES IN SCHEMA identity`. Not exploitable today
(`MembershipAccess` has three correctly scoped methods and there is no listing endpoint), but
the "RLS is the backstop for a forgotten filter" guarantee does not extend to the control
plane — the data with the highest blast radius. Recommendation: forced RLS + an
`organization_id` policy on `Memberships` and `Organizations`; `AspNetUsers` is genuinely
global. Needs a design decision (invitations/member-list endpoints are on the M3+ roadmap).

### M-2 — Data Protection keys not persisted or shared (Medium) — resolved

The default key ring is local, unencrypted, per-instance. Auth and antiforgery cookies break
across a restart or a second instance.

**Resolution:** `Program.cs` binds `DataProtection:KeyPath` (persistent, instance-shared key
ring) and optional `DataProtection:CertificatePath` / `DataProtection:CertificatePassword`
(encryption at rest), sets a stable application name, and **refuses to start in `Production`
or `Staging` without a key path**. `Development`/`Testing` keep the ephemeral default; local
runs now set `ASPNETCORE_ENVIRONMENT=Development`. Covered by `DataProtectionStartupTests`.

### M-3 — `apps/web` has no lockfile and is outside CI (Medium) — open

No `package-lock.json`; CI never runs `npm ci` / `npm audit` / `next build`. Non-reproducible
frontend, no vulnerability scanning on browser-delivered code. Owned by the M3 web work.

### L-3 — No control-character guard on outbound message fields (Low now, High with real providers) — resolved

`Subject`, `RecipientAddress` and message bodies were length-checked only. A CR/LF in a
subject is SMTP header injection once a real provider is wired; a rendered template value is
unencoded.

**Resolution:** `MessageText.RequireSingleLine` / `RequireBody` reject control characters
(single-line fields reject all; bodies allow only newline/tab) in `OutboxMessage`,
`MessageTemplate` and `OutboundMessage` — `OutboundMessage` is the chokepoint every send
passes through. Context-aware template encoding and a recipient/egress policy for the real
provider adapter remain for PF-7.05.

### L-6 — Login username-enumeration timing oracle (Low) — resolved

An unknown email short-circuited before the password hash check, so it responded measurably
faster. **Resolution:** `SessionAuthentication.LoginAsync` verifies the supplied password
against a decoy hash when the user is not found, so both paths cost the same.

### L-1 — Tenant GUC is session-scoped, not transaction-scoped (Low) — accepted

`set_config('app.organization_id', …, false)`. Sound in the current configuration (interceptor
re-sets on every checkout; Npgsql resets pooled connections; tests confirm no leak). Fragile
to a future Npgsql multiplexing / reset-disabled change. Hardening (`SET LOCAL` per
transaction, or `RESET` on close) is deferred.

### L-2 — Runtime role holds unused `DELETE` on `WorkItems` / `OutboxMessages` (Low) — accepted

No endpoint deletes either. RLS still confines any delete to the current tenant. Drop `DELETE`
from those grants when confirming least-privilege; re-add per feature.

### L-4 — No HSTS / HTTPS redirection / security response headers (Low) — accepted

Cookies are `__Host-` + `Secure` + `HttpOnly` + `SameSite=Strict` and the API is JSON-only, so
exposure is low. `UseHsts`/`UseHttpsRedirection` and `X-Content-Type-Options` / `Referrer-Policy`
/ a web CSP are deferred to deployment hardening.

### L-5 — Login rate-limiter degrades to one global bucket when `RemoteIpAddress` is null (Low) — accepted

Per-account lockout (5/15min) partially compensates. Fixing it needs care around the existing
rate-limit test and trusted-proxy configuration; deferred.

### L-7 — Capability grants precede their endpoints (Low) — partially resolved

`Work.Create/Update/AssignEmployee`, `Settings.ManageCategories` are granted but only the
category endpoints exist. Docs refreshed; keep `Capabilities.ForRole` in lockstep as
endpoints land.

## Verified sound (no finding)

Tenant isolation core; EF query filter + write guard; `TenantConnectionInterceptor`;
background outbox path (restricted role, per-tenant context, `xmin` single-claim);
`DatabaseSafety` + startup guard; session cookie + per-request revalidation; org-claim
header/body rejection; CSRF on every mutation; IDOR on every `{id}` route; no raw/interpolated
SQL in `src`/`tools`; no committed secrets; `apps/web` output encoding.
