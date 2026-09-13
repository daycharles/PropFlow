using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Communications;

namespace PropFlow.Api;

public static class DocumentWorkflowEndpoints
{
    public static void MapDocumentWorkflowEndpoints(this WebApplication app)
    {
        var templates = app.MapGroup("/api/communication/document-templates").RequireAuthorization(Capabilities.ManageTemplates);
        templates.MapGet("/", async (CommunicationsStore store, CancellationToken ct) => Results.Ok(await store.DocumentTemplates.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)));
        templates.MapPost("/", async (CreateDocumentTemplateRequest request, CommunicationsStore store, CancellationToken ct) =>
        { try { var item = new DocumentTemplate(store.OrganizationId, Guid.NewGuid(), request.Name, request.Content); store.DocumentTemplates.Add(item); await store.SaveChangesAsync(ct); return Results.Created($"/api/communication/document-templates/{item.Id}", item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });
        templates.MapPut("/{id:guid}", async (Guid id, ReviseDocumentTemplateRequest request, CommunicationsStore store, CancellationToken ct) =>
        { var item = await store.DocumentTemplates.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); try { item.Revise(request.Content); await store.SaveChangesAsync(ct); return Results.Ok(item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } });

        var packets = app.MapGroup("/api/communication/document-packets").RequireAuthorization(Capabilities.ReadWork);
        packets.MapGet("/", async (CommunicationsStore store, CancellationToken ct) => Results.Ok(await store.DocumentPackets.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct)));
        packets.MapPost("/", async (CreateDocumentPacketRequest request, CommunicationsStore store, TimeProvider clock, CancellationToken ct) =>
        { var template = await store.DocumentTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TemplateId && x.IsActive, ct); if (template is null) return Results.NotFound(); var packet = new DocumentPacket(store.OrganizationId, Guid.NewGuid(), template.Id, request.Title, template.Content, request.ExpiresAt, template.Version); packet.SetRetention(request.RetainUntil ?? clock.GetUtcNow().AddDays(365)); store.DocumentPackets.Add(packet); await store.SaveChangesAsync(ct); return Results.Created($"/api/communication/document-packets/{packet.Id}", packet); }).RequireAuthorization(Capabilities.SendResidentMessage);
        packets.MapPost("/{id:guid}/send", async (Guid id, CommunicationsStore store, CancellationToken ct) => { var packet = await store.DocumentPackets.SingleOrDefaultAsync(x => x.Id == id, ct); if (packet is null) return Results.NotFound(); packet.Send(); await store.SaveChangesAsync(ct); return Results.Ok(packet); }).RequireAuthorization(Capabilities.SendResidentMessage);
        packets.MapPost("/{id:guid}/signers", async (Guid id, AddSignerRequest request, ClaimsPrincipal user, CommunicationsStore store, CancellationToken ct) => { if (!await store.DocumentPackets.AnyAsync(x => x.Id == id, ct)) return Results.NotFound(); var signerId = user.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(signerId)) return Results.Unauthorized(); var signer = new SignatureRequest(store.OrganizationId, Guid.NewGuid(), id, signerId, request.SignerName); store.SignatureRequests.Add(signer); await store.SaveChangesAsync(ct); return Results.Created($"/api/communication/document-packets/{id}/signers/{signer.Id}", signer); }).RequireAuthorization(Capabilities.SendResidentMessage);
        packets.MapPost("/{id:guid}/signers/{signerId:guid}/complete", async (Guid id, Guid signerId, ClaimsPrincipal user, CommunicationsStore store, TimeProvider clock, CancellationToken ct) => { var actor = user.FindFirstValue(ClaimTypes.NameIdentifier); var signer = await store.SignatureRequests.SingleOrDefaultAsync(x => x.Id == signerId && x.PacketId == id, ct); if (signer is null) return Results.NotFound(); if (actor != signer.SignerId) return Results.Forbid(); var packet = await store.DocumentPackets.SingleAsync(x => x.Id == id, ct); signer.MarkSigned(clock.GetUtcNow(), packet.IntegrityHash); packet.MarkSigned(); await store.SaveChangesAsync(ct); return Results.Ok(signer); }).RequireAuthorization(Capabilities.SendResidentMessage);
    }
}

public sealed record CreateDocumentTemplateRequest(string Name, string Content);
public sealed record ReviseDocumentTemplateRequest(string Content);
public sealed record CreateDocumentPacketRequest(Guid TemplateId, string Title, DateTimeOffset? ExpiresAt = null, DateTimeOffset? RetainUntil = null);
public sealed record AddSignerRequest(string SignerName);
