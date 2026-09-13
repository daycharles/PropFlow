using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;
using PropFlow.Domain.People;
using PropFlow.Infrastructure.Communications;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class CommunicationEndpoints
{
    public static void MapCommunicationEndpoints(this WebApplication app)
    {
        MapResidentMessageEndpoint(app);
        MapConversationEndpoints(app);
        MapCampaignEndpoints(app);
        var group = app.MapGroup("/api/communication/templates").RequireAuthorization(Capabilities.ManageTemplates);

        // Senders need a safe picker even when they cannot administer templates.
        app.MapGet("/api/communication/templates/available", async (CommunicationsStore store, CancellationToken ct) =>
            Results.Ok(await store.MessageTemplates.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.Channel, x.Subject }).ToListAsync(ct)))
            .RequireAuthorization(Capabilities.SendResidentMessage);

        group.MapGet("/", async (CommunicationsStore store, CancellationToken ct) =>
            Results.Ok(await store.MessageTemplates.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, CommunicationsStore store, CancellationToken ct) =>
            await store.MessageTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } template
                ? Results.Ok(template) : Results.NotFound());

        group.MapPost("/", async (CreateTemplateRequest request, CommunicationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            MessageTemplate template;
            try
            {
                template = new MessageTemplate(tenant.OrganizationId, Guid.NewGuid(),
                    request.Name, request.Channel, request.Subject, request.Body);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            store.MessageTemplates.Add(template);
            await store.SaveChangesAsync(ct);
            return Results.Created($"/api/communication/templates/{template.Id}", template);
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateTemplateRequest request, CommunicationsStore store, CancellationToken ct) =>
        {
            var template = await store.MessageTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (template is null) return Results.NotFound();

            try
            {
                template.Rename(request.Name);
                template.Revise(request.Subject, request.Body);
                if (request.IsActive) template.Activate(); else template.Deactivate();
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            await store.SaveChangesAsync(ct);
            return Results.Ok(template);
        });

        group.MapDelete("/{id:guid}", async (Guid id, CommunicationsStore store, CancellationToken ct) =>
        {
            var template = await store.MessageTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (template is null) return Results.NotFound();
            store.MessageTemplates.Remove(template);
            await store.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void MapConversationEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/communication/conversations").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async (CommunicationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Conversations.AsNoTracking().OrderByDescending(x => x.LastMessageAt ?? x.CreatedAt).Take(200).ToListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid id, CommunicationsStore store, CancellationToken ct) =>
        {
            var conversation = await store.Conversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (conversation is null) return Results.NotFound();
            var messages = await store.ConversationMessages.AsNoTracking().Where(x => x.ConversationId == id)
                .OrderBy(x => x.OccurredAt).ToListAsync(ct);
            return Results.Ok(new { conversation, messages });
        });
        group.MapPost("/", async (CreateConversationRequest request, ClaimsPrincipal user, CommunicationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            try
            {
                var conversation = new Conversation(store.OrganizationId, Guid.NewGuid(), request.Subject, request.ParticipantAddress);
                conversation.Touch(clock.GetUtcNow());
                store.Conversations.Add(conversation);
                var message = new ConversationMessage(store.OrganizationId, Guid.NewGuid(), conversation.Id,
                    ConversationMessageDirection.Inbound, request.Channel, request.Body, user.FindFirstValue(ClaimTypes.NameIdentifier), clock.GetUtcNow());
                store.ConversationMessages.Add(message);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/communication/conversations/{conversation.Id}", conversation);
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.SendResidentMessage);
        group.MapPost("/{id:guid}/close", async (Guid id, CommunicationsStore store, CancellationToken ct) =>
        {
            var conversation = await store.Conversations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (conversation is null) return Results.NotFound(); conversation.Close(); await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.SendResidentMessage);
        group.MapPost("/{id:guid}/messages", async (Guid id, AddConversationMessageRequest request, ClaimsPrincipal user, CommunicationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var conversation = await store.Conversations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (conversation is null) return Results.NotFound();
            try
            {
                var message = new ConversationMessage(store.OrganizationId, Guid.NewGuid(), id, request.Direction, request.Channel, request.Body, user.FindFirstValue(ClaimTypes.NameIdentifier), clock.GetUtcNow());
                conversation.Touch(clock.GetUtcNow()); store.ConversationMessages.Add(message); await store.SaveChangesAsync(ct);
                return Results.Created($"/api/communication/conversations/{id}", message);
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.SendResidentMessage);
        group.MapPost("/unsubscribe", async (UnsubscribeRequest request, CommunicationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var existing = await store.ChannelUnsubscribes.SingleOrDefaultAsync(x => x.Channel == request.Channel && x.Address == request.Address.Trim(), ct);
            if (existing is not null) return Results.Accepted();
            store.ChannelUnsubscribes.Add(new ChannelUnsubscribe(store.OrganizationId, Guid.NewGuid(), request.Channel, request.Address, clock.GetUtcNow()));
            await store.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Capabilities.ReadWork);
    }

    private static void MapCampaignEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/communication/campaigns").RequireAuthorization(Capabilities.ManageTemplates);
        group.MapGet("/", async (CommunicationsStore store, CancellationToken ct) => Results.Ok(await store.Campaigns.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct)));
        group.MapPost("/", async (CreateCampaignRequest request, CommunicationsStore store, CancellationToken ct) =>
        {
            try { var campaign = new Campaign(store.OrganizationId, Guid.NewGuid(), request.Name, request.Channel, request.Subject, request.Body, request.ScheduledAt); store.Campaigns.Add(campaign); await store.SaveChangesAsync(ct); return Results.Created($"/api/communication/campaigns/{campaign.Id}", campaign); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        group.MapPost("/{id:guid}/publish", async (Guid id, PublishCampaignRequest request, CommunicationsStore store, OperationsStore operations, ICampaignDispatcher dispatcher, CancellationToken ct) =>
        {
            var campaign = await store.Campaigns.SingleOrDefaultAsync(x => x.Id == id, ct); if (campaign is null) return Results.NotFound();
            if (request.ResidentIds is not { Count: > 0 and <= 1000 } || request.ResidentIds.Any(x => x == Guid.Empty) || request.ResidentIds.Distinct().Count() != request.ResidentIds.Count) return Results.Problem(statusCode: 400, title: "1 to 1000 distinct resident ids are required");
            var residents = await operations.Residents.AsNoTracking().Where(x => request.ResidentIds.Contains(x.Id)).ToListAsync(ct);
            if (residents.Count != request.ResidentIds.Count) return Results.NotFound();
            var eligible = residents.Where(x => x.AllowsContact(campaign.Channel)).Select(x => new CampaignRecipient(x.Id, campaign.Channel == MessageChannel.Email ? x.Email! : x.Phone!)).ToList();
            var dispatched = await dispatcher.DispatchAsync(campaign, eligible, ct);
            return Results.Ok(new { campaign, dispatched });
        });
    }

    // POST /api/work/{id}/message — render a template for the work item's resident and queue it.
    // The outbox row carries the work id, so the message shows up on that work item's timeline
    // (MessageQueued now, MessageSent/MessageFailed once the dispatcher runs).
    private static void MapResidentMessageEndpoint(WebApplication app)
    {
        app.MapPost("/api/work/{workId:guid}/message", async (
            Guid workId, SendResidentMessageRequest request, IResidentMessenger messenger, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.TemplateId == Guid.Empty) return Results.Problem(statusCode: 400, title: "A template id is required");

            // Dedupe an accidental double-submit (same template, same work item, same hour). The
            // template id already implies the channel, so it is not in the key. A deliberate
            // re-send an hour later still goes through.
            var key = $"work-message:{workId:N}:{request.TemplateId:N}:{clock.GetUtcNow():yyyyMMddHH}";
            var result = await messenger.QueueForWorkAsync(workId, request.TemplateId, key, cancellationToken: ct);
            return result.Outcome switch
            {
                ResidentMessageOutcome.Queued => Results.Accepted($"/api/work/{workId}/timeline", new { queued = true }),
                ResidentMessageOutcome.Deduplicated => Results.Accepted($"/api/work/{workId}/timeline", new { queued = false }),
                ResidentMessageOutcome.WorkNotFound or ResidentMessageOutcome.TemplateNotFound => Results.NotFound(),
                ResidentMessageOutcome.RenderFailed => Results.Problem(statusCode: 400, title: result.Detail ?? "The template could not be rendered"),
                ResidentMessageOutcome.NoResident => Results.Problem(statusCode: 409, title: "This work item has no resident to contact"),
                ResidentMessageOutcome.TemplateInactive => Results.Problem(statusCode: 409, title: "That template is inactive"),
                _ => Results.Problem(statusCode: 409, title: result.Detail ?? "The resident cannot be contacted through that template's channel"),
            };
        }).RequireAuthorization(Capabilities.SendResidentMessage);

        // The batch is completely checked before the outbox transaction begins: an item without
        // a resident, consent, or renderable template prevents every message from being queued.
        app.MapPost("/api/work/bulk/message", async (
            BulkResidentMessageRequest request, OperationsStore operations, CommunicationsStore comms,
            IOutbox outbox, ITemplateRenderer renderer, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.TemplateId == Guid.Empty || request.WorkIds is not { Count: > 0 and <= 100 }
                || request.WorkIds.Any(id => id == Guid.Empty) || request.WorkIds.Distinct().Count() != request.WorkIds.Count)
                return Results.Problem(statusCode: 400, title: "A template and 1 to 100 distinct work items are required");

            var template = await comms.MessageTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TemplateId, ct);
            if (template is null) return Results.NotFound();
            if (!template.IsActive) return Results.Problem(statusCode: 409, title: "That template is inactive");

            var works = await operations.WorkItems.AsNoTracking().Where(x => request.WorkIds.Contains(x.Id)).ToListAsync(ct);
            if (works.Count != request.WorkIds.Count) return Results.NotFound();
            if (works.Any(x => x.ResidentId is null)) return Results.Problem(statusCode: 409, title: "Every selected work item needs a resident to contact");
            var residentIds = works.Select(x => x.ResidentId!.Value).Distinct().ToArray();
            var residents = await operations.Residents.AsNoTracking().Where(x => residentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            if (residents.Count != residentIds.Length) return Results.NotFound();
            if (residents.Values.Any(x => !x.AllowsContact(template.Channel)))
                return Results.Problem(statusCode: 409, title: $"Every selected resident must consent to {template.Channel} contact and have a matching address");
            var propertyIds = works.Select(x => x.PropertyId).Distinct().ToArray();
            var properties = await operations.Properties.AsNoTracking().Where(x => propertyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var now = clock.GetUtcNow();
            var submissions = new List<OutboxSubmission>(works.Count);
            try
            {
                foreach (var work in works)
                {
                    var resident = residents[work.ResidentId!.Value];
                    var values = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["resident.name"] = resident.FullName, ["work.title"] = work.Title,
                        ["work.status"] = work.Status.ToString(), ["property.name"] = properties.GetValueOrDefault(work.PropertyId)?.Name ?? "",
                        ["schedule.start"] = work.ScheduledStart?.ToString("f") ?? "", ["schedule.end"] = work.ScheduledEnd?.ToString("f") ?? "",
                    };
                    var body = renderer.Render(template.Body, values);
                    var subject = template.Subject is null ? null : renderer.Render(template.Subject, values);
                    var recipient = template.Channel == MessageChannel.Sms ? resident.Phone! : resident.Email!;
                    submissions.Add(new(template.Channel, recipient, subject, body,
                        $"work-message:{work.Id:N}:{template.Id:N}:{template.Channel}:{now:yyyyMMddHH}", work.Id, ResidentVisible: true));
                }
            }
            catch (TemplateRenderException e) { return Results.Problem(statusCode: 400, title: e.Message); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }

            var queued = await outbox.EnqueueBatchAsync(submissions, ct);
            var changed = queued.Count(x => x);
            return Results.Accepted("/api/work", new { changed, unchanged = queued.Count - changed, total = queued.Count });
        }).RequireAuthorization(Capabilities.SendResidentMessage);
    }
}

// Tenant comes from the verified session; the channel is fixed once the template is created.
public sealed record CreateTemplateRequest(string Name, MessageChannel Channel, string? Subject, string Body);
public sealed record UpdateTemplateRequest(string Name, string? Subject, string Body, bool IsActive);
public sealed record SendResidentMessageRequest(Guid TemplateId);
public sealed record BulkResidentMessageRequest(Guid TemplateId, List<Guid>? WorkIds);
public sealed record CreateConversationRequest(string Subject, string ParticipantAddress, MessageChannel Channel, string Body);
public sealed record AddConversationMessageRequest(ConversationMessageDirection Direction, MessageChannel Channel, string Body);
public sealed record UnsubscribeRequest(MessageChannel Channel, string Address);
public sealed record CreateCampaignRequest(string Name, MessageChannel Channel, string Subject, string Body, DateTimeOffset? ScheduledAt = null);
public sealed record PublishCampaignRequest(List<Guid>? ResidentIds);
