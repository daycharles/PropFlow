using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Application.Work;

namespace PropFlow.Api;

public static class WorkEndpoints
{
    public static void MapWorkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/work").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async (IWorkOperations work, CancellationToken ct) => Results.Ok(await work.ListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid id, IWorkOperations work, CancellationToken ct) =>
            await work.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());
        group.MapGet("/{id:guid}/timeline", async (Guid id, IWorkOperations work, CancellationToken ct) =>
            await work.TimelineAsync(id, ct) is { } events ? Results.Ok(events) : Results.NotFound());
        group.MapPost("/{id:guid}/vendor", async (Guid id, AssignVendorRequest request, ClaimsPrincipal user,
            IWorkOperations work, CancellationToken ct) =>
        {
            if (id == Guid.Empty || request.VendorId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Work and vendor IDs are required");
            var outcome = await work.AssignVendorAsync(id, request.VendorId,
                Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct);
            return outcome == AssignmentOutcome.NotFound ? Results.NotFound()
                : Results.Ok(new { changed = outcome == AssignmentOutcome.Updated });
        }).RequireAuthorization(Capabilities.AssignVendor);
    }
}
// Tenant and actor come exclusively from the verified session.
public sealed record AssignVendorRequest(Guid VendorId);
