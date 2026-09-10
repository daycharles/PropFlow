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
            if (request.VendorId == Guid.Empty || request.Items is null || request.Items.Count is 0 or > 100) return Results.Problem(statusCode: 400, title: "Vendor and 1 to 100 work items are required");
            var summary = await work.BulkAssignVendorAsync(request.Items.Select(x => new BulkVendorAssignment(x.WorkId, x.Version)).ToArray(), request.VendorId, Actor(user), ct);
            return summary.Outcome switch
            {
                AssignmentOutcome.NotFound => Results.NotFound(),
                AssignmentOutcome.Conflict => Results.Problem(statusCode: 409, title: "One or more work items changed by another user"),
                _ => Results.Ok(new { summary.Changed, summary.Unchanged, summary.Total })
            };
        }).RequireAuthorization(Capabilities.AssignVendor);
    }
    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult AssignmentResult(AssignmentOutcome outcome) => outcome switch { AssignmentOutcome.NotFound => Results.NotFound(), AssignmentOutcome.Conflict => Results.Problem(statusCode: 409, title: "One or more work items changed by another user"), _ => Results.Ok(new { changed = outcome == AssignmentOutcome.Updated }) };
}

public sealed record WorkListRequest(string? Search, Guid? CategoryId, WorkStatus? Status, WorkPriority? Priority, Guid? PropertyId, Guid? SpaceId, string? Sort, bool Descending = false, int Page = 1, int PageSize = 25);
public sealed record WorkResponse(WorkItem Item, uint Version);
public sealed record AssignVendorRequest(Guid VendorId, uint? Version = null);
public sealed record AssignEmployeeRequest(Guid EmployeeId, uint? Version = null);
public sealed record BulkWorkVersion(Guid WorkId, uint Version);
public sealed record BulkAssignVendorRequest(Guid VendorId, List<BulkWorkVersion>? Items);
public sealed record CreateWorkRequest(string Title, Guid PropertyId, string? Description = null, WorkType WorkType = WorkType.WorkOrder, Guid? CategoryId = null, WorkPriority Priority = WorkPriority.Normal, Guid? BuildingId = null, Guid? SpaceId = null, Guid? ResidentId = null, DateTimeOffset? DueDate = null, decimal? Cost = null, string? InternalNotes = null, string? ResidentVisibleNotes = null)
{ public CreateWorkCommand ToCommand(Guid actor) => new(Title, PropertyId, actor, Description, WorkType, CategoryId, Priority, BuildingId, SpaceId, ResidentId, DueDate, Cost, InternalNotes, ResidentVisibleNotes); }
public sealed record UpdateWorkRequest(string Title, string? Description, Guid? CategoryId, WorkPriority Priority, Guid PropertyId, Guid? BuildingId, Guid? SpaceId, Guid? ResidentId, DateTimeOffset? DueDate, decimal? Cost, string? InternalNotes, string? ResidentVisibleNotes, WorkStatus? Status, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, uint Version)
{ public UpdateWorkCommand ToCommand(Guid actor) => new(Title, Description, CategoryId, Priority, PropertyId, BuildingId, SpaceId, ResidentId, DueDate, Cost, InternalNotes, ResidentVisibleNotes, Status, ScheduledStart, ScheduledEnd, Version, actor); }
