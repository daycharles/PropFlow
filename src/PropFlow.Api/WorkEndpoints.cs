using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Application.Work;
using PropFlow.Domain.Work;

namespace PropFlow.Api;

public static class WorkEndpoints
{
    public static void MapWorkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/work").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async ([AsParameters] WorkListRequest request, IWorkOperations work, CancellationToken ct) =>
        {
            var page = Math.Max(1, request.Page); var size = Math.Clamp(request.PageSize, 1, 100);
            var results = await work.ListAsync(new(request.Search, request.CategoryId, request.Status, request.Priority, request.PropertyId, request.SpaceId, request.Sort, request.Descending, page, size), ct);
            return Results.Ok(new { results.Items, results.TotalCount, results.Page, results.PageSize });
        });
        group.MapGet("/{id:guid}", async (Guid id, IWorkOperations work, CancellationToken ct) =>
            await work.GetAsync(id, ct) is { } item ? Results.Ok(new WorkResponse(item, (await work.VersionAsync(id, ct))!.Value)) : Results.NotFound());
        group.MapGet("/{id:guid}/timeline", async (Guid id, IWorkOperations work, CancellationToken ct) => await work.TimelineAsync(id, ct) is { } events ? Results.Ok(events) : Results.NotFound());
        group.MapPost("/", async (CreateWorkRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (request.PropertyId == Guid.Empty) return Results.Problem(statusCode: 400, title: "Property ID is required");
            try { var item = await work.CreateAsync(request.ToCommand(Actor(user)), ct); return Results.Created($"/api/work/{item.Id}", new WorkResponse(item, (await work.VersionAsync(item.Id, ct))!.Value)); }
            catch (KeyNotFoundException) { return Results.NotFound(); } catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.CreateWork);
        group.MapPut("/{id:guid}", async (Guid id, UpdateWorkRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (id == Guid.Empty || request.PropertyId == Guid.Empty) return Results.Problem(statusCode: 400, title: "Work and property IDs are required");
            try { var outcome = await work.UpdateAsync(id, request.ToCommand(Actor(user)), ct); return outcome switch { WorkWriteOutcome.NotFound => Results.NotFound(), WorkWriteOutcome.Conflict => Results.Problem(statusCode: 409, title: "Work item was changed by another user"), _ => Results.Ok(new WorkResponse((await work.GetAsync(id, ct))!, (await work.VersionAsync(id, ct))!.Value)) }; }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); } catch (InvalidOperationException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.UpdateWork);
        group.MapPost("/{id:guid}/vendor", async (Guid id, AssignVendorRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (id == Guid.Empty || request.VendorId == Guid.Empty) return Results.Problem(statusCode: 400, title: "Work and vendor IDs are required");
            var outcome = await work.AssignVendorAsync(id, request.VendorId, Actor(user), request.Version, ct); return AssignmentResult(outcome);
        }).RequireAuthorization(Capabilities.AssignVendor);
        group.MapPost("/{id:guid}/employee", async (Guid id, AssignEmployeeRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (id == Guid.Empty || request.EmployeeId == Guid.Empty) return Results.Problem(statusCode: 400, title: "Work and employee IDs are required");
            return AssignmentResult(await work.AssignEmployeeAsync(id, request.EmployeeId, Actor(user), request.Version, ct));
        }).RequireAuthorization(Capabilities.AssignEmployee);
        group.MapPost("/bulk/vendor", async (BulkAssignVendorRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (request.VendorId == Guid.Empty || !ValidBatch(request.Items)) return Results.Problem(statusCode: 400, title: "Vendor and 1 to 100 work items are required");
            var summary = await work.BulkAssignVendorAsync(Refs(request.Items!), request.VendorId, Actor(user), ct);
            return BulkResult(summary, TerminalTitle);
        }).RequireAuthorization(Capabilities.AssignVendor);

        // PF-4.07 — the rest of the bulk toolbar. Each is bounded (1–100), all-or-nothing, and
        // checks every item's client version; one timeline entry per item that actually changes.
        group.MapPost("/bulk/status", async (BulkStatusRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (!ValidBatch(request.Items)) return Results.Problem(statusCode: 400, title: "1 to 100 work items are required");
            if (request.Status is WorkStatus.Draft) return Results.Problem(statusCode: 400, title: "Work cannot be moved back to Draft");
            var summary = await work.BulkApplyAsync(new(BulkWorkAction.Status, Refs(request.Items!), Actor(user), Status: request.Status), ct);
            return BulkResult(summary, "Completed and cancelled work is terminal; reopen it first");
        }).RequireAuthorization(Capabilities.UpdateWork);

        group.MapPost("/bulk/priority", async (BulkPriorityRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (!ValidBatch(request.Items)) return Results.Problem(statusCode: 400, title: "1 to 100 work items are required");
            var summary = await work.BulkApplyAsync(new(BulkWorkAction.Priority, Refs(request.Items!), Actor(user), Priority: request.Priority), ct);
            return BulkResult(summary, "Completed and cancelled work is terminal; reopen it first");
        }).RequireAuthorization(Capabilities.UpdateWork);

        group.MapPost("/bulk/schedule", async (BulkScheduleRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (!ValidBatch(request.Items)) return Results.Problem(statusCode: 400, title: "1 to 100 work items are required");
            if (request.ScheduledEnd is { } end && end < request.ScheduledStart) return Results.Problem(statusCode: 400, title: "Schedule end must follow start");
            var summary = await work.BulkApplyAsync(new(BulkWorkAction.Schedule, Refs(request.Items!), Actor(user),
                ScheduledStart: request.ScheduledStart, ScheduledEnd: request.ScheduledEnd), ct);
            return BulkResult(summary, "Every item must be open and have a vendor or employee before it can be scheduled");
        }).RequireAuthorization(Capabilities.UpdateWork);

        group.MapPost("/bulk/note", async (BulkNoteRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (!ValidBatch(request.Items)) return Results.Problem(statusCode: 400, title: "1 to 100 work items are required");
            var note = request.Note?.Trim();
            if (string.IsNullOrEmpty(note) || note.Length > 2000) return Results.Problem(statusCode: 400, title: "Note must contain 1 to 2000 characters");
            var summary = await work.BulkApplyAsync(new(BulkWorkAction.Note, Refs(request.Items!), Actor(user), Note: note, NoteInternal: request.Internal), ct);
            return BulkResult(summary, "Note could not be added");
        }).RequireAuthorization(Capabilities.UpdateWork);

        group.MapPost("/bulk/reopen", async (BulkReopenRequest request, ClaimsPrincipal user, IWorkOperations work, CancellationToken ct) =>
        {
            if (!ValidBatch(request.Items)) return Results.Problem(statusCode: 400, title: "1 to 100 work items are required");
            var summary = await work.BulkApplyAsync(new(BulkWorkAction.Reopen, Refs(request.Items!), Actor(user)), ct);
            return BulkResult(summary, "Only completed or cancelled work can be reopened");
        }).RequireAuthorization(Capabilities.UpdateWork);
    }
    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private const string TerminalTitle = "Completed and cancelled work cannot be assigned";
    private static bool ValidBatch(List<BulkWorkVersion>? items) => items is { Count: > 0 and <= 100 };
    private static BulkWorkItemRef[] Refs(List<BulkWorkVersion> items) => items.Select(x => new BulkWorkItemRef(x.WorkId, x.Version)).ToArray();
    private static IResult BulkResult(BulkAssignmentSummary summary, string notAssignableTitle) => summary.Outcome switch
    {
        AssignmentOutcome.NotFound => Results.NotFound(),
        AssignmentOutcome.Conflict => Results.Problem(statusCode: 409, title: "One or more work items changed by another user"),
        AssignmentOutcome.NotAssignable => Results.Problem(statusCode: 400, title: notAssignableTitle),
        _ => Results.Ok(new { summary.Changed, summary.Unchanged, summary.Total })
    };
    private static IResult AssignmentResult(AssignmentOutcome outcome) => outcome switch { AssignmentOutcome.NotFound => Results.NotFound(), AssignmentOutcome.Conflict => Results.Problem(statusCode: 409, title: "One or more work items changed by another user"), AssignmentOutcome.NotAssignable => Results.Problem(statusCode: 400, title: TerminalTitle), _ => Results.Ok(new { changed = outcome == AssignmentOutcome.Updated }) };
}

