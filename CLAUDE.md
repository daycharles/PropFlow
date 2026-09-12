# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

**PropFlow** — a modular property operations platform: work orders, bulk vendor assignment,
resident communication, and an append-only operational timeline. One ASP.NET Core minimal-API
host, one PostgreSQL database, one Next.js client in `apps/web`.

Umbrella solution `PropFlow.slnx` — 9 projects: 4 `src`, 3 `tests`, 2 `tools`. SDK pinned to
`10.0.300` with `rollForward: latestPatch` (`global.json`); this box has 10.0.303, which the
band accepts.

**M3–M7 are all complete and on `main`** (M6 was built in parallel with M5, so milestone number
is not milestone order; M7 is the cross-cutting deployment/hardening track). `docs/milestones.md`
holds the per-milestone status; `docs/backlog.md` carries the tasks. New work is backlog top-ups
against the existing epics.

## Build / test / run

**Use PowerShell.** Every gate and helper here is a `.ps1` and CI is a Linux runner; the Bash
tool works fine for read-only inspection (`grep`, `git log`, reading files) but run the gate in
`pwsh`.

Restore is **locked**: every one of the 9 projects has a committed `packages.lock.json`
(`Directory.Build.props:8` sets `RestorePackagesWithLockFile`). `--locked-mode` fails rather than
resolving a new graph, so adding or bumping a package means regenerating the locks in the same
commit.

The local mirror of CI (`docs/local-development.md:69-81`):

```powershell
dotnet restore PropFlow.slnx --locked-mode
dotnet build PropFlow.slnx --configuration Release --no-restore
dotnet run --project tests/PropFlow.FoundationChecks --configuration Release --no-build
dotnet test tests/PropFlow.UnitTests --configuration Release --no-build
dotnet test tests/PropFlow.IntegrationTests --configuration Release --no-build
Push-Location apps/web
npm ci
npm run format
npm run lint
npm run build
Pop-Location
```

First setup is `./scripts/Initialize-Local.ps1` — it writes ignored `.env` credentials, starts
PostgreSQL in Docker (Linux containers), migrates, configures the restricted `propflow_app` role
and provisions the first organization/admin. Integration tests need Docker reachable; they spin
their own disposable PostgreSQL 17 via Testcontainers.

Privileged operations are verbs on `tools/PropFlow.Admin` (`migrate`, `configure-runtime`,
`bootstrap`, `seed-demo`) — the required environment variable per verb is tabulated at
`docs/local-development.md:34-40`. **The API never runs migrations.**

## The merge gate

**Never merge to `develop` or `main` until it is tested.** CI job `verify`
(`.github/workflows/ci.yml:6-23`) is the gate on `main` and runs 1–4:

1. `dotnet build PropFlow.slnx -c Release` is clean (warnings are errors).
2. `dotnet run --project tests/PropFlow.FoundationChecks -c Release` passes.
3. `dotnet test tests/PropFlow.UnitTests -c Release` passes.
4. `dotnet test tests/PropFlow.IntegrationTests -c Release` passes (Docker required).

Two more CI jobs gate a PR and are easy to forget locally:

5. `web` (`ci.yml:24-40`) — in `apps/web`: `npm ci` → `npm run format` → `npm run lint` →
   `npm run build`. `format` is `prettier --check`, so it *fails* on unformatted files.
6. `e2e` (`ci.yml:41-88`, needs `verify` + `web`) — `docker compose up`, then
   `PropFlow.Admin migrate | configure-runtime | seed-demo`, then the API and `next dev`, then
   `npm run e2e` (Playwright).

The evidence each step must produce is in `.claude/rules/verification.md`. Open PRs as **draft**
until CI passes, then mark ready.

**None of those six is a *required* check.** `main` has no branch protection and no ruleset
(verified 2026-09-10: `gh api repos/daycharles/PropFlow/branches/main/protection` → 404,
`gh api repos/daycharles/PropFlow/rulesets` → empty), so a direct push lands and `gh pr merge`
merges red without complaint. Read `gh pr checks <n>` yourself before merging — the gate above
is convention, and the un-configured protection is an open item in `docs/followups.md`.

**Add tests with the code, not after.** New domain/application logic gets xUnit tests in
`tests/PropFlow.UnitTests`; database, RLS and HTTP behavior gets tests in
`tests/PropFlow.IntegrationTests`.

## Branching and flow

- **`main`** — stable and releasable, gated by `verify` **by convention, not by GitHub**. Never
  commit or push directly; a direct push will succeed, which is exactly why the rule matters.
- **`develop`** — the single integration line. Feature work merges here first.
- **Feature branches** — `feat/*`, `fix/*`, `chore/*`, `docs/*`, branched from `develop`, one
  backlog task (or a small related group) per branch, `PF-x.yy` in the branch name, in every
  commit subject and in the PR title.

Branch from `develop` → draft PR into `develop` → CI green → merge. Promote `develop` into `main`
with a PR once the integrated set is verified. Full discipline — push `-u` the day the branch is
created, teardown as part of Done, tracker status mirrors git — is in `.claude/rules/workflow.md`.

## Two rules short enough to keep here

**Under-claim rather than over-claim, and cite real `file:line`.** "I implemented X; I did not
verify Y" is a good answer; a claim you cannot point a line at is not a claim.

