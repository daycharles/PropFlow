using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Communications;

// Renders a template for a work item's resident and queues it — the logic that used to live
// inline in POST /api/work/{id}/message, now shared with the automation SendResidentMessage
// action. Every "cannot reach the resident" case is a typed outcome, not an exception, so a
// caller (the endpoint, or the automation engine) decides whether it is an error.
public sealed class EfResidentMessenger(
    OperationsStore operations,
    CommunicationsStore comms,
    IOutbox outbox,
    ITemplateRenderer renderer) : IResidentMessenger
{
    public async Task<ResidentMessageResult> QueueForWorkAsync(Guid workId, Guid templateId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var work = await operations.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workId, cancellationToken);
        if (work is null) return new(ResidentMessageOutcome.WorkNotFound);
        if (work.ResidentId is not { } residentId) return new(ResidentMessageOutcome.NoResident);

        var template = await comms.MessageTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == templateId, cancellationToken);
        if (template is null) return new(ResidentMessageOutcome.TemplateNotFound);
        if (!template.IsActive) return new(ResidentMessageOutcome.TemplateInactive);

        var resident = await operations.Residents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == residentId, cancellationToken);
        if (resident is null) return new(ResidentMessageOutcome.NoResident);

        var channel = template.Channel;
        if (!resident.AllowsContact(channel))
            return new(ResidentMessageOutcome.NoConsent, $"The resident has not consented to {channel} contact, or has no {channel} address.");

        var property = await operations.Properties.AsNoTracking().SingleOrDefaultAsync(x => x.Id == work.PropertyId, cancellationToken);
        var zone = ResolveZone(property?.TimeZoneId);
        string LocalTime(DateTimeOffset? instant) => instant is not { } value ? ""
            : (zone is null ? value : TimeZoneInfo.ConvertTime(value, zone)).ToString("f");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resident.name"] = resident.FullName,
            ["work.title"] = work.Title,
            ["work.status"] = work.Status.ToString(),
            ["property.name"] = property?.Name ?? "",
            ["schedule.start"] = LocalTime(work.ScheduledStart),
            ["schedule.end"] = LocalTime(work.ScheduledEnd),
        };

        string body;
        string? subject;
        try
        {
            body = renderer.Render(template.Body, values);
            subject = template.Subject is null ? null : renderer.Render(template.Subject, values);
        }
        catch (TemplateRenderException e)
        {
            return new(ResidentMessageOutcome.RenderFailed, e.Message);
        }

        var recipient = channel == MessageChannel.Sms ? resident.Phone! : resident.Email!;
        var submission = new OutboxSubmission(channel, recipient, subject, body, idempotencyKey, WorkId: workId, ResidentVisible: true);

        try
        {
            var created = await outbox.EnqueueAsync(submission, cancellationToken);
            return new(created ? ResidentMessageOutcome.Queued : ResidentMessageOutcome.Deduplicated);
        }
        catch (ArgumentException e)
        {
            return new(ResidentMessageOutcome.RenderFailed, e.Message);
        }
    }

    // .NET resolves IANA ids on every platform; a stored id we cannot map renders in the original
    // offset rather than failing the send.
    private static TimeZoneInfo? ResolveZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}
