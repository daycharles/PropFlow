<!-- What this PR does, and the backlog task ID (PF-x.yy) if there is one. -->

## Checklist

- [ ] `dotnet build PropFlow.slnx -c Release` is clean (warnings are errors)
- [ ] `dotnet run --project tests/PropFlow.FoundationChecks -c Release` passes
- [ ] `dotnet test tests/PropFlow.UnitTests -c Release` passes
- [ ] `dotnet test tests/PropFlow.IntegrationTests -c Release` passes (Docker running)
- [ ] New logic has tests with it — xUnit for domain/application, IntegrationTests for DB/HTTP
- [ ] Docs updated where behavior changed (`docs/api.md`, `docs/architecture.md`, `docs/backlog.md`)
- [ ] Closed items removed from `docs/followups.md`; new follow-ups added there

### If this PR adds or changes a business table

- [ ] EF migrations generated with `dotnet ef` (not hand-edited designer/snapshot)
- [ ] A hand-written `*TenantSecurity` migration adds: org FK to `identity.Organizations`, `ENABLE` + `FORCE ROW LEVEL SECURITY`, and the `tenant_isolation` policy bound to `app.organization_id`
- [ ] `DatabaseProvisioner.ConfigureRuntimeAsync` grants the runtime role the **minimum** privileges the table needs (no `DELETE` unless an endpoint deletes)
- [ ] The central tenant convention (composite key, query filter, `GuardWrites`) covers the new entity
- [ ] An integration test proves cross-tenant read/write is blocked (query filter, RLS, and raw SQL)
- [ ] The migration is safe against a **populated** database, or the constraint is documented
