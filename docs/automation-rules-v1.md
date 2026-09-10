# Automation rules v1 — PF-5.04

This is a persisted, closed WHEN/IF/THEN rule format. It intentionally has no user-authored
expressions, scripts, webhooks, or arbitrary field mutation. That keeps evaluation auditable and
lets a later visual builder operate on the same typed payloads.

| Part | Predefined values |
| --- | --- |
| WHEN | `WorkCreated`, `WorkStatusChanged` |
| IF (all conditions must match) | `WorkStatusEquals`, `CategoryEquals` |
| THEN (in stored order) | `SendResidentMessage(templateId)`, `SetPriority(priority)` |

The set is limited to the examples supported by the product source: an On The Way status can send
a resident SMS, and emergency work creation can become Critical and send a resident message.
`SendResidentMessage` still uses the existing template, recipient, and consent checks. A rule
does not send a message unless the matching work item has a contactable resident.

## Persisted contract

`operations.AutomationRules` stores tenant-scoped `Name`, `Trigger`, `Conditions` (jsonb),
`Actions` (jsonb), `IsEnabled`, `CreatedAt`, and `UpdatedAt`. The .NET domain model validates
every enum and each action's required parameter before serializing the lists.

## Evaluation recommendation

Dispatch a typed work-event envelope after its Operations transaction commits. Load enabled rules
for its trigger in stable ID order, evaluate all conditions against the event's current work
snapshot, then execute actions in their stored order. Action execution must not recursively
dispatch another automation event; a future expansion needs explicit loop semantics.

The current in-process dispatcher has neither durable event storage nor cross-schema atomicity
with the Communications outbox. Consequently, the persisted model is safe to administer now, but
automatic delivery must wait for the event/outbox transaction design in PF-5.03. The rule engine
should record evaluation/action outcomes before enabling automated sends.

## Explicitly deferred

`notify maintenance supervisor`, `start SLA timer`, vendor/employee assignment, scheduling,
arbitrary comparisons, and time-based triggers are not implementable from the current product
model. Adding any of them requires its own product contract and action semantics.
