using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Marketing;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

// FS-S05 rental applications: intake, consent, screening and the approve/deny trail.
//
// Two capabilities gate this surface. ManageApplications is the workflow and covers every route
// here; ReadApplicantPii is a second, separately revocable gate on unmasked contact details,
// income and screening detail. A caller with only the workflow capability sees a masked
// projection in which the PII properties are ABSENT FROM THE JSON rather than null — a null says
// "we hold no value", an absent property says "you may not see this", and a client renders those
// two facts differently. That is why the masked and unmasked shapes are separate record types
// rather than one type with nullable fields.
//
// There is deliberately NO per-read PII access log table. FS-S05 asks for PII access *tests*,
// not an access log; a row-per-read table would immediately be the highest-write table in the
// operations schema and would arrive with its own retention and purge story attached. It was
// considered and declined — if it is wanted later it is its own story, not a field here.
public static class ApplicationEndpoints
{
    public static void MapApplicationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/applications").RequireAuthorization(Capabilities.ManageApplications);

        group.MapGet("/", async (Guid? listingId, ApplicationStatus? status, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            var applications = await store.RentalApplications.AsNoTracking()
                .Where(x => listingId == null || x.ListingId == listingId)
                .Where(x => status == null || x.Status == status)
                .OrderByDescending(x => x.SubmittedAt).ThenBy(x => x.Id).Take(500)
                .ToListAsync(ct);
            var ids = applications.Select(x => x.Id).ToList();
            var rows = await ApplicantRowsAsync(store, ids, ct);
            return Results.Ok(applications.Select(x => Project(x, rows, [], MayReadPii(user))).ToList());
        });

