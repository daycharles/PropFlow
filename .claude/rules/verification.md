---
paths: "**"
---

# Verification

Covers how a change is proven done in PropFlow, and the evidence each gate step must produce.
Citations verified against the tree on 2026-09-09.

Claude stops when work *looks* done. Without a check it can run, "looks done" is the only
signal available and the human becomes the verification loop. Pick the weakest rung that closes
the loop for the task at hand — each one costs more setup than the last.

## The ladder

1. **In the prompt.** State the check and ask for it in the same message: "write it, run
   `dotnet test tests/PropFlow.UnitTests -c Release`, fix failures." Works today, on any task,
   with zero setup. This is the default and covers most work.
2. **A `/goal` condition.** A separate evaluator re-checks the condition after every turn and
   Claude keeps working until it holds. Use when the task spans many turns and the
   done-condition is easy to state but slow to reach.
3. **A `Stop` hook.** Runs a script and blocks the turn from ending until it passes —
   deterministic, not advisory. Use for unattended runs where nobody is reading the output.
   Claude Code overrides the hook after 8 consecutive blocks, so a stuck agent still exits.
4. **A review subagent.** A fresh context sees the diff and the criteria, not the reasoning
   that produced the change, so it grades on the result. `/code-review` for correctness;
   `plan-auditor` for "does this match the plan we agreed on."

## The passback gate

The merge gate in `CLAUDE.md` says *what* must pass. This says what to paste. Check every row
that applies before saying done.

| # | Check | Evidence to paste |
|---|---|---|
| 1 | `dotnet build PropFlow.slnx -c Release` clean — warnings are errors (`Directory.Build.props:6`) | the `Build succeeded. / 0 Warning(s) / 0 Error(s)` summary and the exit code |
| 2 | `dotnet run --project tests/PropFlow.FoundationChecks -c Release` | the `PASS` lines it prints, and the exit code |
| 3 | `dotnet test tests/PropFlow.UnitTests -c Release` | the test summary line (passed/failed/skipped/total) |
| 4 | `dotnet test tests/PropFlow.IntegrationTests -c Release`, Docker up | the test summary line, and the exit code |
| 5 | Touched `apps/web`? `npm run format && npm run lint && npm run build` in `apps/web` | the command output — `format` is `prettier --check`, it fails rather than rewrites |
| 6 | Added a business table? An RLS policy in the migration **and** a `GRANT` in `DatabaseProvisioner.ConfigureRuntimeAsync` | the migration `file:line` for the `ENABLE`/`FORCE`/`CREATE POLICY` block and the grant `file:line` |

Docker is up on this box and both test suites run here (verified 2026-09-09: `dotnet --version`
reports 10.0.303, `docker version` reports server 29.7.2). **"It doesn't run here" is not an
available answer** — run it. That makes the evidence bar higher, not lower.

## Rules

- **Show the evidence, not a summary of it.** Paste the command, its output, and the exit code.
  Reading evidence is faster than re-running the check, and it is the only thing that works for
  a session nobody was watching.
- **Address the root cause.** Suppressing a warning, deleting an assertion, or widening a type
  to make a check pass is a regression, not a fix. If the honest answer is "this needs a design
  change," say that.
- **Never delete or skip a test to get green.** If a test looks wrong, argue why.
- A reviewer asked to find gaps will find some, sound or not. Weigh findings against
  correctness and the stated requirements; ignore the rest rather than over-engineering.

## Two classes of green that mean nothing

- **Absent is not empty.** A missing log, a missing report, a missing artifact is not a clean
  one. If step 6 of the `e2e` job never started the API, `/tmp/propflow-api.log` does not exist
  — and "no errors in the log" is then a statement about a file you did not read. Confirm the
  artifact exists before reading a pass out of it.
- **Zero discovery is failure, not a pass.** A test run that discovers zero tests exits 0. Read
  the counts, never the exit code alone: `dotnet test tests/PropFlow.UnitTests` must report a
  non-zero passed count, and `tests/PropFlow.IntegrationTests` must have actually reached
  Docker rather than skipping its collection.
