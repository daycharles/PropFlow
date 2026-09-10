---
paths: "**"
---

# Settled — do not re-litigate, do not reintroduce

Things in PropFlow that read as bugs on first contact and are not. Each one has been decided;
each one names the code that proves it. If you are about to "fix" one of these, you are about to
break it. Verified 2026-09-09.

**`OutboxDispatcher` is not registered under `Testing`.**
`src/PropFlow.Api/Program.cs:41-43` registers the hosted poller only when the environment is not `Testing`, with
the comment saying why. Integration tests drive `OutboxRelay` / `OutboxProcessor` directly so a
background timer cannot race an assertion. This was audited and closed —
`docs/audit-communications.md:11` (C1, resolved). Do not "restore" the missing registration.

**`TenantConnectionInterceptor` is added in `OnConfiguring`, not DI.**
`OperationsStore.cs:26-27` and `CommunicationsStore.cs:19-20` attach it per context instead of
through `AddDbContext`, and it re-issues `set_config` on **every** `ConnectionOpened` rather than
once per scope. Npgsql resets pooled connections on return, so a once-per-scope version leaks the
previous tenant — `TenantConnectionInterceptor.cs:7-8` is the comment, and
`IsolationTests.cs:138` (`Pooled_connections_do_not_leak_previous_tenant`) is the test.

**RLS is `ENABLE` *and* `FORCE`.**
Both lines are required and neither is redundant: `ENABLE` turns policies on, `FORCE` makes them
apply to the table owner too (`20260909134500_TenantSecurity.cs:19-20`). `DatabaseReadiness`
checks `relrowsecurity AND relforcerowsecurity` and fails readiness if either is missing
(`DatabaseHealth.cs:32,36`).

**`OutboxProcessor` claims a row and saves *before* contacting the provider.**
`OutboxProcessor.cs:38-48` marks the message `Sending` and commits first, then delivers. The
losing racer's `DbUpdateConcurrencyException` is caught, the entity detached, and the message
skipped — that is the at-most-once design, not a swallowed error
(`OutboxProcessor.cs:7-11` states it). Do not move the save after delivery to "avoid" the
exception.

**`operations."Timeline"` is append-only at three levels, on purpose.**
EF refuses a non-Added timeline entry (`OperationsStore.cs:121-122`), the runtime role gets only
`GRANT SELECT, INSERT` (`DatabaseProvisioner.cs:63`), and a PostgreSQL trigger raises on UPDATE
or DELETE (`20260909134500_TenantSecurity.cs:30-35`). The "missing" UPDATE grant is the control.
Do not add it.

**`tests/PropFlow.IntegrationTests` references `tools/PropFlow.Admin` with
`ReferenceOutputAssembly="false"`.**
`PropFlow.IntegrationTests.csproj:8`. It is a build-order edge, not a code dependency — the
tests need the Admin tool built, not linked. Removing the attribute pulls the tool's assembly
into the test output for no reason.

**`Program.cs` ends with `public partial class Program { }`.**
`src/PropFlow.Api/Program.cs:124`. A top-level-statements program has an internal `Program`;
`WebApplicationFactory<Program>` (`DatabaseFixture.cs:53`) cannot bind without this. It looks
like dead code. It is not — delete it and the whole integration suite stops compiling.

**The API never runs migrations and refuses a privileged connection.**
`RuntimeDatabaseGuard` is a hosted service that opens the runtime connection at startup and
throws unless `DatabaseSafety.HasSafeRuntimeRoleAsync` passes (`DatabaseHealth.cs:7-17`,
registered `src/PropFlow.Api/Program.cs:87`). Schema work goes through
`dotnet run --project tools/PropFlow.Admin -- migrate`. Adding a `db.Database.Migrate()` to
startup for convenience defeats both controls.

**`Completed`/`Cancelled` work is terminal — except through `WorkItem.Reopen()`.**
`Edit`, `Schedule`, `SetPriority`, `ChangeStatus`, and both assignment overloads all call
`RefuseWhenTerminal()` (`WorkItem.cs`). `Reopen(actorId, at)` is the **one** sanctioned exit: it
requires `IsTerminal`, moves the item to `Assigned`/`New`, clears `CompletedAt`, and raises
`WorkReopened`. Audit A-1 (`docs/audit-2026-09-10.md`) fixed the old hole where `Schedule` had
no guard and silently un-completed work. Do not remove a `RefuseWhenTerminal()` call, and do not
add a second path out of a terminal state — extend `Reopen` instead.
