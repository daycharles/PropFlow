using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Application.Compliance;
using PropFlow.Domain.Properties;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class ComplianceEndpoints
{
    public static void MapComplianceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compliance").RequireAuthorization(Capabilities.ReadWork);

        group.MapGet("/obligations", async (Guid? propertyId, ComplianceObligationStatus? status,
            bool? overdueOnly, DateOnly? asOf, int? page, int? pageSize, OperationsStore store, CancellationToken ct) =>
        {
            var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var query = store.ComplianceObligations.AsNoTracking();
            if (propertyId is { } property) query = query.Where(x => x.PropertyId == property);
            if (status is { } state) query = query.Where(x => x.Status == state);
            if (overdueOnly == true) query = query.Where(x => x.Status == ComplianceObligationStatus.Active && x.DueOn < today);
            var total = await query.CountAsync(ct);
            var currentPage = Math.Max(page ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var items = await query.OrderBy(x => x.DueOn).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new ComplianceObligationPage(items, total, currentPage, size));
        });

        group.MapPost("/obligations", async (CreateObligationRequest request, OperationsStore store,
            ITenantContext tenant, CancellationToken ct) =>
        {
            try
            {
                var obligation = new ComplianceObligation(tenant.OrganizationId, Guid.NewGuid(), request.PropertyId,
                    request.Title, request.DueOn, request.EscalationDays, request.Recurrence, request.RecurrenceEndOn);
                store.ComplianceObligations.Add(obligation);
                await store.SaveChangesAsync(ct);
                return Results.Created($"/api/compliance/obligations/{obligation.Id}", obligation);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapPost("/obligations/{id:guid}/satisfy", async (Guid id, SatisfyObligationRequest request,
            OperationsStore store, CancellationToken ct) =>
        {
            var obligation = await store.ComplianceObligations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (obligation is null) return Results.NotFound();
            try { obligation.Satisfy(request.SatisfiedOn); await store.SaveChangesAsync(ct); return Results.Ok(obligation); }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapGet("/dashboard", async (DateOnly? asOf, OperationsStore store, CancellationToken ct) =>
        {
            var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
            return Results.Ok(ComplianceRiskCalculator.Calculate(
                await store.ComplianceObligations.AsNoTracking().ToListAsync(ct),
                await store.Incidents.AsNoTracking().ToListAsync(ct),
                await store.Violations.AsNoTracking().ToListAsync(ct),
                await store.Remediations.AsNoTracking().ToListAsync(ct), today));
        });

        group.MapGet("/incidents/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await store.Incidents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } incident
                ? Results.Ok(incident) : Results.NotFound());

        group.MapPost("/incidents/{id:guid}/transition", async (Guid id, IncidentTransitionRequest request,
            OperationsStore store, CancellationToken ct) =>
        {
            var incident = await store.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (incident is null) return Results.NotFound();
            try
            {
                switch (request.Action)
                {
                    case IncidentAuditAction.InvestigationStarted: incident.BeginInvestigation(request.ActorId, request.OccurredAt, request.Note); break;
                    case IncidentAuditAction.Remediated: incident.MarkRemediated(request.ActorId, request.OccurredAt, request.Note); break;
                    case IncidentAuditAction.Closed: incident.Close(request.ActorId, request.OccurredAt, request.Note); break;
                    default: return Results.Problem(statusCode: 400, title: "Unsupported incident transition.");
                }
                await store.SaveChangesAsync(ct);
                return Results.Ok(incident);
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);
    }
}

public sealed record CreateObligationRequest(Guid PropertyId, string Title, DateOnly DueOn, int EscalationDays,
    ComplianceRecurrence Recurrence = ComplianceRecurrence.None, DateOnly? RecurrenceEndOn = null);
public sealed record SatisfyObligationRequest(DateOnly SatisfiedOn);
public sealed record IncidentTransitionRequest(IncidentAuditAction Action, Guid ActorId, DateTimeOffset OccurredAt, string? Note);
