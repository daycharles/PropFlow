# Communications module — code audit

Audit of the milestone 4 Communications work (PRs #75 and #78): templates, rendering, consent,
mock senders, transactional outbox, dispatcher, and the template API. Date: 2026-09-09.

Status key: **resolved** in the same change as this document, **accepted** limitation recorded
for a later task.

## Findings

### C1 — Outbox dispatcher runs during integration tests (High) — resolved

`OutboxDispatcher` was registered unconditionally as a hosted service, so every integration
test that starts the web host also started a 10-second polling loop hitting the database. The
suite passed only because tests finish inside one poll interval; a slow test or an unlucky race
between an `Enqueue` and the first immediate tick would flake, and the loop did needless work on
every test.

**Resolution:** the dispatcher is registered only outside the `Testing` environment. Its
per-tenant relay logic is extracted to `OutboxRelay`, which tests drive directly.

### C2 — A poison message halts a tenant's dispatch cycle (High) — resolved

`OutboxProcessor` built an `OutboundMessage` and called the sender with no per-message guard. A
row that throws while constructing `OutboundMessage`, or a sender that throws, aborted the whole
cycle for that organization; a permanently bad row blocked every later message on every tick.

**Resolution:** each message is delivered inside its own try/catch. A construction or sender
exception is recorded as a delivery failure and the loop continues.

### C3 — Transient delivery failures were terminal (Medium) — resolved

The first failed send moved a message straight to `Failed` with no retry, so a momentary
provider or network blip permanently dropped a resident notification.

**Resolution:** a failed attempt keeps the message `Pending` and increments `AttemptCount`,
and the next attempt is held off by a retry delay, until the attempt cap is reached and the
message moves to `Failed` with the last error retained. Defaults — 2-minute retry delay, cap
of 8 (≈14 minutes of outage tolerance) — are bound from the `Communications` configuration
section (`CommunicationsOptions`), as are the poll interval and stale-claim timeout.
Capped-exponential backoff and transient/permanent error classification are deferred to
PF-7.05 (real providers).

### C4 — Multi-instance dispatch could double-send (Medium) — resolved

Two dispatcher instances both selected the same `Pending` rows, both sent, and both saved
`Sent` (last-writer-wins, no concurrency token), producing two real sends. The in-memory
dedupe is per instance and does not help across processes.

**Resolution:** `OutboxMessage` gains an `xmin` optimistic-concurrency token and a `Sending`
state. The processor claims a message (`Pending` → `Sending`, saved under the token) before
contacting the provider; a lost claim raises `DbUpdateConcurrencyException` and the worker
skips the row. A message stuck in `Sending` past `StaleClaimTimeout` (5 minutes) is reclaimed,
so a crash mid-send is recovered.

### C5 — `OutboxMessage` had two bindable constructors (Low) — resolved

EF Core constructor selection was implicit with both a private `(orgId, id)` constructor and a
public 8-parameter one. Round-tripping happened to work, but the validating constructor could
run on materialization.

**Resolution:** one private constructor for EF plus a static `OutboxMessage.Create(...)`
factory, matching `TimelineEntry`.

### C6 — `InMemorySentMessageLog` grew without bound (Low) — resolved

The singleton accumulated every "sent" message for the life of the host.

**Resolution:** capped to the most recent 500 entries; the idempotency-key set is trimmed with
it.

### C7 — Readiness check ignored the communications schema (Low) — resolved

`DatabaseReadiness` verified forced RLS only on the three operations tables.

**Resolution:** the check now also requires forced RLS on `communications.MessageTemplates`
and `communications.OutboxMessages`.

### C8 — Dispatcher tenant iteration was untested (Low) — resolved

Only `OutboxProcessor` had coverage; the per-organization loop had none.

**Resolution:** `OutboxRelay.RelayPendingAsync` is covered by an integration test that enqueues
in two organizations and asserts each is delivered under its own tenant context.

### C9 — Enum columns sized at 20 characters (Low) — resolved

`HasMaxLength(20)` left little headroom; a longer future enum name would silently truncate.

**Resolution:** widened to 32.

### C10 — Full organization scan every tick (Accepted)

The relay lists every organization and opens a context per tick regardless of pending work.
Fine at current scale. A pending-work signal (notify/listen or an unscoped summary table) is a
later optimization.

### C11 — Enqueue is not atomic with the triggering Operations change (Accepted)

The outbox is a separate context, so a message is not yet written in the same transaction as
the work change that produces it. This is wired in PF-4.06 when work events start producing
messages, together with idempotent real providers.

### C12 — Templates do not flag an unterminated `{{` (Accepted)

`"arriving at {{time"` (missing `}}`) renders literally rather than raising. Low impact for the
authored-template path; revisit if templates become user-generated at scale.
