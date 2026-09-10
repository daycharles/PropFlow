---
paths:
  - "src/PropFlow.Infrastructure/Persistence/**"
  - "src/PropFlow.Infrastructure/Communications/**"
  - "**/Migrations/**"
  - "src/PropFlow.Api/HttpTenantContext.cs"
---

# Tenant isolation, RLS and migrations

Multi-tenant isolation is the security property this system exists to hold. It is defended in
five places and **a new business table needs all five** — four out of five is a hole, not a
partial win. Citations verified 2026-09-09; `docs/architecture.md:33-41` is the prose.

## The five layers

1. **Claim → tenant context.** `TenantAccess.Resolve` reads exactly one `organization_id` claim
   from the authenticated identities and throws `TenantAccessException` on zero, many, or
   unparseable (`TenantAccess.cs:16-23`). `HttpTenantContext` memoizes it per request
   (`HttpTenantContext.cs:5-10`). Background and admin work uses `FixedTenantContext`, which
   refuses `Guid.Empty` (`ITenantContext.cs:9-17`). **No organization is ever read from a
   header or a request body.**
2. **EF model.** Every business entity derives from `TenantEntity`, whose constructor rejects an
   empty organization or id (`TenantEntity.cs:5-11`). The store applies a tenant query filter
   centrally for every `TenantEntity` (`OperationsStore.cs:87-96`), composite keys and foreign keys carry `OrganizationId`,
   and `GuardWrites` re-checks every Added/Modified/Deleted entry — including the *original*
   value, so a foreign row cannot be re-tagged — in both sync and async `SaveChanges`
   (`OperationsStore.cs:99-123`).
3. **Connection.** `TenantConnectionInterceptor` issues
   `SELECT set_config('app.organization_id', @tenant, false)` on every connection open
   (`TenantConnectionInterceptor.cs:29`) and throws rather than opening with an empty tenant
   (`:27`).
4. **Database.** The migration must `ENABLE` **and** `FORCE ROW LEVEL SECURITY` and create a
   `tenant_isolation` policy with both `USING` and `WITH CHECK` against
   `current_setting('app.organization_id', true)` — the pattern is
   `Migrations/Operations/20260909134500_TenantSecurity.cs:15-24`.
5. **Privilege.** An explicit `GRANT` for the new table in
   `DatabaseProvisioner.ConfigureRuntimeAsync` (`DatabaseProvisioner.cs:53-66`). `propflow_app`
   is created `NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB` (`:45-47`) and gets only the
   verbs the milestone needs. No blanket `GRANT ALL`.

`DatabaseReadiness` counts the tables in `operations`, `communications` and `integrations` and
fails the readiness probe if any is missing `relrowsecurity AND relforcerowsecurity`
(`DatabaseHealth.cs`) — so a table added without step 4 breaks `/health/ready`, and the
hard-coded minimum counts there need bumping with the table.

## Four DbContexts, four schemas, four histories

| Context | Schema | History table | Migrations folder |
|---|---|---|---|
| `IdentityStore` | `identity` | `__IdentityMigrations` | `Persistence/Migrations/Identity/` |
| `OperationsStore` | `operations` | `__OperationsMigrations` | `Persistence/Migrations/Operations/` |
| `CommunicationsStore` | `communications` | `__CommunicationsMigrations` | `Persistence/Migrations/Communications/` |
| `IntegrationStore` | `integrations` | `__IntegrationsMigrations` | `Persistence/Migrations/Integrations/` |

(`IntegrationStore.cs`, added by PF-6.10. `CLAUDE.md` and `docs/architecture.md` at various
points said two or three — there are four.)

A new module gets its own context, schema and history rather than joining an existing one, so
the migration histories never interleave. Several files must stay in step when that happens,
and missing one is the classic failure:

- `DatabaseProvisioner.cs` — the `MigrateAsync` list, the `Create<X>Store` factories, **and**
  the `GRANT` block (`USAGE ON SCHEMA …` plus per-table verbs — least privilege, no `GRANT ALL`).
- `src/PropFlow.Api/Program.cs` — the API's `AddDbContext` + `MigrationsHistoryTable` registrations.
- `DesignTimeFactories.cs` — the `IDesignTimeDbContextFactory` `dotnet ef` needs.
- `src/PropFlow.Api/DatabaseHealth.cs` — the per-schema table count in `DatabaseReadiness`.
- `tests/PropFlow.IntegrationTests/DatabaseFixture.cs` — a `Store`-style accessor for the new
  context so isolation tests can reach it on the restricted role.

## Migrations

- **Table/model migrations come from `dotnet ef`** — never hand-edit the `.Designer.cs` or the
  `*ModelSnapshot.cs`:

  ```powershell
  dotnet tool restore
  dotnet ef migrations add NAME --project src/PropFlow.Infrastructure --context OperationsStore
  ```

- **Security migrations are hand-written** `Migration` subclasses with `[DbContext]` +
  `[Migration]` attributes and raw `migrationBuilder.Sql`, with a real `Down`
  (`20260909134500_TenantSecurity.cs:7-9,39-50`). They have no designer file, and that is
  correct — the model is unchanged.
- Migrations are applied **only** by `dotnet run --project tools/PropFlow.Admin -- migrate`,
  against the privileged admin connection. The API never migrates and refuses to boot on a
  privileged connection (`DatabaseHealth.cs:7-17`).
