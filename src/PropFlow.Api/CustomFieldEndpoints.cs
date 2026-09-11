using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Configuration;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// PF-S03.01 — custom field definitions (FS-S03). Values on a work item are PF-S03.02; this is
// the definition CRUD only.
public static class CustomFieldEndpoints
{
    public static void MapCustomFieldEndpoints(this WebApplication app)
    {
        // Reads sit behind Work.Read today because AppliesTo is closed to WorkItem — revisit the
        // gate when a second AppliesTo value needs its own readers.
        var group = app.MapGroup("/api/settings/custom-fields").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/", async (OperationsStore store, CancellationToken ct) =>
            Results.Ok((await store.CustomFieldDefinitions.AsNoTracking()
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct))
                .Select(ToResponse).ToList()));

        group.MapPost("/", async (CustomFieldRequest request, OperationsStore store, CancellationToken ct) =>
        {
            try
            {
                var field = new CustomFieldDefinition(store.OrganizationId, Guid.NewGuid(), request.Key, request.Name,
                    request.AppliesTo, request.FieldType, request.Options, request.IsRequired, request.SortOrder, DateTimeOffset.UtcNow);
                store.CustomFieldDefinitions.Add(field);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/settings/custom-fields/{field.Id}", ToResponse(field));
            }
            catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: ex.Message); }
            catch (DbUpdateException) { return Results.Problem(statusCode: 409, title: "A field with this key already exists for this entity type"); }
        }).RequireAuthorization(Capabilities.ManageConfiguration);

        group.MapPut("/{id:guid}", async (Guid id, CustomFieldUpdateRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var field = await store.CustomFieldDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (field is null) return Results.NotFound();
            try
            {
                field.Rename(request.Name);
                field.Reorder(request.SortOrder);
                field.SetRequired(request.IsRequired);
                field.ReplaceOptions(request.Options);
                await store.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(field));
            }
            catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: ex.Message); }
        }).RequireAuthorization(Capabilities.ManageConfiguration);

        group.MapPost("/{id:guid}/archive", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var field = await store.CustomFieldDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (field is null) return Results.NotFound();
            field.Archive();
            await store.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Capabilities.ManageConfiguration);
    }

    private static CustomFieldResponse ToResponse(CustomFieldDefinition x) => new(x.Id, x.Key, x.Name, x.AppliesTo,
        x.FieldType, x.ReadOptions(), x.IsRequired, x.SortOrder, x.IsArchived, x.CreatedAt);
}

public sealed record CustomFieldRequest(string Key, string Name, CustomFieldAppliesTo AppliesTo,
    CustomFieldType FieldType, IReadOnlyList<string>? Options, bool IsRequired, int SortOrder);
public sealed record CustomFieldUpdateRequest(string Name, IReadOnlyList<string>? Options, bool IsRequired, int SortOrder);
public sealed record CustomFieldResponse(Guid Id, string Key, string Name, CustomFieldAppliesTo AppliesTo,
    CustomFieldType FieldType, IReadOnlyList<string> Options, bool IsRequired, int SortOrder, bool IsArchived, DateTimeOffset CreatedAt);
