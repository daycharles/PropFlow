using PropFlow.Application;
using PropFlow.Application.Attention;

namespace PropFlow.Api;

public static class AttentionEndpoints
{
    public static void MapAttentionEndpoints(this WebApplication app)
    {
        // The actionable attention queue (PF-6.06): every open work item that trips a rule
        // (unassigned emergency, first-response SLA breach, overdue, waiting on vendor/resident,
        // repeat repair, unit-turn at risk), most urgent first, with per-severity counts for
        // the "Needs Your Attention" cards (PF-6.07).
        app.MapGet("/api/attention", async (IAttentionQueue queue, CancellationToken ct) =>
            Results.Ok(await queue.BuildAsync(ct)))
            .RequireAuthorization(Capabilities.ReadWork);
    }
}
