using PropFlow.Application;
using PropFlow.Application.Integrations;

namespace PropFlow.Api;

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/integrations").RequireAuthorization(Capabilities.ManageIntegrations);

        // The adapters this deployment knows how to talk to — the choices for a new connection.
        group.MapGet("/sources", (IIntegrationCatalog catalog) =>
            Results.Ok(catalog.Available.Select(a => new { sourceSystem = a.SourceSystem, displayName = a.DisplayName })));

        group.MapGet("/", async (IIntegrationOperations operations, CancellationToken ct) =>
            Results.Ok(await operations.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, IIntegrationOperations operations, CancellationToken ct) =>
            await operations.GetAsync(id, ct) is { } health ? Results.Ok(health) : Results.NotFound());

        group.MapGet("/{id:guid}/records", async (Guid id, int? page, int? pageSize, IIntegrationOperations operations, CancellationToken ct) =>
            await operations.RecordsAsync(id, page ?? 1, pageSize ?? 50, ct) is { } records ? Results.Ok(records) : Results.NotFound());

        group.MapPost("/", async (CreateConnectionRequest request, IIntegrationOperations operations, CancellationToken ct) =>
        {
            var command = new CreateConnectionCommand(request.SourceSystem ?? "", request.DisplayName ?? "");
            IntegrationWriteOutcome outcome;
            Guid newId;
            try
            {
                (outcome, newId) = await operations.CreateAsync(command, ct);
            }
            catch (ArgumentException exception)
            {
                return Results.Problem(statusCode: 400, title: exception.Message);
            }

            return outcome switch
            {
                IntegrationWriteOutcome.Created => Results.Created($"/api/integrations/{newId}", new { id = newId }),
                IntegrationWriteOutcome.UnknownSource => Results.Problem(statusCode: 400, title: "No adapter is registered for that source system"),
                IntegrationWriteOutcome.DuplicateSource => Results.Problem(statusCode: 409, title: "A connection to that source system already exists"),
                _ => Results.Problem(statusCode: 400, title: "The connection could not be created")
            };
        });

        group.MapPost("/{id:guid}/enable", (Guid id, IIntegrationOperations operations, CancellationToken ct) =>
            SetEnabled(id, true, operations, ct));

        group.MapPost("/{id:guid}/disable", (Guid id, IIntegrationOperations operations, CancellationToken ct) =>
            SetEnabled(id, false, operations, ct));

        group.MapPost("/{id:guid}/sync", async (Guid id, IIntegrationOperations operations, CancellationToken ct) =>
        {
            var report = await operations.SyncAsync(id, ct);
            return report.Outcome switch
            {
                SyncOutcome.NotFound => Results.NotFound(),
                SyncOutcome.Disabled => Results.Problem(statusCode: 409, title: "The connection is disabled"),
                // Another dispatcher holds the run claim. A conflict, not an error: the sync the
                // caller asked for is already happening.
                SyncOutcome.AlreadyRunning => Results.Problem(statusCode: 409, title: "A sync is already running"),
                _ => Results.Ok(report)
            };
        });
    }

    private static async Task<IResult> SetEnabled(Guid id, bool enabled, IIntegrationOperations operations, CancellationToken ct)
    {
        var outcome = await operations.SetEnabledAsync(id, enabled, ct);
        return outcome == IntegrationWriteOutcome.NotFound ? Results.NotFound() : Results.NoContent();
    }
}

// Tenant comes from the verified session.
public sealed record CreateConnectionRequest(string? SourceSystem, string? DisplayName);
