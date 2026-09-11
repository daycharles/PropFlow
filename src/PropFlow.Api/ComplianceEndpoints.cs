using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Application.Attachments;
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

        group.MapPost("/incidents", async (CreateIncidentRequest request, ClaimsPrincipal user, OperationsStore store, ITenantContext tenant, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Unauthorized();
            try { var incident = new Incident(tenant.OrganizationId, Guid.NewGuid(), request.PropertyId, request.Title, request.OccurredAt); store.Incidents.Add(incident); await store.SaveChangesAsync(ct); return Results.Created($"/api/compliance/incidents/{incident.Id}", incident); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapPost("/obligations/{id:guid}/generate-occurrences", async (Guid id, DateOnly? through,
            OperationsStore store, CancellationToken ct) =>
        {
            var obligation = await store.ComplianceObligations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (obligation is null) return Results.NotFound();
            var cutoff = through ?? DateOnly.FromDateTime(DateTime.UtcNow);
            await using var transaction = await store.Database.BeginTransactionAsync(ct);
            var occurrences = ComplianceRecurrenceGenerator.DueThrough(obligation, cutoff);
            var keys = occurrences.Select(x => x.IdempotencyKey).ToArray();
            var existing = await store.ComplianceOccurrences.Where(x => keys.Contains(x.IdempotencyKey))
                .Select(x => x.IdempotencyKey).ToListAsync(ct);
            var added = occurrences.Where(x => !existing.Contains(x.IdempotencyKey))
                .Select(x => new PropFlow.Domain.Properties.ComplianceOccurrence(store.OrganizationId, Guid.NewGuid(), x.SourceObligationId, x.DueOn, x.IdempotencyKey))
                .ToList();
            store.ComplianceOccurrences.AddRange(added);
            await store.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(added);
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
            ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            var incident = await store.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (incident is null) return Results.NotFound();
            try
            {
                switch (request.Action)
                {
                    case IncidentAuditAction.InvestigationStarted: incident.BeginInvestigation(Actor(user), request.OccurredAt, request.Note); break;
                    case IncidentAuditAction.Remediated: incident.MarkRemediated(Actor(user), request.OccurredAt, request.Note); break;
                    case IncidentAuditAction.Closed: incident.Close(Actor(user), request.OccurredAt, request.Note); break;
                    default: return Results.Problem(statusCode: 400, title: "Unsupported incident transition.");
                }
                await store.SaveChangesAsync(ct);
                return Results.Ok(incident);
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapGet("/violations/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await store.Violations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } violation
                ? Results.Ok(violation) : Results.NotFound());

        group.MapPost("/violations", async (CreateViolationRequest request, OperationsStore store,
            ITenantContext tenant, CancellationToken ct) =>
        {
            try
            {
                var violation = new Violation(tenant.OrganizationId, Guid.NewGuid(), request.PropertyId,
                    request.Title, request.IdentifiedOn, request.Severity);
                store.Violations.Add(violation); await store.SaveChangesAsync(ct);
                return Results.Created($"/api/compliance/violations/{violation.Id}", violation);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapPost("/violations/{id:guid}/transition", async (Guid id, ViolationTransitionRequest request,
            OperationsStore store, CancellationToken ct) =>
        {
            var violation = await store.Violations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (violation is null) return Results.NotFound();
            try
            {
                switch (request.Action)
                {
                    case ViolationAction.StartRemediation: violation.StartRemediation(); break;
                    case ViolationAction.Resolve: violation.Resolve(request.On ?? DateOnly.FromDateTime(DateTime.UtcNow)); break;
                    case ViolationAction.Waive: violation.Waive(); break;
                    default: return Results.Problem(statusCode: 400, title: "Unsupported violation transition.");
                }
                await store.SaveChangesAsync(ct); return Results.Ok(violation);
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapGet("/remediations/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await store.Remediations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } remediation
                ? Results.Ok(remediation) : Results.NotFound());

        group.MapPost("/remediations", async (CreateRemediationRequest request, OperationsStore store,
            ITenantContext tenant, CancellationToken ct) =>
        {
            try
            {
                var remediation = new Remediation(tenant.OrganizationId, Guid.NewGuid(), request.PropertyId,
                    request.ViolationId, request.IncidentId, request.Action, request.DueOn);
                store.Remediations.Add(remediation); await store.SaveChangesAsync(ct);
                return Results.Created($"/api/compliance/remediations/{remediation.Id}", remediation);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapPost("/remediations/{id:guid}/transition", async (Guid id, RemediationTransitionRequest request,
            OperationsStore store, CancellationToken ct) =>
        {
            var remediation = await store.Remediations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (remediation is null) return Results.NotFound();
            try
            {
                switch (request.Action)
                {
                    case RemediationAction.Start: remediation.Start(); break;
                    case RemediationAction.Complete: remediation.Complete(request.On ?? DateOnly.FromDateTime(DateTime.UtcNow)); break;
                    case RemediationAction.Cancel: remediation.Cancel(); break;
                    default: return Results.Problem(statusCode: 400, title: "Unsupported remediation transition.");
                }
                await store.SaveChangesAsync(ct); return Results.Ok(remediation);
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

        group.MapGet("/evidence", async (Guid? incidentId, Guid? violationId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.ComplianceEvidence.AsNoTracking()
                .Where(x => (incidentId == null || x.IncidentId == incidentId) && (violationId == null || x.ViolationId == violationId))
                .OrderByDescending(x => x.CreatedAt).ToListAsync(ct)));

        group.MapGet("/evidence/{id:guid}/content", async (Guid id, OperationsStore store, IAttachmentStorage storage, CancellationToken ct) =>
        {
            var evidence = await store.ComplianceEvidence.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (evidence is null) return Results.NotFound();
            var attachment = await store.Attachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == evidence.AttachmentId, ct);
            if (attachment is null) return Results.NotFound();
            var content = await storage.OpenReadAsync(attachment.StorageKey, ct);
            return content is null ? Results.NotFound() : Results.File(content, attachment.ContentType, attachment.FileName);
        });

        group.MapPost("/evidence", async (CreateEvidenceRequest request, OperationsStore store,
            ITenantContext tenant, CancellationToken ct) =>
        {
            if (!await store.Attachments.AnyAsync(x => x.Id == request.AttachmentId, ct)) return Results.BadRequest("Attachment was not found.");
            if (request.IncidentId is null && request.ViolationId is null) return Results.BadRequest("An incident or violation is required.");
            var created = request.CreatedAt ?? DateTimeOffset.UtcNow;
            try
            {
                var retainUntil = new EvidenceRetentionPolicy(request.RetentionDays).RetainUntil(created, request.LegalHold);
                var evidence = new ComplianceEvidence(tenant.OrganizationId, Guid.NewGuid(), request.AttachmentId,
                    request.IncidentId, request.ViolationId, created, retainUntil, request.LegalHold);
                store.ComplianceEvidence.Add(evidence); await store.SaveChangesAsync(ct);
                return Results.Created($"/api/compliance/evidence/{evidence.Id}", evidence);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageProperties);

    }

    private static Guid Actor(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty ? id : throw new UnauthorizedAccessException();
}

public sealed record CreateObligationRequest(Guid PropertyId, string Title, DateOnly DueOn, int EscalationDays,
    ComplianceRecurrence Recurrence = ComplianceRecurrence.None, DateOnly? RecurrenceEndOn = null);
public sealed record SatisfyObligationRequest(DateOnly SatisfiedOn);
public sealed record CreateIncidentRequest(Guid PropertyId, string Title, DateTimeOffset OccurredAt);
public sealed record IncidentTransitionRequest(IncidentAuditAction Action, DateTimeOffset OccurredAt, string? Note);
public sealed record CreateViolationRequest(Guid PropertyId, string Title, DateOnly IdentifiedOn, int Severity);
public enum ViolationAction { StartRemediation, Resolve, Waive }
public sealed record ViolationTransitionRequest(ViolationAction Action, DateOnly? On);
public sealed record CreateRemediationRequest(Guid PropertyId, Guid? ViolationId, Guid? IncidentId, string Action, DateOnly DueOn);
public enum RemediationAction { Start, Complete, Cancel }
public sealed record RemediationTransitionRequest(RemediationAction Action, DateOnly? On);
public sealed record CreateEvidenceRequest(Guid AttachmentId, Guid? IncidentId, Guid? ViolationId, int RetentionDays = 365,
    bool LegalHold = false, DateTimeOffset? CreatedAt = null);
