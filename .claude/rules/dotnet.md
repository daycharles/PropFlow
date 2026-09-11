---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/Directory.*.props"
  - "Directory.*.props"
  - "**/*.slnx"
  - "*.slnx"
---

# C# / .NET defaults

The *language and build* baseline for every C# change in PropFlow. The repo-specific rules that
also load for `.cs` changes are `architecture.md` (layering, endpoint shape) and, under the
persistence and migration paths, `tenancy-and-rls.md`. `CLAUDE.md` indexes the set and wins
where any two disagree. Citations verified 2026-09-09.

## The build contract

`Directory.Build.props` applies to every project — there is no per-project override:

- `net10.0`, `Nullable=enable`, `ImplicitUsings=enable` (`Directory.Build.props:3-5`).
- **`TreatWarningsAsErrors=true`** (`Directory.Build.props:6`). Fix the drift. Do **not** add a
  `#pragma warning disable` or a `<NoWarn>` to get a build through. If a suppression is
  genuinely correct, say why in the diff.
- `Deterministic=true` and `RestorePackagesWithLockFile=true`
  (`Directory.Build.props:7-8`).

## Package versions are inline and locked

**There is no `Directory.Packages.props` here** — this repo does not use central package
management. Versions are written inline on each `PackageReference`
(`src/PropFlow.Infrastructure/PropFlow.Infrastructure.csproj:5-7` is the fullest example) and
pinned by a committed `packages.lock.json` in all 9 projects.

Consequences:

- CI restores with `--locked-mode` (`.github/workflows/ci.yml:14`), which **fails** rather than
  resolving a changed graph.
- Adding or bumping a package means running a plain `dotnet restore PropFlow.slnx` to regenerate
  the affected lock files and committing them in the same change.
- Do not introduce `Directory.Packages.props` as a drive-by refactor. That is its own task.

## House style

Match the surrounding code, which is consistent:

- **`sealed` by default** and **primary-constructor injection**. See
  `OperationsStore.cs:12`, `EfWorkOperations.cs:9`,
  `ApiExceptionHandler.cs:7`, `HttpTenantContext.cs:5`, and every test class
  (`tests/PropFlow.UnitTests/*.cs` are all `public sealed class <Subject>Tests`).
- `sealed record` for DTOs and commands (`src/PropFlow.Application/Work/IWorkOperations.cs:8-12`).
- Inject `TimeProvider`, do not call `DateTimeOffset.UtcNow` in domain or application code — it
  is registered once at `src/PropFlow.Api/Program.cs:23` and consumed as a constructor parameter
  (`OutboxProcessor.cs:15`).
- Invariants live in domain constructors and throw `ArgumentException`
  (`src/PropFlow.Domain/TenantEntity.cs:7-8`), not in the endpoint.

## Before claiming it works

- Run `dotnet build` before saying a change compiles.
- Run **the project you touched**, not the whole solution: `dotnet test tests/PropFlow.UnitTests`
  for domain/application work, `tests/PropFlow.IntegrationTests` for anything that reaches the
  database or HTTP. A solution-wide run is usually the wrong granularity, and the integration
  suite costs a container.
- Paste the output. `.claude/rules/verification.md` says which line.
