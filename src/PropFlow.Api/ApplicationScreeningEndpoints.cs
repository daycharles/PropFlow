using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Application.Screening;
using PropFlow.Domain.Marketing;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// FS-S05 screening: where ScreeningRequest's retry engine meets IScreeningProvider.
//
// What "a provider outage writes nothing" means here, precisely. BillingEndpoints.cs:190-193 is
// the precedent and it writes literally nothing, because a payment has no retry state to keep.
// Screening does: PF-S05.06 also requires that retry exhaustion move the request to Abandoned
// and the application back to ConsentGranted, and an attempt counter that is not persisted can
// never exhaust. So the split is:
//
//   * NO VERDICT is written on an outage - no ScreeningResult row, no advance to UnderReview.
//     ScreeningRecommendation.Unavailable is refused by ScreeningResult's constructor
//     (ScreeningResult.cs:31-32), so an outage cannot become a stored verdict even by mistake.
//   * The ScreeningRequest row and its attempt counter ARE written, because they are the retry
//     ledger rather than an answer about the applicant. That is what the idempotency key and the
//     unique index on (OrganizationId, IdempotencyKey) exist for.
//
// The response is 503, not 500 and not a stored Fail.
public static class ApplicationScreeningEndpoints
{
    public static void MapApplicationScreeningEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/applications").RequireAuthorization(Capabilities.ManageApplications);

        group.MapGet("/{id:guid}/screening", async (Guid id, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.RentalApplications.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var rows = await ApplicationEndpoints.ScreeningRowsAsync(store, id, ct);
            return Results.Ok(ApplicationEndpoints.MayReadPii(user)
                ? rows.Select(x => new ScreeningPiiResponse(x.RequestId, x.ApplicantId, x.Status, x.Attempts, x.LastAttemptedAt, x.LastError, x.CompletedAt, x.Recommendation, x.Score, x.Summary, x.ReceivedAt)).ToList<object>()
                : rows.Select(x => new ScreeningResponse(x.RequestId, x.ApplicantId, x.Status, x.Attempts, x.LastAttemptedAt, x.LastError, x.CompletedAt, x.Recommendation)).ToList<object>());
        });

        group.MapPost("/{id:guid}/screening", async (Guid id, ClaimsPrincipal user, OperationsStore store,
            IScreeningProvider provider, ScreeningOptions options, TimeProvider clock, CancellationToken ct) =>
        {
            var application = await store.RentalApplications.Include(x => x.Applicants).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (application is null) return Results.NotFound();

            // The consent gate is asserted twice, deliberately. The domain refuses any status but
            // ConsentGranted; the row check below refuses an individual applicant whose consent
            // was revoked after the application reached ConsentGranted, which the status alone
            // cannot see.
            var consents = await store.ApplicationConsents.AsNoTracking().Where(x => x.ApplicationId == id).ToListAsync(ct);
            var subjects = application.Applicants
                .Where(applicant => HasAnyConsent(consents, id, applicant.ApplicantId))
                .ToList();
            if (subjects.Count == 0)
                return Results.Problem(statusCode: 409, title: "No applicant on this application holds consent to be screened.");

            try { application.BeginScreening(ApplicationEndpoints.Actor(user)); }
            catch (InvalidOperationException exception) { return ApplicationEndpoints.Conflict(exception); }

            // Get-or-create is what makes a replayed POST safe: the key is derived, not generated.
            var existing = await store.ScreeningRequests.Where(x => x.ApplicationId == id).ToListAsync(ct);
            var requests = new List<ScreeningRequest>();
            foreach (var applicant in subjects)
            {
                var key = ScreeningRequest.KeyFor(id, applicant.ApplicantId);
                var request = existing.SingleOrDefault(x => x.IdempotencyKey == key);
                if (request is null)
                {
                    request = new ScreeningRequest(store.OrganizationId, Guid.NewGuid(), id, applicant.ApplicantId, key);
                    store.ScreeningRequests.Add(request);
                }
                requests.Add(request);
            }
            try { await store.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return ApplicationEndpoints.Stale(); }

            return await AttemptAsync(application, [.. requests.Where(x => x.Status == ScreeningRequestStatus.Pending)],
                consents, store, provider, options, clock, user, ct);
        });

