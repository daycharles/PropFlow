using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Communications;

namespace PropFlow.Api;

public static class CommunicationEndpoints
{
    public static void MapCommunicationEndpoints(this WebApplication app)
    {
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
}

// Tenant comes from the verified session; the channel is fixed once the template is created.
public sealed record CreateTemplateRequest(string Name, MessageChannel Channel, string? Subject, string Body);
public sealed record UpdateTemplateRequest(string Name, string? Subject, string Body, bool IsActive);
