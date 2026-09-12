using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Approvals;
using PropFlow.Domain.Timeline;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// PF-S03.05. A generic, single-decision approval primitive - see ApprovalRequest for what it is
// and is not. v1 keeps the capability model simple since nothing concrete consumes this yet:
// any authenticated staff member (Work.Read, the baseline every role above Technician/Vendor
// carries) can request or read; deciding needs Settings.ManageConfiguration. A future feature
// that wires a real subject type into this will very likely want its own, narrower capability
// for requesting - this is deliberately not that yet.
public static class ApprovalEndpoints
{
    public static void MapApprovalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/approvals").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/", async (string? subjectType, Guid? subjectId, ApprovalStatus? status,
            OperationsStore store, CancellationToken ct) =>
        {
            var query = store.ApprovalRequests.AsNoTracking().AsQueryable();
            if (subjectType is not null) query = query.Where(x => x.SubjectType == subjectType);
            if (subjectId is not null) query = query.Where(x => x.SubjectId == subjectId);
            if (status is not null) query = query.Where(x => x.Status == status);
            var results = await query.OrderByDescending(x => x.RequestedAt).Take(200).ToListAsync(ct);
            return Results.Ok(results.Select(ToResponse).ToList());
        });

        group.MapPost("/", async (ApprovalRequestBody request, ClaimsPrincipal user, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            try
            {
                var actorId = WorkScopeAccess.Actor(user);
                var now = clock.GetUtcNow();
                var approval = ApprovalRequest.Create(store.OrganizationId, Guid.NewGuid(), request.SubjectType, request.SubjectId, actorId, request.Note, now);
                store.ApprovalRequests.Add(approval);
                store.Timeline.Add(TimelineEntry.Record(store.OrganizationId, actorId, now, "ApprovalRequested",
                    "ApprovalRequest", approval.Id, null, approval.SubjectType,
                    changes: JsonSerializer.Serialize(new { subjectType = approval.SubjectType, subjectId = approval.SubjectId, note = approval.Note })));
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/approvals/{approval.Id}", ToResponse(approval));
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });

        group.MapPost("/{id:guid}/decide", async (Guid id, DecideApprovalRequest request, ClaimsPrincipal user,
            OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var approval = await store.ApprovalRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (approval is null) return Results.NotFound();
            try
            {
                var actorId = WorkScopeAccess.Actor(user);
                var now = clock.GetUtcNow();
                var from = approval.Status;
                if (request.Approved) approval.Approve(actorId, now, request.Reason);
                else approval.Reject(actorId, now, request.Reason);
                store.Timeline.Add(TimelineEntry.Record(store.OrganizationId, actorId, now, "ApprovalDecided",
                    "ApprovalRequest", approval.Id, from.ToString(), approval.Status.ToString(),
                    changes: JsonSerializer.Serialize(new { subjectType = approval.SubjectType, subjectId = approval.SubjectId, reason = approval.DecisionReason })));
                await store.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(approval));
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
            catch (InvalidOperationException e) { return Results.Problem(statusCode: 409, title: e.Message); }
        }).RequireAuthorization(Capabilities.ManageConfiguration);
    }

    private static ApprovalResponse ToResponse(ApprovalRequest x) => new(x.Id, x.SubjectType, x.SubjectId, x.RequestedBy,
        x.Note, x.Status, x.RequestedAt, x.DecidedBy, x.DecidedAt, x.DecisionReason);
}

public sealed record ApprovalRequestBody(string SubjectType, Guid SubjectId, string? Note);
public sealed record DecideApprovalRequest(bool Approved, string? Reason);
public sealed record ApprovalResponse(Guid Id, string SubjectType, Guid SubjectId, Guid RequestedBy, string? Note,
    ApprovalStatus Status, DateTimeOffset RequestedAt, Guid? DecidedBy, DateTimeOffset? DecidedAt, string? DecisionReason);
