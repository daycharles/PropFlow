using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Marketing;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// FS-S05 approve/deny and the decision trail.
//
// The approve-over-Fail override rule is a domain refusal (RentalApplication.Approve), not an
// endpoint check, and is deliberately NOT re-implemented here. The endpoint's only job is to
// gather the screening recommendations the domain needs to judge, and to make sure the refusal
// reaches the caller as a 409 whose body says why: "Approving over a failed screening requires
// an override note." is actionable, "Conflict" is not.
public static class ApplicationDecisionEndpoints
{
    public static void MapApplicationDecisionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/applications").RequireAuthorization(Capabilities.ManageApplications);

        group.MapPost("/{id:guid}/approve", async (Guid id, DecisionRequest request, ClaimsPrincipal user,
            OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var recommendations = await RecommendationsAsync(store, id, ct);
            return await DecideAsync(id, store, user, ct, (application, actor) =>
                application.Approve(actor, clock.GetUtcNow(), request.Reason, request.Note, recommendations));
        });

        group.MapPost("/{id:guid}/deny", async (Guid id, DecisionRequest request, ClaimsPrincipal user,
            OperationsStore store, TimeProvider clock, CancellationToken ct) =>
            await DecideAsync(id, store, user, ct, (application, actor) =>
                application.Deny(actor, clock.GetUtcNow(), request.Reason, request.Note)));

        // Append-only, so this is the whole trail rather than the current outcome: a denial is
        // adverse-action evidence and the reason code is what an applicant is entitled to be told.
        group.MapGet("/{id:guid}/decisions", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.RentalApplications.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await store.ApplicationDecisions.AsNoTracking().Where(x => x.ApplicationId == id)
                .OrderByDescending(x => x.DecidedAt).ToListAsync(ct));
        });
    }

    // The latest verdict per screening request, which is what the domain judges the override
    // against. Results are append-only, so a revised verdict is a later row and the newest wins.
    private static async Task<IReadOnlyCollection<ScreeningRecommendation>> RecommendationsAsync(
        OperationsStore store, Guid applicationId, CancellationToken ct) =>
        [.. (await ApplicationEndpoints.ScreeningRowsAsync(store, applicationId, ct))
            .Where(x => x.Recommendation is not null)
            .Select(x => x.Recommendation!.Value)];

    private static async Task<IResult> DecideAsync(Guid id, OperationsStore store, ClaimsPrincipal user,
        CancellationToken ct, Func<RentalApplication, Guid, ApplicationDecision> decide)
    {
        var application = await store.RentalApplications.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (application is null) return Results.NotFound();
        ApplicationDecision decision;
        try
        {
            decision = decide(application, ApplicationEndpoints.Actor(user));
            store.ApplicationDecisions.Add(decision);
            await store.SaveChangesAsync(ct);
        }
        // Carries the domain's own message, including the override-note refusal.
        catch (InvalidOperationException exception) { return ApplicationEndpoints.Conflict(exception); }
        catch (DbUpdateConcurrencyException) { return ApplicationEndpoints.Stale(); }
        return Results.Created($"/api/applications/{id}/decisions", new { decision, applicationStatus = application.Status });
    }
}

public sealed record DecisionRequest(string Reason, string? Note);