        group.MapPost("/", async (CreateApplicationRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Listings.AnyAsync(x => x.Id == request.ListingId, ct)) return Results.NotFound("Listing was not found.");
            var application = new RentalApplication(store.OrganizationId, Guid.NewGuid(), request.ListingId);
            store.RentalApplications.Add(application);
            await store.SaveChangesAsync(ct);
            return Results.Created($"/api/applications/{application.Id}", Project(application, [], [], false));
        });

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
            await DetailAsync(id, store, MayReadPii(user), ct));

        // The explicit unmasked route. Distinct from GET /{id} on purpose: a caller who is denied
        // this one gets a 403 they can see, rather than silently receiving a thinner object.
        group.MapGet("/{id:guid}/pii", async (Guid id, OperationsStore store, CancellationToken ct) =>
            await DetailAsync(id, store, unmasked: true, ct)).RequireAuthorization(Capabilities.ReadApplicantPii);

        group.MapPost("/{id:guid}/applicants", async (Guid id, AddApplicantRequest request, ClaimsPrincipal user, OperationsStore store, CancellationToken ct) =>
        {
            var application = await store.RentalApplications.Include(x => x.Applicants).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (application is null) return Results.NotFound();
            if (!await store.Applicants.AnyAsync(x => x.Id == request.ApplicantId, ct)) return Results.NotFound("Applicant was not found.");
            try
            {
                application.AddApplicant(Guid.NewGuid(), request.ApplicantId, request.Role, request.MonthlyIncome, request.EmploymentStatus);
                await store.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException exception) { return Conflict(exception); }
            catch (DbUpdateConcurrencyException) { return Stale(); }
            return await DetailAsync(id, store, MayReadPii(user), ct);
        });

        group.MapPost("/{id:guid}/submit", async (Guid id, ClaimsPrincipal user, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
            await TransitionAsync(id, store, user, ct, (application, actor) => application.Submit(actor, clock.GetUtcNow()), includeApplicants: true));

        group.MapGet("/{id:guid}/consent", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.RentalApplications.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            // Consent rows carry no PII of their own, so this route is not masked. The whole
            // history is returned alongside the reduction, because "consent was held when
            // screening ran" is answered by the rows, not by the current effective value.
            var history = await store.ApplicationConsents.AsNoTracking().Where(x => x.ApplicationId == id)
                .OrderBy(x => x.ApplicantId).ThenBy(x => x.ConsentType).ThenBy(x => x.RecordedAt).ToListAsync(ct);
            var effective = ApplicationConsent.Effective(history).Values
                .Select(x => new EffectiveConsentResponse(x.ApplicantId, x.ConsentType, x.Decision, x.RecordedAt, x.RecordedBy, x.Source))
                .OrderBy(x => x.ApplicantId).ThenBy(x => x.ConsentType).ToList();
            return Results.Ok(new { applicationId = id, effective, history });
        });

        group.MapPost("/{id:guid}/consent", async (Guid id, RecordConsentRequest request, ClaimsPrincipal user, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var application = await store.RentalApplications.Include(x => x.Applicants).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (application is null) return Results.NotFound();
            if (application.Applicants.All(x => x.ApplicantId != request.ApplicantId))
                return Results.Problem(statusCode: 400, title: "That applicant is not on this application.");
            if (request.Decision is not (ConsentDecision.Granted or ConsentDecision.Revoked))
                return Results.Problem(statusCode: 400, title: "A recorded consent must be granted or revoked.");

            var now = clock.GetUtcNow();
            // Append-only: a revocation is a new row, never an edit of the granting one.
            var consent = new ApplicationConsent(store.OrganizationId, Guid.NewGuid(), id, request.ApplicantId,
                request.ConsentType, request.Decision, now, Actor(user), request.Source);
            store.ApplicationConsents.Add(consent);

            // The application advances to ConsentGranted once every applicant on it holds at
            // least one effective granted consent. A required-check policy ("background AND
            // credit, always") is an organization setting FS-S05 does not fund, so the gate is
            // "nobody on this application is un-consented" and the screening call then asks the
            // provider only for the checks actually granted. A later revocation does not walk the
            // status back — there is no such transition — so PF-S05.06 re-checks effective
            // consent per applicant immediately before it calls the provider.
            var rows = await store.ApplicationConsents.AsNoTracking().Where(x => x.ApplicationId == id).ToListAsync(ct);
            rows.Add(consent);
            if (application.Status == ApplicationStatus.Submitted && EveryApplicantConsented(application, rows))
            {
                try { application.MarkConsented(Actor(user)); }
                catch (InvalidOperationException exception) { return Conflict(exception); }
            }
            try { await store.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Stale(); }
            return Results.Created($"/api/applications/{id}/consent", new { consent, applicationStatus = application.Status });
        });

        group.MapPost("/{id:guid}/withdraw", async (Guid id, ClaimsPrincipal user, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
            await TransitionAsync(id, store, user, ct, (application, actor) => application.Withdraw(actor, clock.GetUtcNow())));
    }

    // ---- shared helpers -----------------------------------------------------------------

    internal static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    internal static bool MayReadPii(ClaimsPrincipal user) => user.HasClaim(TenantAccess.CapabilityClaim, Capabilities.ReadApplicantPii);

    // A refused domain transition is a 409 and the message is the whole point: "approving over a
    // failed screening requires an override note" is actionable, "conflict" is not. ProblemDetails
    // rather than Results.Conflict(string) so the body shape matches ApiExceptionHandler's.
    // ArgumentException is deliberately NOT caught here — ApiExceptionHandler:20 already maps the
    // whole family to 400, and catching it locally would only duplicate that.
    internal static IResult Conflict(InvalidOperationException exception) =>
        Results.Problem(statusCode: 409, title: exception.Message);

    internal static IResult Stale() =>
        Results.Problem(statusCode: 409, title: "The application changed; reload and try again.");

    internal static async Task<IResult> TransitionAsync(
        Guid id, OperationsStore store, ClaimsPrincipal user, CancellationToken ct,
        Action<RentalApplication, Guid> change, bool includeApplicants = false)
    {
        var query = store.RentalApplications.AsQueryable();
        if (includeApplicants) query = query.Include(x => x.Applicants);
        var application = await query.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (application is null) return Results.NotFound();
        try
        {
            change(application, Actor(user));
            await store.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException exception) { return Conflict(exception); }
        catch (DbUpdateConcurrencyException) { return Stale(); }
        return await DetailAsync(id, store, MayReadPii(user), ct);
    }

    internal static async Task<IResult> DetailAsync(Guid id, OperationsStore store, bool unmasked, CancellationToken ct)
    {
        var application = await store.RentalApplications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (application is null) return Results.NotFound();
        var rows = await ApplicantRowsAsync(store, [id], ct);
        var screening = await ScreeningRowsAsync(store, id, ct);
        return Results.Ok(Project(application, rows, screening, unmasked));
    }

    // The person's name/email come from operations."Applicants", the phone from the inquiry that
    // produced them, and income/employment from the per-application join row.
    internal static async Task<IReadOnlyList<ApplicantRow>> ApplicantRowsAsync(OperationsStore store, IReadOnlyList<Guid> applicationIds, CancellationToken ct)
    {
        if (applicationIds.Count == 0) return [];
        return await (from link in store.ApplicationApplicants.AsNoTracking()
                      join person in store.Applicants.AsNoTracking()
                          on new { link.OrganizationId, Id = link.ApplicantId } equals new { person.OrganizationId, person.Id }
                      join inquiry in store.Inquiries.AsNoTracking()
                          on new { person.OrganizationId, Id = person.InquiryId } equals new { inquiry.OrganizationId, inquiry.Id } into inquiries
                      from inquiry in inquiries.DefaultIfEmpty()
                      where applicationIds.Contains(link.ApplicationId)
                      select new ApplicantRow(link.ApplicationId, link.Id, link.ApplicantId, person.ProspectName, link.Role,
                          person.Email, inquiry == null ? null : inquiry.Phone, link.MonthlyIncome, link.EmploymentStatus))
            .ToListAsync(ct);
    }

    internal static async Task<IReadOnlyList<ScreeningRow>> ScreeningRowsAsync(OperationsStore store, Guid applicationId, CancellationToken ct)
    {
        var requests = await store.ScreeningRequests.AsNoTracking().Where(x => x.ApplicationId == applicationId).ToListAsync(ct);
        if (requests.Count == 0) return [];
        var requestIds = requests.Select(x => x.Id).ToList();
        var results = await store.ScreeningResults.AsNoTracking().Where(x => requestIds.Contains(x.ScreeningRequestId)).ToListAsync(ct);
        return requests.Select(request =>
        {
            // Results are append-only, so a revised verdict is a later row; the latest one wins.
            var latest = results.Where(x => x.ScreeningRequestId == request.Id).OrderByDescending(x => x.ReceivedAt).FirstOrDefault();
            return new ScreeningRow(request.Id, request.ApplicantId, request.Status, request.Attempts, request.LastAttemptedAt,
                request.LastError, request.CompletedAt, latest?.Recommendation, latest?.Score, latest?.Summary, latest?.ReceivedAt);
        }).OrderBy(x => x.ApplicantId).ToList();
    }

    // The masking decision, in one place. Two record shapes, not one shape with nulls.
    internal static object Project(RentalApplication application, IReadOnlyList<ApplicantRow> rows, IReadOnlyList<ScreeningRow> screening, bool unmasked)
    {
        var mine = rows.Where(x => x.ApplicationId == application.Id).OrderBy(x => x.Role).ThenBy(x => x.Name).ToList();
        if (unmasked)
            return new ApplicationPiiResponse(application.Id, application.ListingId, application.Status,
                application.SubmittedAt, application.DecidedAt,
                [.. mine.Select(x => new ApplicantPiiResponse(x.Id, x.ApplicantId, x.Name, x.Role, x.Email, x.Phone, x.MonthlyIncome, x.EmploymentStatus))],
                [.. screening.Select(x => new ScreeningPiiResponse(x.RequestId, x.ApplicantId, x.Status, x.Attempts, x.LastAttemptedAt, x.LastError, x.CompletedAt, x.Recommendation, x.Score, x.Summary, x.ReceivedAt))]);
        return new ApplicationResponse(application.Id, application.ListingId, application.Status,
            application.SubmittedAt, application.DecidedAt,
            [.. mine.Select(x => new ApplicantResponse(x.Id, x.ApplicantId, x.Name, x.Role))],
            [.. screening.Select(x => new ScreeningResponse(x.RequestId, x.ApplicantId, x.Status, x.Attempts, x.LastAttemptedAt, x.LastError, x.CompletedAt, x.Recommendation))]);
    }

    internal static bool EveryApplicantConsented(RentalApplication application, IReadOnlyList<ApplicationConsent> consents)
    {
        var effective = ApplicationConsent.Effective(consents);
        return application.Applicants.Count > 0 && application.Applicants.All(applicant =>
            effective.Any(entry => entry.Key.ApplicantId == applicant.ApplicantId && entry.Value.AllowsScreening));
    }
}