public sealed record WorkListRequest(string? Search, Guid? CategoryId, WorkStatus? Status, WorkPriority? Priority, Guid? PropertyId, Guid? SpaceId, string? Sort, bool Descending = false, int Page = 1, int PageSize = 25);
public sealed record WorkResponse(WorkItem Item, uint Version);
public sealed record AssignVendorRequest(Guid VendorId, uint? Version = null);
public sealed record AssignEmployeeRequest(Guid EmployeeId, uint? Version = null);
public sealed record BulkWorkVersion(Guid WorkId, uint Version);
public sealed record BulkAssignVendorRequest(Guid VendorId, List<BulkWorkVersion>? Items);
public sealed record BulkStatusRequest(WorkStatus Status, List<BulkWorkVersion>? Items);
public sealed record BulkPriorityRequest(WorkPriority Priority, List<BulkWorkVersion>? Items);
public sealed record BulkScheduleRequest(DateTimeOffset ScheduledStart, DateTimeOffset? ScheduledEnd, List<BulkWorkVersion>? Items);
public sealed record BulkNoteRequest(string? Note, bool Internal, List<BulkWorkVersion>? Items);
public sealed record BulkReopenRequest(List<BulkWorkVersion>? Items);
public sealed record CreateWorkRequest(string Title, Guid PropertyId, string? Description = null, WorkType WorkType = WorkType.WorkOrder, Guid? CategoryId = null, WorkPriority Priority = WorkPriority.Normal, Guid? BuildingId = null, Guid? SpaceId = null, Guid? ResidentId = null, DateTimeOffset? DueDate = null, decimal? Cost = null, string? InternalNotes = null, string? ResidentVisibleNotes = null)
{ public CreateWorkCommand ToCommand(Guid actor) => new(Title, PropertyId, actor, Description, WorkType, CategoryId, Priority, BuildingId, SpaceId, ResidentId, DueDate, Cost, InternalNotes, ResidentVisibleNotes); }
public sealed record UpdateWorkRequest(string Title, string? Description, Guid? CategoryId, WorkPriority Priority, Guid PropertyId, Guid? BuildingId, Guid? SpaceId, Guid? ResidentId, DateTimeOffset? DueDate, decimal? Cost, string? InternalNotes, string? ResidentVisibleNotes, WorkStatus? Status, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, uint Version)
{ public UpdateWorkCommand ToCommand(Guid actor) => new(Title, Description, CategoryId, Priority, PropertyId, BuildingId, SpaceId, ResidentId, DueDate, Cost, InternalNotes, ResidentVisibleNotes, Status, ScheduledStart, ScheduledEnd, Version, actor); }
