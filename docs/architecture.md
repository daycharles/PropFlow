# Architecture proposal

## Decisions

Use a modular monolith: one ASP.NET Core API and one PostgreSQL database, with a separate Next.js/React/TypeScript/Tailwind frontend. .NET 10 is installed locally and satisfies the requested .NET 8+ baseline. No prior repository decisions existed. Dependency versions for the web application and EF Core will be selected and locked when those components are implemented.

Keep domain logic in domain objects and application services, not HTTP handlers. Organize each layer into modules as behavior is implemented: Identity, Organizations, Properties, People, Work, Assets, Communications, Automation, Timeline, Integrations. Avoid empty projects for every future module.

```text
apps/web/                         Next.js (milestone 3)
src/PropFlow.Api/                  endpoints, auth, error translation
src/PropFlow.Application/          use cases, tenant access, event contracts
src/PropFlow.Domain/               tenant entities, domain events
src/PropFlow.Infrastructure/       EF Core, adapters, outbox (milestone 2)
tests/PropFlow.FoundationChecks/   foundation checks
tests/PropFlow.IntegrationTests/   PostgreSQL/API tests (milestone 2)
docs/
```

## Tenant and permission boundaries

Every business entity inherits an immutable nonempty OrganizationId. Resolve the active organization from an authenticated principal after checking membership; never trust a tenant header, route parameter, or posted organization ID. The initial session resolver rejects unauthenticated, missing, invalid, or ambiguous organization claims. Membership validation and claim issuance belong to milestone 2.

EF Core must apply central tenant query filters and enforce tenant IDs on every write. Foreign keys between tenant-owned entities must include OrganizationId to prevent cross-tenant relationships. Raw SQL and background jobs must use explicit scoped tenant access. Filters alone are insufficient: verify change tracking, bulk writes, and relationship integrity. No business data endpoints ship before these checks pass.

Authorize capabilities such as Work.AssignVendor. Map roles to capabilities in one policy layer. API cookies must not redirect unauthorized API requests to HTML pages. Login, secure cookie issuance, CSRF protection for writes, membership lookup, and production identity configuration are milestone 2 requirements.

## Events, history, and integrations

Domain events contain an event ID, organization, actor, timestamp, and event type. Assignment events record both previous and new vendor IDs. No-op assignments create no history. Use application services to save aggregate changes, timeline entries, and an outbox atomically in PostgreSQL. Dispatch handlers after commit; delivery retries use event IDs as idempotency keys. The initial in-process dispatcher is a contract foundation, not a durable queue. Never use it to deliver external messages before transaction commit.

Keep resident-visible communication separate from internal notes. Mock delivery must remain visibly labeled as mock. Define SMS/email interfaces and integration adapters around canonical Property, Space, Person/Occupancy, Work, and Asset objects; retain external ID, source system, sync status, and last sync separately.

Bulk assignment validates every work ID and vendor in the active organization, checks permissions and concurrency, then writes all changes and audit entries in one transaction. Start with atomic all-or-nothing behavior and a bounded batch size. Return actionable validation errors before committing. Scheduling uses UTC instants plus the property's IANA time zone.

## Risks and open decisions

- Identity provider and invitations are undecided. Start with ASP.NET Identity behind an application abstraction; do not create fixed production demo credentials.
- Portfolio/property-level access rules need definition beyond organization isolation.
- Tenant isolation and bulk write consistency are release gates, not later hardening.
- SMS consent, delivery callbacks, provider credentials, and retry behavior are required before real delivery; demo delivery is mocked.
- Workflow retries must prevent duplicate notifications and recursive event loops.
- Seed dates should be relative to the demo date so overdue and scheduled examples remain useful.
- Attachment storage, malware scanning, retention, and permissions remain to be designed before uploads.
- Native PMS integrations, accounting, payments, leasing, and predictive AI remain out of scope.