        group.MapPost("/{id:guid}/screening/{requestId:guid}/retry", async (Guid id, Guid requestId, ClaimsPrincipal user,
            OperationsStore store, IScreeningProvider provider, ScreeningOptions options, TimeProvider clock, CancellationToken ct) =>
        {
            var application = await store.RentalApplications.Include(x => x.Applicants).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (application is null) return Results.NotFound();
            var request = await store.ScreeningRequests.SingleOrDefaultAsync(x => x.Id == requestId && x.ApplicationId == id, ct);
            if (request is null) return Results.NotFound();
            if (request.Status != ScreeningRequestStatus.Pending)
                return Results.Problem(statusCode: 409, title: $"Only a pending screening request can be retried; this one is {request.Status}.");

            var consents = await store.ApplicationConsents.AsNoTracking().Where(x => x.ApplicationId == id).ToListAsync(ct);
            if (!HasAnyConsent(consents, id, request.ApplicantId))
                return Results.Problem(statusCode: 409, title: "That applicant no longer holds consent to be screened.");

            return await AttemptAsync(application, [request], consents, store, provider, options, clock, user, ct);
        });
    }

    // One attempt over a set of pending requests. Shared by the request and retry routes so the
    // outage, exhaustion and completion rules cannot drift apart between them.
    private static async Task<IResult> AttemptAsync(
        RentalApplication application, IReadOnlyList<ScreeningRequest> pending, IReadOnlyList<ApplicationConsent> consents,
        OperationsStore store, IScreeningProvider provider, ScreeningOptions options, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var actor = ApplicationEndpoints.Actor(user);
        var effective = ApplicationConsent.Effective(consents);
        string? outage = null;

        foreach (var request in pending)
        {
            var person = await store.Applicants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ApplicantId, ct);
            if (person is null) return Results.Problem(statusCode: 409, title: "The applicant for this screening request no longer exists.");
            var phone = await store.Inquiries.AsNoTracking().Where(x => x.Id == person.InquiryId).Select(x => x.Phone).FirstOrDefaultAsync(ct);
            var granted = effective
                .Where(entry => entry.Key.ApplicationId == application.Id && entry.Key.ApplicantId == request.ApplicantId && entry.Value.AllowsScreening)
                .Select(entry => entry.Key.ConsentType).ToArray();

            request.BeginAttempt(now);
            var result = await provider.ScreenAsync(new ScreeningProviderRequest(store.OrganizationId, application.Id,
                request.ApplicantId, person.ProspectName, person.Email, phone, granted, request.IdempotencyKey), ct);

            if (result.Outcome == ScreeningProviderOutcome.Unavailable)
            {
                // No ScreeningResult is constructed at all on this path, so there is no way for
                // Unavailable to reach the verdict table.
                request.RecordFailure(result.FailureReason ?? "The screening provider is unavailable.", now, options.MaxAttempts);
                outage ??= result.FailureReason ?? "The screening provider is unavailable.";
                continue;
            }

            store.ScreeningResults.Add(new ScreeningResult(store.OrganizationId, Guid.NewGuid(), request.Id,
                request.ApplicantId, result.Recommendation, result.Score, result.Summary, now));
            request.Complete(now);
        }

        // Every request abandoned means the retry budget is spent for this application: back to
        // ConsentGranted, from which a fresh request can be raised without re-collecting consent.
        var all = await store.ScreeningRequests.Where(x => x.ApplicationId == application.Id).ToListAsync(ct);
        var abandoned = all.Count > 0 && all.TrueForAll(x => x.Status == ScreeningRequestStatus.Abandoned);
        try
        {
            if (abandoned && application.Status == ApplicationStatus.Screening) application.AbandonScreening(actor);
            else if (outage is null && all.Count > 0 && all.TrueForAll(x => x.Status == ScreeningRequestStatus.Completed)
                     && application.Status == ApplicationStatus.Screening) application.CompleteScreening(actor);
            await store.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException exception) { return ApplicationEndpoints.Conflict(exception); }
        catch (DbUpdateConcurrencyException) { return ApplicationEndpoints.Stale(); }

        if (outage is not null)
            return Results.Problem(statusCode: 503, title: outage, detail: abandoned
                ? $"Every screening request for this application is abandoned after {options.MaxAttempts} attempts; the application has returned to ConsentGranted."
                : "No screening verdict was written. Retry the request when the provider is available.");

        return await ApplicationEndpoints.DetailAsync(application.Id, store, ApplicationEndpoints.MayReadPii(user), ct);
    }

    private static bool HasAnyConsent(IReadOnlyList<ApplicationConsent> consents, Guid applicationId, Guid applicantId) =>
        ApplicationConsent.Effective(consents).Any(entry =>
            entry.Key.ApplicationId == applicationId && entry.Key.ApplicantId == applicantId && entry.Value.AllowsScreening);
}
