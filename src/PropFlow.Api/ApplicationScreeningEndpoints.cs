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

            // Idempotent by design, and the consent gate is untouched by that: ENTERING Screening
            // still requires ConsentGranted, which is the gate. Re-posting while already in
            // Screening re-attempts the requests that are still pending instead of answering 409,
            // because a client that just received a 503 retries the URL it posted to. Without
            // this the only way forward after an outage was the retry route, and the replay path
            // below was unreachable.
            if (application.Status != ApplicationStatus.Screening)
            {
                try { application.BeginScreening(ApplicationEndpoints.Actor(user)); }
                catch (InvalidOperationException exception) { return ApplicationEndpoints.Conflict(exception); }
            }

            // Get-or-create, per applicant and in this priority order:
            //   live (Pending/InFlight)    -> reuse it. That is what makes a replayed POST safe,
            //     and it is why the row is found by applicant rather than by a guessed key.
            //   already Completed          -> leave it alone. Re-screening a completed applicant
            //     would be a second credit pull at the organization's expense.
            //   nothing, or only Abandoned -> mint the next attempt cycle. This is the path
            //     AbandonScreening promises, and a key fixed per (application, applicant) made it
            //     unreachable: the unique index would have refused a second request forever.
            var existing = await store.ScreeningRequests.Where(x => x.ApplicationId == id).ToListAsync(ct);
            var pending = new List<ScreeningRequest>();
            foreach (var applicant in subjects)
            {
                var mine = existing.Where(x => x.ApplicantId == applicant.ApplicantId).ToList();
                var live = mine.Find(x => x.Status is ScreeningRequestStatus.Pending or ScreeningRequestStatus.InFlight);
                if (live is not null) { if (live.Status == ScreeningRequestStatus.Pending) pending.Add(live); continue; }
                if (mine.Exists(x => x.Status == ScreeningRequestStatus.Completed)) continue;
                var request = new ScreeningRequest(store.OrganizationId, Guid.NewGuid(), id, applicant.ApplicantId,
                    ScreeningRequest.KeyFor(id, applicant.ApplicantId, mine.Count + 1));
                store.ScreeningRequests.Add(request);
                pending.Add(request);
            }
            try { await store.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return ApplicationEndpoints.Stale(); }
            // Two callers racing the same application both compute the same next cycle; the unique
            // index turns the loser into a conflict rather than a duplicate credit pull.
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            { return Results.Problem(statusCode: 409, title: "Screening is already being requested for this application; reload and try again."); }

            return await AttemptAsync(application, pending, consents, store, provider, options, clock, user, ct);
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

        // Judged per applicant, not per row. Once an abandoned cycle can be followed by a fresh
        // one, "every request is Completed" is the wrong question — an applicant whose cycle 1
        // was abandoned and whose cycle 2 passed would never reach it, and the application would
        // sit in Screening forever. The state of an applicant is the best outcome they hold:
        // still in flight beats completed beats abandoned.
        var all = await store.ScreeningRequests.Where(x => x.ApplicationId == application.Id).ToListAsync(ct);
        var perApplicant = all.GroupBy(x => x.ApplicantId).Select(StateOf).ToList();
        var abandoned = perApplicant.Count > 0 && perApplicant.TrueForAll(x => x == ScreeningRequestStatus.Abandoned);
        var completed = perApplicant.Count > 0 && perApplicant.TrueForAll(x => x == ScreeningRequestStatus.Completed);
        try
        {
            if (abandoned && application.Status == ApplicationStatus.Screening) application.AbandonScreening(actor);
            else if (outage is null && completed && application.Status == ApplicationStatus.Screening) application.CompleteScreening(actor);
            await store.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException exception) { return ApplicationEndpoints.Conflict(exception); }
        catch (DbUpdateConcurrencyException) { return ApplicationEndpoints.Stale(); }

        if (outage is not null)
            return Results.Problem(statusCode: 503, title: outage, detail: abandoned
                ? $"Every screening request for this application is abandoned after {options.MaxAttempts} attempts; the application has returned to ConsentGranted."
                : "No screening verdict was written. Re-post to this route, or POST to screening/{requestId}/retry, once the provider is available.");

        return await ApplicationEndpoints.DetailAsync(application.Id, store, ApplicationEndpoints.MayReadPii(user), ct);
    }

    // One applicant's screening state across every attempt cycle they hold.
    private static ScreeningRequestStatus StateOf(IEnumerable<ScreeningRequest> cycles)
    {
        var rows = cycles.ToList();
        if (rows.Exists(x => x.Status is ScreeningRequestStatus.Pending or ScreeningRequestStatus.InFlight))
            return ScreeningRequestStatus.Pending;
        return rows.Exists(x => x.Status == ScreeningRequestStatus.Completed)
            ? ScreeningRequestStatus.Completed
            : ScreeningRequestStatus.Abandoned;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static bool HasAnyConsent(IReadOnlyList<ApplicationConsent> consents, Guid applicationId, Guid applicantId) =>
        ApplicationConsent.Effective(consents).Any(entry =>
            entry.Key.ApplicationId == applicationId && entry.Key.ApplicantId == applicantId && entry.Value.AllowsScreening);
}