// Internal carriers, not part of the HTTP contract.
public sealed record ApplicantRow(Guid ApplicationId, Guid Id, Guid ApplicantId, string Name, ApplicantRole Role, string Email, string? Phone, decimal? MonthlyIncome, string? EmploymentStatus);
public sealed record ScreeningRow(Guid RequestId, Guid ApplicantId, ScreeningRequestStatus Status, int Attempts, DateTimeOffset? LastAttemptedAt, string? LastError, DateTimeOffset? CompletedAt, ScreeningRecommendation? Recommendation, int? Score, string? Summary, DateTimeOffset? ReceivedAt);

// Masked shapes: Email, Phone, MonthlyIncome, EmploymentStatus, Score and Summary are absent,
// not null. Name stays visible — a leasing agent cannot work a queue of anonymous rows.
public sealed record ApplicationResponse(Guid Id, Guid ListingId, ApplicationStatus Status, DateTimeOffset? SubmittedAt, DateTimeOffset? DecidedAt, IReadOnlyList<ApplicantResponse> Applicants, IReadOnlyList<ScreeningResponse> Screening);
public sealed record ApplicantResponse(Guid Id, Guid ApplicantId, string Name, ApplicantRole Role);
public sealed record ScreeningResponse(Guid RequestId, Guid ApplicantId, ScreeningRequestStatus Status, int Attempts, DateTimeOffset? LastAttemptedAt, string? LastError, DateTimeOffset? CompletedAt, ScreeningRecommendation? Recommendation);

