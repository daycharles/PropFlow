using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;

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

        MapConflicts(group);
        MapMappings(group);
        MapRuns(group);
        MapRecords(group);
    }

    // --- The conflict queue -------------------------------------------------------------------

    private static void MapConflicts(RouteGroupBuilder group)
    {
        // status / kind / reason are what make a queue workable: an operator filters to the open
        // unmapped-value conflicts, fixes the rule, and comes back.
        group.MapGet("/{id:guid}/conflicts", async (Guid id, string? status, string? kind, string? reason,
            int? page, int? pageSize, IIntegrationAdministration admin, CancellationToken ct) =>
        {
            if (!TryParseFilter(status, out ConflictStatus? parsedStatus))
                return Results.Problem(statusCode: 400, title: $"'{status}' is not a conflict status");
            if (!TryParseFilter(kind, out IntegrationEntityKind? parsedKind))
                return Results.Problem(statusCode: 400, title: $"'{kind}' is not an entity kind");
            if (!TryParseFilter(reason, out ConflictReason? parsedReason))
                return Results.Problem(statusCode: 400, title: $"'{reason}' is not a conflict reason");

            var filter = new ConflictFilter(parsedStatus, parsedKind, parsedReason);
            return await admin.ConflictsAsync(id, filter, page ?? 1, pageSize ?? 50, ct) is { } conflicts
                ? Results.Ok(conflicts)
                : Results.NotFound();
        });

        // Resolution is a status transition. There is deliberately no DELETE route, because there is
        // no delete: the runtime role has no DELETE grant on Conflicts, so a resolved conflict stays
        // as the audit fact that a human looked at a divergence and made a call.
        group.MapPost("/{id:guid}/conflicts/{conflictId:guid}/resolve", (Guid id, Guid conflictId,
            CloseConflictRequest? request, ClaimsPrincipal user, IIntegrationAdministration admin, CancellationToken ct) =>
            CloseConflict(id, conflictId, request, user, admin, ignore: false, ct));

        group.MapPost("/{id:guid}/conflicts/{conflictId:guid}/ignore", (Guid id, Guid conflictId,
            CloseConflictRequest? request, ClaimsPrincipal user, IIntegrationAdministration admin, CancellationToken ct) =>
            CloseConflict(id, conflictId, request, user, admin, ignore: true, ct));
    }

    private static async Task<IResult> CloseConflict(Guid id, Guid conflictId, CloseConflictRequest? request,
        ClaimsPrincipal user, IIntegrationAdministration admin, bool ignore, CancellationToken ct)
    {
        var outcome = await admin.CloseConflictAsync(id, conflictId, Actor(user), request?.Note, ignore, ct);
        return outcome switch
        {
            ConflictWriteOutcome.NotFound => Results.NotFound(),
            // A refused domain transition, not a caller input error.
            ConflictWriteOutcome.AlreadyClosed => Results.Problem(statusCode: 409,
                title: "The conflict is already resolved or ignored"),
            _ => Results.NoContent()
        };
    }

    // --- Mapping profiles and rules -------------------------------------------------------------

    private static void MapMappings(RouteGroupBuilder group)
    {
        // Every profile for the connection, each carrying its rules AND its MappingIssue list, so
        // the panel can render "what is wrong and why" without a second round-trip.
        group.MapGet("/{id:guid}/mappings", async (Guid id, IIntegrationAdministration admin, CancellationToken ct) =>
            await admin.MappingProfilesAsync(id, ct) is { } profiles ? Results.Ok(profiles) : Results.NotFound());

        // Upsert keyed on (connection, kind) — the same key as the unique index, so it cannot
        // collide with itself.
        group.MapPut("/{id:guid}/mappings/{kind}", async (Guid id, string kind, SaveMappingProfileRequest request,
            IIntegrationAdministration admin, CancellationToken ct) =>
        {
            if (!TryParseKind(kind, out var parsed)) return UnknownKind(kind);
            var command = new SaveMappingProfileCommand(request.TargetPortfolioId, request.DefaultCreatorId,
                request.DefaultTimeZoneId);
            var (outcome, profileId) = await admin.SaveMappingProfileAsync(id, parsed, command, ct);
            return outcome switch
            {
                MappingWriteOutcome.NotFound => Results.NotFound(),
                MappingWriteOutcome.Created => Results.Created($"/api/integrations/{id}/mappings", new { id = profileId }),
                _ => Results.Ok(new { id = profileId })
            };
        });

        group.MapPost("/{id:guid}/mappings/{kind}/rules", async (Guid id, string kind, SaveMappingRuleRequest request,
            IIntegrationAdministration admin, CancellationToken ct) =>
        {
            if (!TryParseKind(kind, out var parsedKind)) return UnknownKind(kind);
            if (!Enum.TryParse<MappingSourceField>(request.SourceField, ignoreCase: true, out var field)
                || !Enum.IsDefined(field))
                return Results.Problem(statusCode: 400, title: $"'{request.SourceField}' is not a mapping source field");

            var command = new SaveMappingRuleCommand(field, request.SourceValue ?? "", request.TargetValue ?? "");
            // A blank or over-long value throws ArgumentException out of the MappingRule
            // constructor, and ApiExceptionHandler.cs:20 already maps that to 400 — so it is
            // deliberately not caught here.
            var (outcome, ruleId) = await admin.AddMappingRuleAsync(id, parsedKind, command, ct);
            return outcome switch
            {
                MappingWriteOutcome.NotFound => Results.NotFound(),
                MappingWriteOutcome.NotApplicable => Results.Problem(statusCode: 400,
                    title: $"A {field} rule does not apply to a {parsedKind} profile"),
                _ => Results.Created($"/api/integrations/{id}/mappings", new { id = ruleId })
            };
        });

        group.MapDelete("/{id:guid}/mappings/{kind}/rules/{ruleId:guid}", async (Guid id, string kind, Guid ruleId,
            IIntegrationAdministration admin, CancellationToken ct) =>
        {
            if (!TryParseKind(kind, out var parsed)) return UnknownKind(kind);
            var outcome = await admin.RemoveMappingRuleAsync(id, parsed, ruleId, ct);
            return outcome == MappingWriteOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        });

        // The guard rail. Refuses while Validate() reports an error and hands back the whole issue
        // list, so an operator sees exactly what to fix rather than a bare "no".
        group.MapPost("/{id:guid}/mappings/{kind}/promote", async (Guid id, string kind,
            IIntegrationAdministration admin, CancellationToken ct) =>
        {
            if (!TryParseKind(kind, out var parsed)) return UnknownKind(kind);
            var (outcome, issues) = await admin.PromoteAsync(id, parsed, ct);
            return outcome switch
            {
                MappingPromotionOutcome.NotFound => Results.NotFound(),
                MappingPromotionOutcome.HasErrors => Results.Problem(statusCode: 409,
                    title: "The mapping profile has validation errors and cannot auto-apply",
                    extensions: new Dictionary<string, object?> { ["issues"] = issues }),
                _ => Results.Ok(new { mode = MappingMode.AutoApply, issues })
            };
        });

        group.MapPost("/{id:guid}/mappings/{kind}/report-only", async (Guid id, string kind,
            IIntegrationAdministration admin, CancellationToken ct) =>
        {
            if (!TryParseKind(kind, out var parsed)) return UnknownKind(kind);
            var outcome = await admin.RevertToReportOnlyAsync(id, parsed, ct);
            return outcome == MappingWriteOutcome.NotFound ? Results.NotFound() : Results.NoContent();
        });
    }

    // --- Run history ------------------------------------------------------------------------------

    private static void MapRuns(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/runs", async (Guid id, int? page, int? pageSize,
            IIntegrationAdministration admin, CancellationToken ct) =>
            await admin.RunsAsync(id, page ?? 1, pageSize ?? 25, ct) is { } runs
                ? Results.Ok(runs) : Results.NotFound());

        group.MapGet("/{id:guid}/runs/{runId:guid}", async (Guid id, Guid runId,
            IIntegrationAdministration admin, CancellationToken ct) =>
            await admin.RunAsync(id, runId, ct) is { } run ? Results.Ok(run) : Results.NotFound());
    }

    // --- Manual retirement --------------------------------------------------------------------------

    private static void MapRecords(RouteGroupBuilder group)
    {
        // Operator-invoked, so the actor is recorded rather than a run id — there is no run. That
        // is why ExternalRecordLink carries RetiredByUserId alongside RetiredAt.
        group.MapPost("/{id:guid}/records/{recordId:guid}/retire", async (Guid id, Guid recordId,
            ClaimsPrincipal user, IIntegrationAdministration admin, CancellationToken ct) =>
        {
            var outcome = await admin.RetireRecordAsync(id, recordId, Actor(user), ct);
            return outcome switch
            {
                RecordRetireOutcome.NotFound => Results.NotFound(),
                RecordRetireOutcome.AlreadyRetired => Results.Problem(statusCode: 409,
                    title: "The record link is already retired"),
                _ => Results.NoContent()
            };
        });
    }

    private static async Task<IResult> SetEnabled(Guid id, bool enabled, IIntegrationOperations operations, CancellationToken ct)
    {
        var outcome = await operations.SetEnabledAsync(id, enabled, ct);
        return outcome == IntegrationWriteOutcome.NotFound ? Results.NotFound() : Results.NoContent();
    }

    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Entity kind travels in the route as its name ("Property", "WorkOrder"). Parsed explicitly so
    // an unknown one is a 400 naming the value rather than a route-binding failure.
    private static bool TryParseKind(string? value, out IntegrationEntityKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind);

    private static IResult UnknownKind(string? value) =>
        Results.Problem(statusCode: 400, title: $"'{value}' is not an integration entity kind");

    // An absent query-string filter means "no filter"; a present but unparseable one is a 400
    // rather than being silently ignored, which would show the operator a queue they did not ask
    // for and let them close conflicts they never meant to see.
    private static bool TryParseFilter<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var result) || !Enum.IsDefined(result)) return false;
        parsed = result;
        return true;
    }
}

// Tenant comes from the verified session.
public sealed record CreateConnectionRequest(string? SourceSystem, string? DisplayName);

// The actor is taken from the principal, never the body.
public sealed record CloseConflictRequest(string? Note);

public sealed record SaveMappingProfileRequest(Guid? TargetPortfolioId, Guid? DefaultCreatorId, string? DefaultTimeZoneId);

public sealed record SaveMappingRuleRequest(string? SourceField, string? SourceValue, string? TargetValue);
