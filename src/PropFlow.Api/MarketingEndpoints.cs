using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Marketing;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class MarketingEndpoints
{
    public static void MapMarketingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/marketing/listings").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async (Guid? propertyId, ListingStatus? status, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Listings.AsNoTracking()
                .Where(x => propertyId == null || x.PropertyId == propertyId)
                .Where(x => status == null || x.Status == status)
                .Where(x => x.Status != ListingStatus.Published || x.SpaceId == null || !store.Occupancies.Any(occupancy => occupancy.SpaceId == x.SpaceId && occupancy.MovedOutOn == null))
                .OrderBy(x => x.AvailableOn).ThenBy(x => x.Headline).Take(500)
                .Select(x => new { x.Id, x.PropertyId, x.SpaceId, x.Headline, x.Description, x.AvailableOn, x.MonthlyRent, x.Status, IsAvailable = x.SpaceId == null || !store.Occupancies.Any(occupancy => occupancy.SpaceId == x.SpaceId && occupancy.MovedOutOn == null) }).ToListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var listing = await store.Listings.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.PropertyId, x.SpaceId, x.Headline, x.Description, x.AvailableOn, x.MonthlyRent, x.Status, IsAvailable = x.SpaceId == null || !store.Occupancies.Any(occupancy => occupancy.SpaceId == x.SpaceId && occupancy.MovedOutOn == null) }).SingleOrDefaultAsync(ct);
            return listing is null ? Results.NotFound() : Results.Ok(listing);
        });

        group.MapPost("/", async (ListingRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Properties.AnyAsync(x => x.Id == request.PropertyId, ct)) return Results.Problem(statusCode: 400, title: "Property was not found");
            if (request.SpaceId is { } spaceId && !await store.Spaces.AnyAsync(x => x.Id == spaceId && x.PropertyId == request.PropertyId, ct)) return Results.Problem(statusCode: 400, title: "Space was not found in this property");
            var listing = new Listing(store.OrganizationId, Guid.NewGuid(), request.PropertyId, request.SpaceId, request.Headline, request.Description, request.AvailableOn, request.MonthlyRent);
            store.Listings.Add(listing); await store.SaveChangesAsync(ct);
            return Results.Created($"/api/marketing/listings/{listing.Id}", listing);
        }).RequireAuthorization(Capabilities.ManageLeasing);

        group.MapPut("/{id:guid}", async (Guid id, ListingRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var listing = await store.Listings.SingleOrDefaultAsync(x => x.Id == id, ct); if (listing is null) return Results.NotFound();
            if (!await store.Properties.AnyAsync(x => x.Id == request.PropertyId, ct)) return Results.Problem(statusCode: 400, title: "Property was not found");
            if (request.SpaceId is { } spaceId && !await store.Spaces.AnyAsync(x => x.Id == spaceId && x.PropertyId == request.PropertyId, ct)) return Results.Problem(statusCode: 400, title: "Space was not found in this property");
            listing.Update(request.PropertyId, request.SpaceId, request.Headline, request.Description, request.AvailableOn, request.MonthlyRent); await store.SaveChangesAsync(ct); return Results.Ok(listing);
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/publish", Publish).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/unpublish", async (Guid id, OperationsStore store, CancellationToken ct) => await SetListingStatus(id, store, ct, l => l.Unpublish())).RequireAuthorization(Capabilities.ManageLeasing);

        group.MapGet("/{id:guid}/inquiries", async (Guid id, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Inquiries.AsNoTracking().Where(x => x.ListingId == id).OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct)));
        group.MapPost("/{id:guid}/inquiries", async (Guid id, InquiryRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Listings.AnyAsync(x => x.Id == id && x.Status == ListingStatus.Published && (x.SpaceId == null || !store.Occupancies.Any(occupancy => occupancy.SpaceId == x.SpaceId && occupancy.MovedOutOn == null)), ct)) return Results.NotFound();
            if (await store.Inquiries.AnyAsync(x => x.ListingId == id && x.Email == request.Email && x.Status != InquiryStatus.Closed, ct)) return Results.Conflict("An active inquiry already exists for this email.");
            var inquiry = new Inquiry(store.OrganizationId, Guid.NewGuid(), id, request.ProspectName, request.Email, request.Phone, request.Message, request.LeadSource); store.Inquiries.Add(inquiry); await store.SaveChangesAsync(ct); return Results.Created($"/api/marketing/listings/{id}/inquiries/{inquiry.Id}", inquiry);
        });
        group.MapPut("/{id:guid}/inquiries/{inquiryId:guid}/status", async (Guid id, Guid inquiryId, InquiryStatusRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var inquiry = await store.Inquiries.SingleOrDefaultAsync(x => x.Id == inquiryId && x.ListingId == id, ct); if (inquiry is null) return Results.NotFound(); inquiry.SetStatus(request.Status); await store.SaveChangesAsync(ct); return Results.Ok(inquiry);
        }).RequireAuthorization(Capabilities.ManageLeasing);

        group.MapGet("/{id:guid}/applicants", async (Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.Applicants.AsNoTracking().Where(x => x.ListingId == id).OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct)));
        group.MapPost("/{id:guid}/applicants", async (Guid id, ApplicantRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var inquiry = await store.Inquiries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.InquiryId && x.ListingId == id, ct); if (inquiry is null) return Results.NotFound();
            if (await store.Applicants.AnyAsync(x => x.InquiryId == request.InquiryId, ct)) return Results.Conflict("This inquiry is already an applicant.");
            var applicant = new Applicant(store.OrganizationId, Guid.NewGuid(), id, inquiry.Id, inquiry.ProspectName, inquiry.Email); store.Applicants.Add(applicant); await store.SaveChangesAsync(ct); return Results.Created($"/api/marketing/listings/{id}/applicants/{applicant.Id}", applicant);
        }).RequireAuthorization(Capabilities.ManageLeasing);
        // FS-S05 fence, and it is a correctness fix rather than tidying. This route sets a flat
        // status on the *person*, with no consent check and no decision row. Once a
        // RentalApplication exists for that person, the shipped "Send to screening" / "Approve"
        // buttons on /marketing/listings would drive it straight to Approved and bypass the
        // consent gate and the adverse-action trail that FS-S05 exists to produce. So it refuses,
        // loudly, and names the surface that does it properly. It keeps working for an applicant
        // with no application, so nothing that predates FS-S05 breaks. Rewiring the UI onto the
        // application flow is PF-S05.09.
        group.MapPut("/{id:guid}/applicants/{applicantId:guid}/status", async (Guid id, Guid applicantId, ApplicantStatusRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var applicant = await store.Applicants.SingleOrDefaultAsync(x => x.Id == applicantId && x.ListingId == id, ct);
            if (applicant is null) return Results.NotFound();
            if (await store.ApplicationApplicants.AnyAsync(x => x.ApplicantId == applicantId, ct))
                return Results.Problem(statusCode: 409, title: "This applicant has a rental application; use /api/applications to record consent, screening and the approve/deny decision.");
            applicant.SetStatus(request.Status); await store.SaveChangesAsync(ct); return Results.Ok(applicant);
        }).RequireAuthorization(Capabilities.ManageLeasing);

        group.MapGet("/{id:guid}/showings", async (Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.Showings.AsNoTracking().Where(x => x.ListingId == id).OrderBy(x => x.ScheduledAt).ToListAsync(ct)));
        group.MapPost("/{id:guid}/showings", async (Guid id, ShowingRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Listings.AnyAsync(x => x.Id == id && x.Status == ListingStatus.Published && (x.SpaceId == null || !store.Occupancies.Any(occupancy => occupancy.SpaceId == x.SpaceId && occupancy.MovedOutOn == null)), ct)) return Results.NotFound();
            var showing = new Showing(store.OrganizationId, Guid.NewGuid(), id, request.ProspectName, request.ScheduledAt); store.Showings.Add(showing); await store.SaveChangesAsync(ct); return Results.Created($"/api/marketing/listings/{id}/showings/{showing.Id}", showing);
        });
        group.MapPut("/{id:guid}/showings/{showingId:guid}/status", async (Guid id, Guid showingId, ShowingStatusRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var showing = await store.Showings.SingleOrDefaultAsync(x => x.Id == showingId && x.ListingId == id, ct); if (showing is null) return Results.NotFound(); showing.SetStatus(request.Status); await store.SaveChangesAsync(ct); return Results.Ok(showing);
        }).RequireAuthorization(Capabilities.ManageLeasing);
    }

    private static async Task<IResult> SetListingStatus(Guid id, OperationsStore store, CancellationToken ct, Action<Listing> change)
    {
        var listing = await store.Listings.SingleOrDefaultAsync(x => x.Id == id, ct); if (listing is null) return Results.NotFound(); change(listing); await store.SaveChangesAsync(ct); return Results.Ok(listing);
    }

    private static async Task<IResult> Publish(Guid id, OperationsStore store, CancellationToken ct)
    {
        var listing = await store.Listings.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (listing is null) return Results.NotFound();
        if (listing.SpaceId is { } spaceId && await store.Occupancies.AnyAsync(x => x.SpaceId == spaceId && x.MovedOutOn == null, ct))
            return Results.Conflict("A listing cannot be published for an occupied space.");
        listing.Publish();
        await store.SaveChangesAsync(ct);
        return Results.Ok(listing);
    }
}

public sealed record ListingRequest(Guid PropertyId, Guid? SpaceId, string Headline, string? Description, DateOnly? AvailableOn, decimal? MonthlyRent);
public sealed record InquiryRequest(string ProspectName, string Email, string? Phone, string? Message, string? LeadSource);
public sealed record InquiryStatusRequest(InquiryStatus Status);
public sealed record ShowingRequest(string ProspectName, DateTimeOffset ScheduledAt);
public sealed record ShowingStatusRequest(ShowingStatus Status);
public sealed record ApplicantRequest(Guid InquiryId);
public sealed record ApplicantStatusRequest(ApplicantStatus Status);
