# Architecture

## Stack and boundaries

A modular monolith: one ASP.NET Core API, one PostgreSQL database, and a separate Next.js/React/TypeScript/Tailwind frontend planned for milestone 3. The repository pins the installed .NET 10 SDK feature band. NuGet versions are explicit and transitive dependencies are locked.

Domain objects and application/persistence services hold behavior; HTTP endpoints bind requests and translate outcomes. Future modules remain Identity, Organizations, Properties, People, Work, Assets, Communications, Automation, Timeline and Integrations. Create module files as their behavior is implemented instead of empty projects.

```text
apps/web/                         Next.js boundary (milestone 3)
src/PropFlow.Api/                  endpoints, auth, errors, readiness
src/PropFlow.Application/          capability policies, tenant and use-case contracts
src/PropFlow.Domain/               tenant entities, assignment events, timeline
src/PropFlow.Infrastructure/       Identity, EF contexts, migrations, PostgreSQL
  Identity/                       global identity control plane
  Persistence/                    tenant-scoped operations and RLS
  Persistence/Migrations/         separate histories for each context
tools/PropFlow.Admin/              explicit privileged administration
tests/PropFlow.FoundationChecks/   domain/tenant/event checks
tests/PropFlow.IntegrationTests/   real PostgreSQL + API tests
```

## Identity and organization membership

Users are global identity principals; a user can belong to multiple organizations. Organizations and memberships live in the separate Identity control-plane store. They are not business-query surfaces. Login accepts an organization slug as a selection, verifies the password through ASP.NET Identity, and requires an active membership in an active organization before issuing a cookie. The slug is generated from the organization name at provisioning and is unique. No organization claim is accepted from a client header.

Every authenticated request checks the user/security stamp, lockout state, active organization and membership against the database. Capabilities are reconstructed from the current membership role. Changing a role or revoking membership takes effect on the next request. Logout changes the security stamp, revoking all of that user's existing sessions. Cookie lifetime is eight hours without sliding renewal.

The current capability set is Work.Read, Work.AssignVendor, Work.AssignEmployee, Work.Create, Work.Update, Settings.ManageCategories, Communications.ManageTemplates and People.Manage. Organization Admin and Property Manager receive all of them. Regional Manager receives the work capabilities plus Settings.ManageCategories; Maintenance Supervisor receives the work capabilities only. Read Only receives Work.Read. Unknown roles, Technician and Vendor receive nothing until assignment/property-level scopes are implemented. Some capabilities are granted ahead of the endpoints that consume them; the role mapping is reviewed as each endpoint lands. Capabilities are mapped centrally, and endpoints do not compare role names.

Login has a per-source-IP limit of ten attempts per minute and Identity lockout after five failed passwords for fifteen minutes. The API does not trust forwarded IP headers by default. A proxy deployment must configure trusted proxies deliberately. All API mutation requests, including login/logout, require a matching antiforgery cookie/header token. Cookies require HTTPS and are HttpOnly and SameSite Strict. Fetch a new request token after login. This follows ASP.NET's [antiforgery guidance](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Business-data isolation

Every business entity has immutable OrganizationId and Id fields. OperationsStore applies tenant query filters centrally and validates added/modified/deleted entities in both sync and async SaveChanges. Tenant context is fixed per request from the verified principal; administrative jobs must provide an explicit nonempty context. The EF approach follows the documented [tenant-context query-filter pattern](https://learn.microsoft.com/en-us/ef/core/querying/filters).

Business primary keys include OrganizationId. Work-to-vendor and timeline-to-work foreign keys include the tenant key. Organizations are referenced at database level, and timeline actors must have a membership in the same organization. PostgreSQL xmin detects conflicting work updates. A failed audit insert or concurrency conflict rolls back the assignment transaction.

Database RLS is enabled and forced on every current business table. A connection interceptor sets the verified organization on each connection checkout. Pooled connections are reset and the organization is overwritten on every open. Without a database tenant context, business reads return no rows. Tests bypass EF query filters and execute raw SQL/bulk operations to verify the database boundary still applies.

The API must use the restricted propflow_app account. It has no schema ownership, superuser, RLS-bypass, role-management or database-creation privileges; startup rejects such configurations. Migrations and provisioning use a separate administrative connection. PostgreSQL documents [RLS behavior and privileged-role exceptions](https://www.postgresql.org/docs/17/ddl-rowsecurity.html). RLS protects against missing filters and accidental cross-tenant operations; it is not a defense against an attacker with arbitrary SQL execution who can change session configuration. SQL input remains parameterized, and no raw SQL endpoint exists.

## Audit and events

Assignment domain events retain event ID, organization, work, actor, UTC timestamp, previous vendor and new vendor. No-op assignments create no duplicate history. Assignment plus timeline insertion commit in one SaveChanges transaction. Timeline modifications/deletions are blocked by EF, runtime database permissions and a PostgreSQL trigger. No external communication is dispatched by this milestone.

The in-process event dispatcher is a foundation, not a durable queue. The Communications module (milestone 4) adds a `communications`-schema context with its own migration history, RLS on every table, a per-organization `OutboxMessage` queue with a unique idempotency key, and a background dispatcher that polls each organization on its own tenant context using the restricted runtime role — no privileged connection. A message is claimed under an `xmin` concurrency token (`Pending` → `Sending`) before the provider is contacted, so competing dispatchers deliver it at most once; a claim left stale by a crash is reclaimed after a timeout. Failed attempts stay retryable, spaced by a retry delay, up to an attempt cap, then move to `Failed`. The poll interval, retry delay (default 2 minutes), attempt cap (default 8) and stale-claim timeout are bound from the `Communications` configuration section. Mock SMS and email providers record instead of sending. Enqueuing an outbox row inside the same transaction as the Operations change that triggers it, and idempotent real providers, are wired when work events start producing messages. Future adapters map external Property, Space, Person/Occupancy, Work and Asset records to canonical objects, retaining external IDs and sync status separately.

## Remaining decisions / boundaries

- Property- and assignment-level scope for regional, technician and vendor access remains to be defined before field workflows ship.
- Invitations, self-service account recovery, MFA and organization switching are not included. Initial provisioning is an administrator-only command.
- `Production`/`Staging` startup requires `DataProtection:KeyPath` (a persistent, instance-shared key ring, optionally certificate-encrypted via `DataProtection:CertificatePath`). A multi-instance deployment still needs trusted proxy configuration, managed secrets and PostgreSQL TLS; the local defaults are not a production deployment recipe.
- RLS policies and grants must accompany every new business table; migrations are reviewed and explicitly applied, never run by the API.
- Bulk all-or-nothing operations, bounded batches and client-facing concurrency tokens belong to milestone 3. The current API exposes only single-item assignment for persistence validation.
- Scheduling uses UTC instants plus property IANA zones when implemented.
- Message consent, provider callbacks, attachment storage/permissions, retention and workflow retry controls remain future work.
- Accounting, payments, leasing, predictive AI and native production PMS integrations remain outside the MVP.
