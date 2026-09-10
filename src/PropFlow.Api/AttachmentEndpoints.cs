using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Application.Attachments;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class AttachmentEndpoints
{
    private const long MaxLength = 25 * 1024 * 1024;
    private static readonly HashSet<string> AllowedTypes = ["application/pdf", "image/jpeg", "image/png", "image/webp", "image/heic"];
    private static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["application/pdf"] = ".pdf", ["image/jpeg"] = ".jpg", ["image/png"] = ".png",
        ["image/webp"] = ".webp", ["image/heic"] = ".heic"
    };

    public static void MapAttachmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/work/{workId:guid}/attachments").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/", async (Guid workId, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.WorkItems.AnyAsync(x => x.Id == workId, ct)) return Results.NotFound();
            var attachments = await store.Attachments.AsNoTracking().Where(x => x.WorkId == workId)
                .OrderByDescending(x => x.CreatedAt).Select(x => ToResponse(x)).ToListAsync(ct);
            return Results.Ok(attachments);
        });

        group.MapGet("/{attachmentId:guid}", async (Guid workId, Guid attachmentId, OperationsStore store,
            IAttachmentStorage storage, CancellationToken ct) =>
        {
            var attachment = await store.Attachments.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == attachmentId && x.WorkId == workId, ct);
            if (attachment is null) return Results.NotFound();
            var content = await storage.OpenReadAsync(attachment.StorageKey, ct);
            return content is null ? Results.NotFound() : Results.File(content, attachment.ContentType, attachment.FileName);
        });

        group.MapPost("/", async (Guid workId, HttpRequest request, OperationsStore store,
            IAttachmentStorage storage, ITenantContext tenant, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await store.WorkItems.AnyAsync(x => x.Id == workId, ct)) return Results.NotFound();
            if (!request.HasFormContentType) return Results.Problem(statusCode: 400, title: "Multipart form data is required");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.Problem(statusCode: 400, title: "A file is required");
            if (file.Length > MaxLength) return Results.Problem(statusCode: 413, title: "Attachment exceeds the 25 MB limit");
            var contentType = file.ContentType.Trim().ToLowerInvariant();
            if (!AllowedTypes.Contains(contentType)) return Results.Problem(statusCode: 400, title: "Only PDF, JPEG, PNG, WebP, and HEIC files are supported");
            var fileName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
                return Results.Problem(statusCode: 400, title: "File name must contain 1 to 255 characters");
            if (!bool.TryParse(form["residentVisible"], out var residentVisible)) residentVisible = false;
            DateTimeOffset? retainUntil = null;
            if (!string.IsNullOrWhiteSpace(form["retainUntil"]))
            {
                if (!DateTimeOffset.TryParse(form["retainUntil"], out var parsed) || parsed < clock.GetUtcNow())
                    return Results.Problem(statusCode: 400, title: "retainUntil must be a future timestamp");
                retainUntil = parsed.ToUniversalTime();
            }

            var id = Guid.NewGuid();
            var storageKey = $"{tenant.OrganizationId:N}/{workId:N}/{id:N}{Extensions[contentType]}";
            Attachment attachment;
            try
            {
                await using var input = file.OpenReadStream();
                await storage.StoreAsync(storageKey, input, ct);
                attachment = Attachment.Create(tenant.OrganizationId, id, workId, fileName, contentType,
                file.Length, storageKey, residentVisible, clock.GetUtcNow(), retainUntil);
                store.Attachments.Add(attachment);
                await store.SaveChangesAsync(ct);
            }
            catch
            {
                await storage.DeleteAsync(storageKey, CancellationToken.None);
                throw;
            }
            return Results.Created($"/api/work/{workId}/attachments/{id}", ToResponse(attachment));
        }).RequireAuthorization(Capabilities.ManageAttachments);

        group.MapDelete("/{attachmentId:guid}", async (Guid workId, Guid attachmentId, OperationsStore store,
            IAttachmentStorage storage, CancellationToken ct) =>
        {
            var attachment = await store.Attachments.SingleOrDefaultAsync(x => x.Id == attachmentId && x.WorkId == workId, ct);
            if (attachment is null) return Results.NotFound();
            store.Attachments.Remove(attachment);
            await store.SaveChangesAsync(ct);
            await storage.DeleteAsync(attachment.StorageKey, ct);
            return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageAttachments);
    }

    private static AttachmentResponse ToResponse(Attachment attachment) => new(attachment.Id, attachment.FileName,
        attachment.ContentType, attachment.Length, attachment.ResidentVisible, attachment.CreatedAt, attachment.RetainUntil);
}

public sealed record AttachmentResponse(Guid Id, string FileName, string ContentType, long Length,
    bool ResidentVisible, DateTimeOffset CreatedAt, DateTimeOffset? RetainUntil);
