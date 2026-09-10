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
        var group = app.MapGroup("/api/communication/templates").RequireAuthorization(Capabilities.ManageTemplates);

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

    // POST /api/work/{id}/message — render a template for the work item's resident and queue it.
    // The outbox row carries the work id, so the message shows up on that work item's timeline
    // (MessageQueued now, MessageSent/MessageFailed once the dispatcher runs).
    private static void MapResidentMessageEndpoint(WebApplication app)
    {
        app.MapPost("/api/work/{workId:guid}/message", async (
            Guid workId, SendResidentMessageRequest request,
            OperationsStore operations, CommunicationsStore comms, IOutbox outbox, ITemplateRenderer renderer,
            CancellationToken ct) =>
        {
            if (request.TemplateId == Guid.Empty) return Results.Problem(statusCode: 400, title: "A template id is required");

            var work = await operations.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workId, ct);
            if (work is null) return Results.NotFound();
            if (work.ResidentId is not { } residentId)
                return Results.Problem(statusCode: 409, title: "This work item has no resident to contact");

            var template = await comms.MessageTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TemplateId, ct);
            if (template is null) return Results.NotFound();
            if (!template.IsActive) return Results.Problem(statusCode: 409, title: "That template is inactive");

            var resident = await operations.Residents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == residentId, ct);
            if (resident is null) return Results.NotFound();

            var channel = template.Channel;
            if (!resident.AllowsContact(channel))
                return Results.Problem(statusCode: 409, title: $"The resident has not consented to {channel} contact, or has no {channel} address");

            var property = await operations.Properties.AsNoTracking().SingleOrDefaultAsync(x => x.Id == work.PropertyId, ct);
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resident.name"] = resident.FullName,
                ["work.title"] = work.Title,
                ["work.status"] = work.Status.ToString(),
                ["property.name"] = property?.Name ?? "",
                ["schedule.start"] = work.ScheduledStart?.ToString("f") ?? "",
                ["schedule.end"] = work.ScheduledEnd?.ToString("f") ?? "",
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
                return Results.Problem(statusCode: 400, title: e.Message);
            }

            var recipient = channel == MessageChannel.Sms ? resident.Phone! : resident.Email!;
            var submission = new OutboxSubmission(channel, recipient, subject, body,
                $"work-message:{workId:N}:{Guid.NewGuid():N}", WorkId: workId, ResidentVisible: true);

            try
            {
                await outbox.EnqueueAsync(submission, ct);
            }
            catch (ArgumentException e)
            {
                return Results.Problem(statusCode: 400, title: e.Message);
            }

            return Results.Accepted($"/api/work/{workId}/timeline", new { queued = true });
        }).RequireAuthorization(Capabilities.SendResidentMessage);
    }
}

// Tenant comes from the verified session; the channel is fixed once the template is created.
public sealed record CreateTemplateRequest(string Name, MessageChannel Channel, string? Subject, string Body);
public sealed record UpdateTemplateRequest(string Name, string? Subject, string Body, bool IsActive);
public sealed record SendResidentMessageRequest(Guid TemplateId);