**When prose and code disagree, trust the code — and fix the prose in the same change.** Docs in
this repo drift. Reconciling them is part of the commit that invalidated them, not a follow-up.

## Where the detail lives

`CLAUDE.md` stays short on purpose — a long one gets skipped rather than followed. Everything
else lives in `.claude/rules/`, path-scoped so it loads when it is relevant. **`.claude/` is
team-shared and binding**, not personal; `.claude/settings.json` is the read-only permission
baseline and `.claude/settings.local.json` stays gitignored.

| Rules file | Loads for | What is in it |
|---|---|---|
| `verification.md` | all | The ladder, the evidence rules, the six-check passback gate |
| `workflow.md` | all | Branch/commit/PR/teardown, backlog tracking, the shared merge points |
| `traps.md` | all | Settled decisions that look like bugs — do not re-litigate |
| `dotnet.md` | `**/*.cs`, `**/*.csproj`, `**/Directory.*.props`, `**/*.slnx` | Warnings-are-errors, inline+locked package versions, house C# style |
| `architecture.md` | `src/**/*.cs`, `tests/**/*.cs`, `**/*.slnx` | Layer graph, the minimal-API endpoint shape, outcome enums, error taxonomy, the capability model |
| `tenancy-and-rls.md` | `src/PropFlow.Infrastructure/Persistence/**`, `.../Communications/**`, `**/Migrations/**`, `src/PropFlow.Api/HttpTenantContext.cs` | The five enforcement layers, the three DbContexts, migration rules |
| `testing.md` | `tests/**`, `.github/workflows/ci.yml` | Suite map, the fixture's two connection strings, naming |
| `web.md` | `apps/web/**` | Next 16 App Router, the script set, the `/api` proxy, Playwright |

## Docs — what to trust

| Document | Status |
|---|---|
| `docs/architecture.md` | Normative for cross-cutting design and persistence |
| `docs/backlog.md` | The task source. `PF-x.yy` ids are stable references — **never renumber**. ✅ in the Task cell = the GitHub issue is closed |
| `docs/api.md` | The HTTP contract for every endpoint that exists (M3–M7) |
| `docs/integration-sandbox-contract.md` | The **outbound** JSON-over-HTTP contract `SandboxIntegrationAdapter` pulls (PF-S19.07): payload shape, the per-connection credential keys, the synthetic-resident-id rule, the failure messages |
| `docs/local-development.md` | Accurate, PowerShell-first, matches the tree. The API port is **5001** everywhere (`.env.example`, `next.config.ts:7`, `demo-script.md`, the CI `e2e` job) |
| `docs/DEPLOYMENT.md` | The **production-posture** install path: a release bundle plus `propflow-deploy up`. Names the three settings Production hard-requires, and the two deliberate deviations (Kestrel-terminated TLS, loopback proxy allow-list) |
| `docs/RELEASE-TESTING.md` | The **Development** bring-up guide — running from source with `dotnet run` / `next dev`. Ported from the CI `e2e` job; the macOS-specific steps are noted there as unverified |
| `CHANGELOG.md` | Keep-a-Changelog, one entry per release, every line traceable to a `PF-x.yy` |
| `docs/audit-communications.md` | The honest defect/accepted-risk register for the outbox |
| `docs/demo-script.md` | The M3 walkthrough, in demo order. Leads with bulk vendor assignment |
| `docs/milestones.md` | Per-milestone status: what shipped and what remains. M3–M7 reconciled to GitHub 2026-09-11 |
| `docs/full-suite-scope.md` | The FS-S## full-suite scope, and the `PF-S05.NN` / `PF-S19.NN` sub-task tables. The Status cell is the truth for in-flight sub-task work — sub-tasks carry no GitHub issue |
| `docs/followups.md` | The open/deferred register, with a *Closed* section that keeps a gap from being re-filed. A row moves to *Closed* only with the evidence that closed it |

Known stale, verified 2026-09-10 — fix them if your change touches them:

- `Capabilities.cs` remains the authority for the role→capability map — restate it, never
  paraphrase from memory. `docs/architecture.md`'s "current capability set" paragraph names the
  M5/M7 additions in prose but its opening list is still the M3/M4 set; treat the code as truth.
- `DatabaseProvisioner.MigrateAsync` migrates **four** contexts (identity, operations,
  communications, integrations) — keep the `PropFlow.Admin` migrate message and any prose count
  in step when a fifth is added.

Counts in prose rot. Re-measure them; do not propagate them.
`docs/backlog.md` is the task source. GitHub milestones `M3`–`M7`, epic issues `#1`–`#5`,
task issues `#6`–`#73`. **`PF-3.27` is the one exception — it has no issue**, because it was
written after the M3 issues were cut (`baf4498`, PR #98); do not open one retroactively and do not
assume every `PF-x.yy` id resolves to an issue. Reference the `PF-x.yy` id in branch names,
commits, and PRs.

Keep the tracker in step with git, and **assign the task issue to the human whose agent it is**
so the board shows whose agent did what — on the macOS box that is `mday440` (Michael Day), not
the `daycharles` account the PAT authenticates as. Assign + move to *In progress* when you
branch, close with a PR-linking comment when it merges. Full procedure, ids, and the
fine-grained-PAT fallback (Issues write, no Projects access) are in
`.claude/rules/workflow.md` → *Tracker status mirrors git*.