// Unmasked shapes, behind Applications.ReadPii.
public sealed record ApplicationPiiResponse(Guid Id, Guid ListingId, ApplicationStatus Status, DateTimeOffset? SubmittedAt, DateTimeOffset? DecidedAt, IReadOnlyList<ApplicantPiiResponse> Applicants, IReadOnlyList<ScreeningPiiResponse> Screening);
public sealed record ApplicantPiiResponse(Guid Id, Guid ApplicantId, string Name, ApplicantRole Role, string Email, string? Phone, decimal? MonthlyIncome, string? EmploymentStatus);
public sealed record ScreeningPiiResponse(Guid RequestId, Guid ApplicantId, ScreeningRequestStatus Status, int Attempts, DateTimeOffset? LastAttemptedAt, string? LastError, DateTimeOffset? CompletedAt, ScreeningRecommendation? Recommendation, int? Score, string? Summary, DateTimeOffset? ReceivedAt);

public sealed record EffectiveConsentResponse(Guid ApplicantId, ApplicationConsentType ConsentType, ConsentDecision Decision, DateTimeOffset RecordedAt, Guid RecordedBy, string Source);

public sealed record CreateApplicationRequest(Guid ListingId);
public sealed record AddApplicantRequest(Guid ApplicantId, ApplicantRole Role, decimal? MonthlyIncome, string? EmploymentStatus);
public sealed record RecordConsentRequest(Guid ApplicantId, ApplicationConsentType ConsentType, ConsentDecision Decision, string Source);
