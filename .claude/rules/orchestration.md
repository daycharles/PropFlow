---
paths: "**"
---

# Orchestrating subagents

How a fan-out runs, and what reaches Slack while it does. Verified 2026-09-09.

This governs orchestration that is **already warranted** — several independent pieces of work
that do not share a file, where the conclusion matters more than the file dumps. It does not
authorize spawning agents for ordinary work. One agent doing the job directly stays the default,
and a single-file change never needs a fan-out.

## The loop

**1. At most five agents run at once.** Five is the ceiling, not a target. Hold the remaining
tasks in a queue and start one only as a slot frees. Launching every task at once buries the
failures and makes the Slack feed useless.

**2. A finished agent reports to the main agent.** A subagent's final report is not shown to
the user, so it is not a delivery — it is a handoff. The main agent owns everything the user or
the channel sees.

**3. The main agent posts that result to `#agent-updates`.** One message per completed task, not
per tool call:

```powershell
./scripts/Send-SlackUpdate.ps1 -Status ok   -Text 'review:bugs — 3 findings, all verified' -Link $prUrl
./scripts/Send-SlackUpdate.ps1 -Status fail -Text 'verify:rls — IsolationTests 2 failed'
```

The post names **which agent** and **which task**, states the outcome, and points at the
evidence. `ok` when the task passed its own check, `fail` when it did not. Reserve `info` for
batch-level events — the fan-out starting, the queue draining.

The webhook is post-only, so there is no read path and no delivery receipt. Say a message was
sent; never claim it landed. If `PROPFLOW_SLACK_WEBHOOK_URL` is unset the script exits non-zero
and says so — record that the update did not go out and carry on. A missing webhook must not
stall the work, exactly as it does not redden CI (`.github/workflows/ci.yml`, `Notify Slack`).

**4. The main agent hands that same agent the next queued task.** Use `SendMessage` with the
agent's id or name so it keeps the context it just built — the repo layout, the conventions, the
files it already read. A fresh `Agent` call throws that away and pays to rediscover it. Only
start a fresh agent when the next task is unrelated to what that agent was doing.

A task that came back `fail` is not silently reassigned. Post the failure, then decide: fix it
in the main session, or hand it back with what was learned. Re-running the same prompt against
the same agent and hoping is the thing this step exists to prevent.

## Why the main agent posts, and not the agents

One voice, in order. Five agents posting independently interleave into something nobody can
read, and each would need the webhook. The main session holds it, and the ordering of the feed
then matches the ordering of the work.
