using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Leasing;
using PropFlow.Domain.People;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class LeasingEndpoints
{
    public static void MapLeasingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/leasing/leases").RequireAuthorization(Capabilities.ReadWork);
        group.MapGet("/", async (Guid? residentId, Guid? spaceId, OperationsStore store, CancellationToken ct) =>
            Results.Ok(await store.Leases.AsNoTracking()
                .Where(x => residentId == null || x.ResidentId == residentId).Where(x => spaceId == null || x.SpaceId == spaceId)
                .OrderByDescending(x => x.StartsOn).Take(500)
                .Select(x => new { x.Id, x.ResidentId, x.SpaceId, x.StartsOn, x.EndsOn, x.MonthlyRent, x.SecurityDeposit, x.Status, x.NoticeDate, x.MoveOutOn }).ToListAsync(ct)));
        group.MapGet("/{id:guid}", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var lease = await store.Leases.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.ResidentId, x.SpaceId, x.StartsOn, x.EndsOn, x.MonthlyRent, x.SecurityDeposit, x.Status, x.NoticeDate, x.MoveOutOn }).SingleOrDefaultAsync(ct);
            return lease is null ? Results.NotFound() : Results.Ok(lease);
        });
        group.MapPost("/", async (LeaseRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Residents.AnyAsync(x => x.Id == request.ResidentId, ct)) return Results.Problem(statusCode: 400, title: "Resident was not found");
            if (!await store.Spaces.AnyAsync(x => x.Id == request.SpaceId, ct)) return Results.Problem(statusCode: 400, title: "Space was not found");
            if (await store.Leases.AnyAsync(x => x.ResidentId == request.ResidentId && x.SpaceId == request.SpaceId && x.Status != LeaseStatus.Ended, ct)) return Results.Conflict("An active lease already exists for this resident and space.");
            var lease = new Lease(store.OrganizationId, Guid.NewGuid(), request.ResidentId, request.SpaceId, request.StartsOn, request.EndsOn, request.MonthlyRent, request.SecurityDeposit);
            store.Leases.Add(lease); await store.SaveChangesAsync(ct); return Results.Created($"/api/leasing/leases/{lease.Id}", lease);
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/activate", async (Guid id, OperationsStore store, CancellationToken ct) =>
        {
            var lease = await store.Leases.SingleOrDefaultAsync(x => x.Id == id, ct); if (lease is null) return Results.NotFound();
            var current = await store.Occupancies.Where(x => x.ResidentId == lease.ResidentId && x.MovedOutOn == null).SingleOrDefaultAsync(ct);
            if (current is not null && current.SpaceId != lease.SpaceId) return Results.Conflict("The resident already has an active occupancy in another space.");
            lease.Activate();
            if (current is null) store.Occupancies.Add(new Occupancy(store.OrganizationId, Guid.NewGuid(), lease.ResidentId, lease.SpaceId, lease.StartsOn));
            await store.SaveChangesAsync(ct); return Results.Ok(lease);
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/renew", async (Guid id, RenewLeaseRequest request, OperationsStore store, CancellationToken ct) => await Change(id, store, ct, lease => lease.Renew(request.EndsOn, request.MonthlyRent))).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/notice", async (Guid id, NoticeRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var lease = await store.Leases.SingleOrDefaultAsync(x => x.Id == id, ct); if (lease is null) return Results.NotFound();
            lease.GiveNotice(request.NoticeDate, request.MoveOutOn); var notice = new LeaseNotice(store.OrganizationId, Guid.NewGuid(), id, request.Type, request.MoveOutOn, request.Notes); store.LeaseNotices.Add(notice); await store.SaveChangesAsync(ct); return Results.Ok(new { lease, notice });
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/move-out", async (Guid id, MoveOutRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var lease = await store.Leases.SingleOrDefaultAsync(x => x.Id == id, ct); if (lease is null) return Results.NotFound();
            lease.End(request.MoveOutOn);
            var occupancy = await store.Occupancies.SingleOrDefaultAsync(x => x.ResidentId == lease.ResidentId && x.SpaceId == lease.SpaceId && x.MovedOutOn == null, ct);
            occupancy?.EndOn(request.MoveOutOn);
            await store.SaveChangesAsync(ct); return Results.Ok(lease);
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/transfer", async (Guid id, TransferLeaseRequest request, OperationsStore store, CancellationToken ct) =>
        {
            var lease = await store.Leases.SingleOrDefaultAsync(x => x.Id == id, ct); if (lease is null) return Results.NotFound();
            if (!await store.Residents.AnyAsync(x => x.Id == request.ResidentId, ct)) return Results.Problem(statusCode: 400, title: "Resident was not found");
            if (!await store.Spaces.AnyAsync(x => x.Id == request.SpaceId, ct)) return Results.Problem(statusCode: 400, title: "Space was not found");
            if (await store.Leases.AnyAsync(x => x.Id != id && x.ResidentId == request.ResidentId && x.Status != LeaseStatus.Ended, ct)) return Results.Conflict("The target resident already has an active lease.");
            if (await store.Occupancies.AnyAsync(x => x.SpaceId == request.SpaceId && x.MovedOutOn == null, ct)) return Results.Conflict("The target space is occupied.");
            var oldOccupancy = await store.Occupancies.SingleOrDefaultAsync(x => x.ResidentId == lease.ResidentId && x.SpaceId == lease.SpaceId && x.MovedOutOn == null, ct);
            try
            {
                lease.Transfer(request.ResidentId, request.SpaceId);
                oldOccupancy?.EndOn(request.EffectiveOn);
                store.Occupancies.Add(new Occupancy(store.OrganizationId, Guid.NewGuid(), request.ResidentId, request.SpaceId, request.EffectiveOn));
                await store.SaveChangesAsync(ct);
                return Results.Ok(lease);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapGet("/{id:guid}/notices", async (Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.LeaseNotices.AsNoTracking().Where(x => x.LeaseId == id).OrderBy(x => x.DueOn).ToListAsync(ct)));
        group.MapGet("/{id:guid}/parties", async (Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.LeaseParties.AsNoTracking().Where(x => x.LeaseId == id).OrderBy(x => x.FullName).ToListAsync(ct)));
        group.MapPost("/{id:guid}/parties", async (Guid id, LeasePartyRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Leases.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            try
            {
                var party = new LeaseParty(store.OrganizationId, Guid.NewGuid(), id, request.FullName, request.Role, request.Email);
                store.LeaseParties.Add(party); await store.SaveChangesAsync(ct); return Results.Created($"/api/leasing/leases/{id}/parties/{party.Id}", party);
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapGet("/{id:guid}/documents", async (Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.LeaseDocuments.AsNoTracking().Where(x => x.LeaseId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)));
        group.MapPost("/{id:guid}/documents", async (Guid id, LeaseDocumentRequest request, OperationsStore store, CancellationToken ct) =>
        {
            if (!await store.Leases.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            try { var document = new LeaseDocument(store.OrganizationId, Guid.NewGuid(), id, request.Title, request.DocumentUrl, request.ResidentVisible); store.LeaseDocuments.Add(document); await store.SaveChangesAsync(ct); return Results.Created($"/api/leasing/leases/{id}/documents/{document.Id}", document); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/documents/{documentId:guid}/send", async (Guid id, Guid documentId, OperationsStore store, CancellationToken ct) => await ChangeDocument(id, documentId, store, ct, document => document.Send())).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapPost("/{id:guid}/documents/{documentId:guid}/sign", async (Guid id, Guid documentId, SignLeaseDocumentRequest request, OperationsStore store, CancellationToken ct) => await ChangeDocument(id, documentId, store, ct, document => document.Sign(request.SignedBy))).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapGet("/{id:guid}/charges", async (Guid id, OperationsStore store, CancellationToken ct) => Results.Ok(await store.LeaseCharges.AsNoTracking().Where(x => x.LeaseId == id).OrderBy(x => x.DueOn).ToListAsync(ct)));
        group.MapPost("/{id:guid}/charges", async (Guid id, LeaseChargeRequest request, OperationsStore store, CancellationToken ct) =>
        { if (!await store.Leases.AnyAsync(x => x.Id == id, ct)) return Results.NotFound(); try { var charge = new LeaseCharge(store.OrganizationId, Guid.NewGuid(), id, request.Type, request.Description, request.Amount, request.DueOn); store.LeaseCharges.Add(charge); await store.SaveChangesAsync(ct); return Results.Created($"/api/leasing/leases/{id}/charges/{charge.Id}", charge); } catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); } }).RequireAuthorization(Capabilities.ManageLeasing);
        group.MapGet("/payments", async (OperationsStore store, CancellationToken ct) => Results.Ok(await store.ResidentPayments.AsNoTracking().OrderByDescending(x => x.SubmittedAt).Take(500).ToListAsync(ct)));
        group.MapPost("/payments/{paymentId:guid}/settle", async (Guid paymentId, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var payment = await store.ResidentPayments.SingleOrDefaultAsync(x => x.Id == paymentId, ct); if (payment is null) return Results.NotFound();
            try
            {
                payment.Settle(clock.GetUtcNow());
                if (payment.ChargeId is { } chargeId)
                {
                    var charge = await store.LeaseCharges.SingleOrDefaultAsync(x => x.Id == chargeId, ct);
                    if (charge is null) return Results.Problem(statusCode: 409, title: "The linked charge no longer exists.");
                    // FS-S08: settling a full payment applies its amount rather than flipping a
                    // flag, so the charge's AmountApplied stays the ledger of what was paid.
                    charge.Apply(Math.Min(payment.Amount, charge.Outstanding));
                }
                await store.SaveChangesAsync(ct); return Results.Ok(payment);
            }
            catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ManageLeasing);
    }

    private static async Task<IResult> Change(Guid id, OperationsStore store, CancellationToken ct, Action<Lease> change)
    {
        var lease = await store.Leases.SingleOrDefaultAsync(x => x.Id == id, ct); if (lease is null) return Results.NotFound(); change(lease); await store.SaveChangesAsync(ct); return Results.Ok(lease);
    }

    private static async Task<IResult> ChangeDocument(Guid leaseId, Guid documentId, OperationsStore store, CancellationToken ct, Action<LeaseDocument> change)
    {
        var document = await store.LeaseDocuments.SingleOrDefaultAsync(x => x.Id == documentId && x.LeaseId == leaseId, ct);
        if (document is null) return Results.NotFound();
        try { change(document); await store.SaveChangesAsync(ct); return Results.Ok(document); }
        catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
        catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
    }

    private static async Task<IResult> ChangePayment(Guid paymentId, OperationsStore store, CancellationToken ct, Action<ResidentPayment> change)
    {
        var payment = await store.ResidentPayments.SingleOrDefaultAsync(x => x.Id == paymentId, ct); if (payment is null) return Results.NotFound();
        try { change(payment); await store.SaveChangesAsync(ct); return Results.Ok(payment); }
        catch (InvalidOperationException exception) { return Results.Problem(statusCode: 409, title: exception.Message); }
    }
}

public sealed record LeaseRequest(Guid ResidentId, Guid SpaceId, DateOnly StartsOn, DateOnly EndsOn, decimal MonthlyRent, decimal? SecurityDeposit);
public sealed record RenewLeaseRequest(DateOnly EndsOn, decimal MonthlyRent);
public sealed record NoticeRequest(LeaseNoticeType Type, DateOnly NoticeDate, DateOnly MoveOutOn, string? Notes);
public sealed record MoveOutRequest(DateOnly MoveOutOn);
public sealed record TransferLeaseRequest(Guid ResidentId, Guid SpaceId, DateOnly EffectiveOn);
public sealed record LeasePartyRequest(string FullName, string Role, string? Email);
public sealed record LeaseDocumentRequest(string Title, string DocumentUrl, bool ResidentVisible);
public sealed record SignLeaseDocumentRequest(string SignedBy);
public sealed record LeaseChargeRequest(LeaseChargeType Type, string Description, decimal Amount, DateOnly DueOn);
