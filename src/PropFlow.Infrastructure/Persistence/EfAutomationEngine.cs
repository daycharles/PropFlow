using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Application.Automation;
using PropFlow.Application.Communications;
using PropFlow.Domain.Automation;
using PropFlow.Domain.Timeline;

namespace PropFlow.Infrastructure.Persistence;

// The automation runtime. Called after a work change commits: it re-reads the committed work
// item, matches it against the tenant's enabled rules for the trigger, and applies each match.
//
// Idempotency has two layers. A rule's whole effect is fenced by an "AutomationApplied" timeline
// entry whose id is derived from (rule id, occurrence id): if it exists, the rule has already
// run for this occurrence and is skipped. Individual actions are idempotent too — SetPriority is
// a no-op when the priority already matches, SendResidentMessage rides the outbox idempotency
// key. Each rule runs in its own try/catch and its own SaveChanges, so one bad rule neither
// fails the work operation nor touches another rule's work. The audit entry's Changes record
// every message action's outcome, so a "skipped: no consent" is visible on the timeline.
public sealed class EfAutomationEngine(
    OperationsStore store,
    IResidentMessenger messenger,
    TimeProvider clock,
    ILogger<EfAutomationEngine> logger) : IAutomationEngine
{
    public async Task<AutomationRunSummary> RunAsync(AutomationTrigger trigger, Guid workId, Guid occurrenceId, CancellationToken cancellationToken)
    {
        var work = await store.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workId, cancellationToken);
        if (work is null) return AutomationRunSummary.Empty;

        // A note-triggered message can render {{ note.text }}. Only a resident-visible note gets
        // here (EfWorkOperations does not raise WorkNoteAdded for an internal one), but re-check
        // the committed entry so a stale trigger cannot leak an internal note into a message.
        string? noteText = null;
        if (trigger == AutomationTrigger.WorkNoteAdded)
        {
            var note = await store.Timeline.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == occurrenceId && t.EventType == "WorkNote", cancellationToken);
            if (note is null || !note.ResidentVisible) return AutomationRunSummary.Empty;
            noteText = Truncate(note.NewValue ?? "", NoteTextRenderLimit);
        }

        var rules = await store.AutomationRules.AsNoTracking()
            .Where(r => r.IsEnabled && r.Trigger == trigger)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        int matched = 0, applied = 0, failed = 0;
        foreach (var rule in rules)
        {
            if (!rule.Matches(work.Status, work.CategoryId)) continue;
            matched++;
            try
            {
                if (await ApplyAsync(rule, workId, occurrenceId, noteText, cancellationToken)) applied++;
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogError(exception,
                    "Automation rule {RuleId} failed for work {WorkId} ({Trigger}, occurrence {OccurrenceId}).",
                    rule.Id, workId, trigger, occurrenceId);
            }
        }

        if (matched > 0)
            logger.LogInformation("Automation {Trigger} on work {WorkId}: {Matched} matched, {Applied} applied, {Failed} failed.",
                trigger, workId, matched, applied, failed);
        return new(matched, applied, failed);
    }

    // Returns false when the rule had already been applied for this occurrence.
    private async Task<bool> ApplyAsync(AutomationRule rule, Guid workId, Guid occurrenceId, string? noteText, CancellationToken cancellationToken)
    {
        var fenceId = Deterministic(rule.Id, occurrenceId);
        if (await store.Timeline.AsNoTracking().AnyAsync(t => t.Id == fenceId, cancellationToken))
            return false;

        var now = clock.GetUtcNow();
        var extraValues = noteText is null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["note.text"] = noteText };
        var messageOutcomes = new List<object>();

        // Messages first — the outbox is a separate context and its own transaction, so a
        // SetPriority/audit rollback below cannot orphan a queued message (the idempotency key
        // makes a retry safe either way).
        foreach (var action in rule.ReadActions().Where(a => a.Kind == AutomationActionKind.SendResidentMessage))
        {
            var key = $"automation:{rule.Id:N}:{occurrenceId:N}:{action.TemplateId:N}";
            var result = await messenger.QueueForWorkAsync(workId, action.TemplateId!.Value, key, extraValues, cancellationToken);
            messageOutcomes.Add(new { templateId = action.TemplateId, outcome = result.Outcome.ToString() });
            if (result.Outcome is ResidentMessageOutcome.RenderFailed or ResidentMessageOutcome.TemplateNotFound)
                throw new InvalidOperationException($"Rule {rule.Id} message action: {result.Outcome} ({result.Detail}).");
            if (!result.Persisted)
                logger.LogInformation("Automation rule {RuleId} message skipped for work {WorkId}: {Outcome}.", rule.Id, workId, result.Outcome);
        }

        var tracked = await store.WorkItems.SingleOrDefaultAsync(x => x.Id == workId, cancellationToken);
        if (tracked is null) return false;

        foreach (var action in rule.ReadActions().Where(a => a.Kind == AutomationActionKind.SetPriority))
        {
            var target = action.Priority!.Value;
            if (tracked.Priority == target) continue;
            if (tracked.IsTerminal)
            {
                logger.LogInformation("Automation rule {RuleId} priority action skipped: work {WorkId} is terminal.", rule.Id, workId);
                continue;
            }
            var from = tracked.Priority;
            tracked.SetPriority(target);
            store.Timeline.Add(TimelineEntry.Record(tracked.OrganizationId, actorId: null, now, "PriorityChanged", "WorkItem", tracked.Id,
                from.ToString(), tracked.Priority.ToString(), tracked.Id,
                JsonSerializer.Serialize(new { oldValue = from.ToString(), newValue = tracked.Priority.ToString(), automationRuleId = rule.Id })));
        }

        store.Timeline.Add(TimelineEntry.Record(tracked.OrganizationId, actorId: null, now, "AutomationApplied", "AutomationRule", rule.Id,
            oldValue: null, newValue: rule.Name, workId: tracked.Id,
            JsonSerializer.Serialize(new { ruleId = rule.Id, ruleName = rule.Name, occurrenceId, messages = messageOutcomes }), id: fenceId));

        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            store.ChangeTracker.Clear();
            // A concurrent run wrote the fence first — its effect stands; anything else propagates.
            if (await store.Timeline.AsNoTracking().AnyAsync(t => t.Id == fenceId, cancellationToken)) return false;
            throw;
        }
        return true;
    }

    // A note can be up to 2000 characters, the same as a rendered message body. Feeding the whole
    // note into {{ note.text }} could push a template's rendered body past that limit and fail the
    // render — which fails the whole rule (silently, but logged). Cap the substitution so a long
    // note degrades to a truncated notification rather than no notification.
    private const int NoteTextRenderLimit = 1000;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";

    private static Guid Deterministic(Guid ruleId, Guid occurrenceId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"automation-applied:{ruleId:N}:{occurrenceId:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
