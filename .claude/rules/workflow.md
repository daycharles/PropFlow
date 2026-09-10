---
paths: "**"
---

# Ticket, branch and PR workflow

Covers how a PropFlow task travels from `docs/backlog.md` to `develop`. The branching spine is
`CLAUDE.md` → *Branching and flow*; this is the discipline around it. Verified 2026-09-09.

## Tracking

`docs/backlog.md` is the task source, and it is the source for the GitHub issues, not a mirror
of them (`backlog.md:5-15`). GitHub milestones `M3`–`M7`, epic issues `#1`–`#5`, task issues
`#6`–`#73`.

- Task ids are `PF-<milestone>.<nn>` — `PF-3.14`, `PF-4.02`. **They are stable references; never
  renumber** (`backlog.md:17`). New tasks take the next free number.
- The `PF-x.yy` id appears in the branch name, in **every** commit subject, and in the PR title.
  It is how a line of code is traced back to a decision.

## Branches

- **`develop` is the single integration line.** Anything merged there ships in the next
  promotion to `main`, so incomplete work must be behind a flag before it merges — and the issue
  must say so.
- One backlog task, or one small group of related tasks, = one branch off `develop`:
  `feat/pf-3-14-short-slug`, `fix/…`, `chore/…`, `docs/…`.
- **Push `-u` the day the branch is created.** Nothing stays local-only overnight; an
  unreachable branch is work nobody can review or recover.
- PRs open as **draft** until CI is green, then get marked ready. `verify`, `web` and `e2e` all
  gate a PR (`.github/workflows/ci.yml:6,24,41`) — but none of them is a **required** check:
  `main` carries no branch protection and no ruleset (verified 2026-09-10), so `gh pr merge`
  will happily merge red. Read `gh pr checks <n>` yourself before merging; see
  `docs/followups.md` for the open item.

## Teardown is part of Done

A task is not finished when the PR merges. It is finished when:

- the remote branch is deleted (delete-on-merge, or by hand),
- the local branch is deleted,
- any `git worktree` for it is removed with `git worktree remove <path>` from the owning repo.

A merged branch left standing is the reason the next session cannot tell what is in flight.

## Tracker status mirrors git

- branch created, work in flight → the issue is **assigned** and **In progress**,
- merged to `develop` (or `main`) → the issue is **closed** with a comment linking the PR,
- feature branch only → the issue stays in-progress,
- code shipping in a build while its issue says in-progress is not allowed. Either the work is
  flag-off and the issue says so, or the status moves.

### Assign the issue to the human whose agent it is — every time

The board and the issue list are how a human tells **whose agent did what**. An unassigned
closed issue loses that. So, as the first step of picking up a task and before opening the PR:

```bash
gh issue edit <n> --add-assignee mday440   # the human, NOT necessarily the token account
```

- Assign to the **human who owns the session**, which is not always the account `gh` is authed
  as. The macOS box's fine-grained PAT authenticates as `daycharles` (the repo owner), but the
  person running it is **Michael Day → `mday440`** — assign `mday440`, not `@me`.
- Only repo **collaborators** are assignable (`gh api repos/daycharles/PropFlow/assignees`).
  Today that is just `daycharles` and `mday440`. `michaelday` is a real account but not a
  collaborator; `daycdev` (the git-author name on the M3 commits, `cday@cushingsystems.us`) is
  not a GitHub account at all. So the M3 track cannot be assigned to its owner — by decision
  (2026-09-09) the **M3 task issues `#6`–`#31` stay assigned to `daycharles`** and that is what
  marks them as the other track. Do not reassign or unassign them.
- On merge, close the issue from the PR or by hand with a comment that names the PR and says an
  agent session did it:
  ```bash
  gh issue close <n> --reason completed \
    --comment "Done in PR #NN (merged to <branch>). <account> Claude Code session. <one line>."
  ```
- Partial / blocked work: assign it, comment what landed and what remains, leave it open.

### The board, and the commands that read and move it

The tracker is GitHub Projects **project 2**, owner `daycharles` — *@daycharles's PropFlow*,
project id `PVT_kwHOA6-kA84Bi9VY`. All ids below were read with `gh project field-list` on
2026-09-09; re-read them if the board is reconfigured.

**Auth (as of 2026-09-10):** the macOS box's `gh` (in `~/.local/bin`) uses a keyring-stored
`mday440` OAuth token with scopes `gist, project, read:org, repo, workflow`. **Do not set
`GH_TOKEN`** — that would fall back to the fine-grained PAT, which lacks `project` and
`workflow`. So an agent session here **can move Projects-v2 cards and push
`.github/workflows/**` directly**. Move a card as its task changes state; a `#agent-updates`
board-status line is still good practice but no longer the only channel.

Reading the board with `--format json` and piping through anything but `gh --jq` is fragile —
an item title can carry a control character that breaks a naive JSON parser. Use `gh project
item-list … --jq '.items[]|select(.content.number==N)|.id'`.

```bash
# Read the board. --limit defaults to 30 and truncates silently — always pass it.
gh project item-list 2 --owner daycharles --limit 300 --format json

# Status field id: PVTSSF_lAHOA6-kA84Bi9VYzhhzecU
#   Backlog      f75ad846
#   Ready        61e4505c
#   In progress  47fc9ee4
#   In review    df73e18b
#   Done         98236657
gh project item-edit --project-id PVT_kwHOA6-kA84Bi9VY --id ITEM_ID --field-id PVTSSF_lAHOA6-kA84Bi9VYzhhzecU --single-select-option-id 47fc9ee4

# Re-resolve field and option ids
gh project field-list 2 --owner daycharles --format json
```

The three policy lines above map onto those options:

| Git state | Status |
|---|---|
| branch created, work in flight, no PR yet | `In progress` |
| draft or open PR into `develop` | `In review` |
| merged to `develop` | `Done` |

Reads are allowlisted in `.claude/settings.json`. `gh project item-edit`, `gh issue edit`,
`gh issue close` and `gh pr create` are deliberately **not** — they sit in `ask`, so a tracker
write surfaces for approval instead of happening silently.

## Docs are reconciled in the same change

If a change makes a sentence in `README.md`, `docs/architecture.md`, `docs/api.md` or
`docs/local-development.md` untrue, fix that sentence in the same commit. Not a follow-up
commit, not a backlog item. `CLAUDE.md` → *Docs — what to trust* lists the drift that already
accumulated this way; do not add to it.

## Shared merge points

M4 and M6 are in flight simultaneously (M3 is complete and on `main`). Keep a change inside its
module's namespace. These three files are touched by every track — treat edits as **append-only**
and **rebase** rather than resolving a conflict destructively:

- `src/PropFlow.Api/Program.cs` — DI registrations and the `Map<Area>Endpoints` list
  (`:116-121`)
- `src/PropFlow.Application/Capabilities.cs` — the capability constants and `All`
- `src/PropFlow.Infrastructure/Persistence/DatabaseProvisioner.cs` — the migrate list and the
  `GRANT` block (`DatabaseProvisioner.cs:14-19, 53-66`)

A conflict in one of these means someone else added a line next to yours. Keep both.

## Not the process here

Imported from the PSIMS working agreement but deliberately **not** adopted: the two-commit
slice (one code commit, one doc-reconcile commit), Jira keys, and Bitbucket branch shapes.
PropFlow tracks on GitHub with `PF-x.yy`, and doc reconciliation goes in the commit that caused
it.
