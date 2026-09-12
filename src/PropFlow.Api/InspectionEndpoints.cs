using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class InspectionEndpoints
{
    public static void MapInspectionEndpoints(this WebApplication app)
    {
        var templates = app.MapGroup("/api/inspection-templates").RequireAuthorization(Capabilities.ReadWork);
        templates.MapGet("/", async (OperationsStore db, CancellationToken ct) => Results.Ok(await db.InspectionTemplates.AsNoTracking().Where(x => !x.IsArchived).OrderBy(x => x.Name).ThenByDescending(x => x.Version).ToListAsync(ct)));
        templates.MapPost("/", async (TemplateRequest request, ClaimsPrincipal user, OperationsStore db, TimeProvider clock, CancellationToken ct) =>
        {
            try { var version = (await db.InspectionTemplates.Where(x => x.Name == request.Name).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1; var item = new InspectionTemplate(db.OrganizationId, Guid.NewGuid(), request.Name, version, request.Checklist, Actor(user), clock.GetUtcNow()); db.InspectionTemplates.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/inspection-templates/{item.Id}", item); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.UpdateWork);
        templates.MapPost("/{id:guid}/archive", async (Guid id, OperationsStore db, CancellationToken ct) => { var item = await db.InspectionTemplates.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); item.Archive(); await db.SaveChangesAsync(ct); return Results.Ok(item); }).RequireAuthorization(Capabilities.UpdateWork);

        var inspections = app.MapGroup("/api/inspections").RequireAuthorization(Capabilities.ReadWork);
        inspections.MapGet("/", async (Guid? propertyId, InspectionStatus? status, OperationsStore db, CancellationToken ct) => { var q = db.Inspections.AsNoTracking().AsQueryable(); if (propertyId is { } p) q = q.Where(x => x.PropertyId == p); if (status is { } s) q = q.Where(x => x.Status == s); return Results.Ok(await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct)); });
        inspections.MapGet("/{id:guid}", async (Guid id, OperationsStore db, CancellationToken ct) => { var item = await db.Inspections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); return item is null ? Results.NotFound() : Results.Ok(new { item, findings = await db.InspectionFindings.AsNoTracking().Where(x => x.InspectionId == id).OrderBy(x => x.Area).ToListAsync(ct) }); });
        inspections.MapPost("/", async (CreateInspectionRequest request, ClaimsPrincipal user, OperationsStore db, CancellationToken ct) =>
        {
            if (!await db.Properties.AnyAsync(x => x.Id == request.PropertyId, ct) || !await db.InspectionTemplates.AnyAsync(x => x.Id == request.TemplateId && !x.IsArchived, ct)) return Results.NotFound();
            try { var item = new Inspection(db.OrganizationId, Guid.NewGuid(), request.PropertyId, request.SpaceId, request.TemplateId, request.Kind, Actor(user), DateTimeOffset.UtcNow); db.Inspections.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/inspections/{item.Id}", item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.CreateWork);
        inspections.MapPost("/{id:guid}/start", async (Guid id, OperationsStore db, CancellationToken ct) => await Change(id, db, ct, x => x.Start()));
        inspections.MapPost("/{id:guid}/complete", async (Guid id, OperationsStore db, TimeProvider clock, CancellationToken ct) => await Change(id, db, ct, x => x.Complete(clock.GetUtcNow()))).RequireAuthorization(Capabilities.UpdateWork);
        inspections.MapPost("/{id:guid}/approve", async (Guid id, OperationsStore db, TimeProvider clock, CancellationToken ct) => await Change(id, db, ct, x => x.Approve(clock.GetUtcNow()))).RequireAuthorization(Capabilities.UpdateWork);
        inspections.MapPost("/{id:guid}/findings", async (Guid id, FindingRequest request, OperationsStore db, CancellationToken ct) => { if (!await db.Inspections.AnyAsync(x => x.Id == id, ct)) return Results.NotFound(); try { var item = new InspectionFinding(db.OrganizationId, Guid.NewGuid(), id, request.Area, request.Description, request.Severity); item.SetPhotos(request.PhotoAttachmentIds ?? []); db.InspectionFindings.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/inspections/{id}/findings/{item.Id}", item); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } }).RequireAuthorization(Capabilities.UpdateWork);
        inspections.MapPost("/{inspectionId:guid}/findings/{findingId:guid}/resolve", async (Guid inspectionId, Guid findingId, OperationsStore db, TimeProvider clock, CancellationToken ct) => { var item = await db.InspectionFindings.SingleOrDefaultAsync(x => x.Id == findingId && x.InspectionId == inspectionId, ct); if (item is null) return Results.NotFound(); try { item.Resolve(clock.GetUtcNow()); await db.SaveChangesAsync(ct); return Results.Ok(item); } catch (InvalidOperationException e) { return Results.Problem(statusCode: 400, title: e.Message); } }).RequireAuthorization(Capabilities.UpdateWork);

        var turns = app.MapGroup("/api/unit-turns").RequireAuthorization(Capabilities.ReadWork);
        turns.MapGet("/", async (Guid? propertyId, OperationsStore db, CancellationToken ct) => { var q = db.UnitTurns.AsNoTracking().AsQueryable(); if (propertyId is { } p) q = q.Where(x => x.PropertyId == p); return Results.Ok(await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct)); });
        turns.MapGet("/{id:guid}", async (Guid id, OperationsStore db, CancellationToken ct) => { var turn = await db.UnitTurns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); return turn is null ? Results.NotFound() : Results.Ok(new { turn, tasks = await db.UnitTurnTasks.AsNoTracking().Where(x => x.TurnId == id).OrderBy(x => x.Sequence).ToListAsync(ct) }); });
        turns.MapPost("/", async (CreateTurnRequest request, ClaimsPrincipal user, OperationsStore db, TimeProvider clock, CancellationToken ct) =>
        {
            var inspection = await db.Inspections.SingleOrDefaultAsync(x => x.Id == request.MoveOutInspectionId && x.Kind == InspectionKind.MoveOut && (x.Status == InspectionStatus.Completed || x.Status == InspectionStatus.Approved), ct);
            if (inspection is null || inspection.SpaceId != request.SpaceId) return Results.BadRequest(new { error = "An approved or completed move-out inspection for the space is required." });
            try { var turn = new UnitTurn(db.OrganizationId, Guid.NewGuid(), inspection.PropertyId, request.SpaceId, inspection.Id, request.TargetReadyOn, Actor(user), clock.GetUtcNow()); db.UnitTurns.Add(turn); var findings = await db.InspectionFindings.Where(x => x.InspectionId == inspection.Id && x.Status == FindingStatus.Open).OrderBy(x => x.Id).ToListAsync(ct); var seq = 1; foreach (var finding in findings) { var work = new WorkItem(db.OrganizationId, Guid.NewGuid(), $"Make-ready: {finding.Area} - {finding.Description}", inspection.PropertyId, Actor(user), WorkType.UnitTurnTask); work.SetDueDate(request.TargetReadyOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); work.Publish(clock.GetUtcNow()); db.WorkItems.Add(work); db.UnitTurnTasks.Add(new UnitTurnTask(db.OrganizationId, Guid.NewGuid(), turn.Id, $"{finding.Area}: {finding.Description}", work.Id, seq++)); } await db.SaveChangesAsync(ct); return Results.Created($"/api/unit-turns/{turn.Id}", new { turn, generatedTasks = seq - 1 }); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.CreateWork);
        turns.MapPost("/{id:guid}/start", async (Guid id, OperationsStore db, CancellationToken ct) => await ChangeTurn(id, db, ct, x => x.Start())).RequireAuthorization(Capabilities.UpdateWork);
        turns.MapPost("/{id:guid}/ready", async (Guid id, OperationsStore db, TimeProvider clock, CancellationToken ct) => { var turn = await db.UnitTurns.SingleOrDefaultAsync(x => x.Id == id, ct); if (turn is null) return Results.NotFound(); var tasks = await db.UnitTurnTasks.Where(x => x.TurnId == id).ToListAsync(ct); if (tasks.Any(x => x.Status != TurnTaskStatus.Completed)) return Results.Conflict(new { error = "All make-ready tasks must be completed before the unit is ready." }); try { turn.MarkReady(clock.GetUtcNow()); await db.SaveChangesAsync(ct); return Results.Ok(turn); } catch (InvalidOperationException e) { return Results.Problem(statusCode: 400, title: e.Message); } }).RequireAuthorization(Capabilities.UpdateWork);
        turns.MapPost("/{turnId:guid}/tasks/{taskId:guid}/status", async (Guid turnId, Guid taskId, TaskStatusRequest request, OperationsStore db, CancellationToken ct) => { var task = await db.UnitTurnTasks.SingleOrDefaultAsync(x => x.Id == taskId && x.TurnId == turnId, ct); if (task is null) return Results.NotFound(); if (!Enum.IsDefined(request.Status)) return Results.BadRequest(); task.ChangeStatus(request.Status); await db.SaveChangesAsync(ct); return Results.Ok(task); }).RequireAuthorization(Capabilities.UpdateWork);
    }
    private static Guid Actor(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new InvalidOperationException("Authenticated actor is required.");
    private static async Task<IResult> Change(Guid id, OperationsStore db, CancellationToken ct, Action<Inspection> action) { var item = await db.Inspections.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); try { action(item); await db.SaveChangesAsync(ct); return Results.Ok(item); } catch (InvalidOperationException e) { return Results.Problem(statusCode: 400, title: e.Message); } }
    private static async Task<IResult> ChangeTurn(Guid id, OperationsStore db, CancellationToken ct, Action<UnitTurn> action) { var item = await db.UnitTurns.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); try { action(item); await db.SaveChangesAsync(ct); return Results.Ok(item); } catch (InvalidOperationException e) { return Results.Problem(statusCode: 400, title: e.Message); } }
}
public sealed record TemplateRequest(string Name, List<string> Checklist);
public sealed record CreateInspectionRequest(Guid PropertyId, Guid? SpaceId, Guid TemplateId, InspectionKind Kind);
public sealed record FindingRequest(string Area, string Description, FindingSeverity Severity, List<Guid>? PhotoAttachmentIds = null);
public sealed record CreateTurnRequest(Guid SpaceId, Guid MoveOutInspectionId, DateOnly TargetReadyOn);
public sealed record TaskStatusRequest(TurnTaskStatus Status);
