# PropFlow — working agreement

Context for automated contributors (Claude Code) and humans. Read `docs/architecture.md`,
`docs/milestones.md`, and `docs/backlog.md` before starting a task.

## Branching and merge gate

- **`main`** — default branch, always stable and releasable. Protected: the `verify` CI check
  must be green. Do not commit or push here directly.
- **`develop`** — integration branch. Feature work merges here first.
- **Feature branches** — `feat/*`, `fix/*`, `chore/*`, `docs/*`, branched from `develop`.
  One branch per backlog task or small group of related tasks.

Flow: branch from `develop` → open a PR into `develop` → CI green → merge. Promote `develop`
into `main` with a PR once the integrated set is verified.

**Never merge to `develop` or `main` until it is tested:**

1. `dotnet build PropFlow.slnx -c Release` is clean (warnings are errors).
2. `dotnet run --project tests/PropFlow.FoundationChecks -c Release` passes (fast boot checks).
3. `dotnet test tests/PropFlow.UnitTests -c Release` passes.
4. `dotnet test tests/PropFlow.IntegrationTests -c Release` passes (needs Docker via Colima).
5. CI `verify` is green — it runs all of the above.

Open PRs as **draft** until CI passes, then mark ready.

**Add tests with the code, not after.** New domain/application logic gets xUnit tests in
`tests/PropFlow.UnitTests`; database, RLS, and HTTP behavior gets tests in
`tests/PropFlow.IntegrationTests`. `PropFlow.FoundationChecks` is a minimal boot smoke test —
do not grow it.

## Local toolchain

- .NET SDK **10.0.303** installed at `~/.dotnet` (pinned band `10.0.3xx`, see `global.json`).
  `~/.zshrc` exports `DOTNET_ROOT` and adds `~/.dotnet` + `~/.dotnet/tools` to `PATH`.
- `dotnet tool restore` provides `dotnet-ef` (10.0.12) for migrations.
- **Docker** runs via Colima (no admin rights needed): `colima` / `limactl` / `docker` are in
  `~/.local/bin`, the VM uses Apple's Virtualization.framework. Start it with `colima start`
  (state persists; `colima stop` to release resources). `~/.testcontainers.properties` sets
  `docker.socket.override=/var/run/docker.sock` so the Testcontainers Ryuk reaper mounts the
  in-VM socket path instead of the host path.

```bash
dotnet tool restore
dotnet restore PropFlow.slnx --locked-mode
dotnet build PropFlow.slnx -c Release --no-restore
dotnet run --project tests/PropFlow.FoundationChecks -c Release --no-build

colima start                                   # once per boot; Docker for the next command
dotnet test tests/PropFlow.IntegrationTests -c Release --no-build
```

## Persistence rules (from docs/architecture.md)

- One EF `DbContext` per module, each with its own schema, migrations subfolder, and history
  table (`identity`, `operations`; new modules follow the same split to avoid a shared
  migration history).
- Every new business table: central tenant convention, composite tenant foreign key where it
  applies, a reviewed PostgreSQL RLS policy in the migration, and a matching `GRANT` in
  `DatabaseProvisioner.ConfigureRuntimeAsync`.
- Migrations are reviewed and applied explicitly by `tools/PropFlow.Admin`; the API never runs
  them.
- Generate migrations with `dotnet ef` — do not hand-write the designer/snapshot files.

## Parallel work

Milestones 3 and 4 are in progress at the same time. Keep a change scoped to its module's
namespace. `Program.cs`, `Application/Capabilities.cs`, and
`Infrastructure/Persistence/DatabaseProvisioner.cs` are shared merge points — treat edits
there as append-only and rebase rather than resolve conflicts destructively.

## Tracking

`docs/backlog.md` is the task source. GitHub milestones `M3`–`M7`, epic issues `#1`–`#5`,
task issues `#6`–`#73`. Reference the `PF-x.yy` id in branch names, commits, and PRs.
