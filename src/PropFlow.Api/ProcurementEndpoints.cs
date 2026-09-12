using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Accounting;
using PropFlow.Domain.People;
using PropFlow.Domain.Procurement;
using PropFlow.Domain.Timeline;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class ProcurementEndpoints
{
    public static void MapProcurementEndpoints(this WebApplication app)
    {
        var read = app.MapGroup("/api/procurement").RequireAuthorization(Capabilities.ReadProcurement);
        read.MapGet("/vendors/{vendorId:guid}/profile", async (Guid vendorId, OperationsStore s, CancellationToken ct) => Results.Ok(await s.VendorProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.VendorId == vendorId, ct)));
        read.MapGet("/vendors/{vendorId:guid}/documents", async (Guid vendorId, OperationsStore s, CancellationToken ct) => Results.Ok(await s.VendorDocuments.AsNoTracking().Where(x => x.VendorId == vendorId).OrderBy(x => x.ExpiresOn).ToListAsync(ct)));
        read.MapGet("/vendors/{vendorId:guid}/contracts", async (Guid vendorId, OperationsStore s, CancellationToken ct) => Results.Ok(await s.VendorContracts.AsNoTracking().Where(x => x.VendorId == vendorId).OrderByDescending(x => x.StartsOn).ToListAsync(ct)));
        read.MapGet("/vendors/{vendorId:guid}/rate-cards", async (Guid vendorId, OperationsStore s, CancellationToken ct) => Results.Ok(await s.VendorRateCards.AsNoTracking().Where(x => x.VendorId == vendorId).OrderBy(x => x.ServiceCode).ToListAsync(ct)));
        read.MapGet("/vendors/{vendorId:guid}/performance", async (Guid vendorId, OperationsStore s, CancellationToken ct) => Results.Ok(await s.VendorPerformanceReviews.AsNoTracking().Where(x => x.VendorId == vendorId).OrderByDescending(x => x.ReviewedAt).ToListAsync(ct)));
        read.MapGet("/bids", async (Guid? workItemId, OperationsStore s, CancellationToken ct) => Results.Ok(await s.ProcurementBids.AsNoTracking().Where(x => workItemId == null || x.WorkItemId == workItemId).OrderBy(x => x.Amount).Take(500).ToListAsync(ct)));
        read.MapGet("/purchase-orders", async (OperationsStore s, CancellationToken ct) => Results.Ok(await s.PurchaseOrders.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct)));
        read.MapGet("/authorizations", async (OperationsStore s, CancellationToken ct) => Results.Ok(await s.WorkAuthorizations.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct)));

        var write = app.MapGroup("/api/procurement").RequireAuthorization(Capabilities.ManageProcurement);
        write.MapPut("/vendors/{vendorId:guid}/profile", async (Guid vendorId, VendorProfileRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await s.Vendors.AnyAsync(x => x.Id == vendorId, ct)) return Results.NotFound();
            var p = await s.VendorProfiles.SingleOrDefaultAsync(x => x.VendorId == vendorId, ct);
            if (p is null) { p = new VendorProfile(s.OrganizationId, Guid.NewGuid(), vendorId, clock.GetUtcNow()); s.VendorProfiles.Add(p); }
            p.Update(r.TaxIdentifier, r.Address, r.Notes); await s.SaveChangesAsync(ct); return Results.Ok(p);
        });
        write.MapPost("/vendors/{vendorId:guid}/documents", async (Guid vendorId, VendorDocumentRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await s.Vendors.AnyAsync(x => x.Id == vendorId && x.IsActive, ct)) return Results.NotFound();
            try { var d = new VendorDocument(s.OrganizationId, Guid.NewGuid(), vendorId, r.Type, r.DocumentNumber, r.ExpiresOn, clock.GetUtcNow()); s.VendorDocuments.Add(d); await s.SaveChangesAsync(ct); return Results.Created($"/api/procurement/vendors/{vendorId}/documents/{d.Id}", d); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        write.MapPost("/vendors/{vendorId:guid}/contracts", async (Guid vendorId, VendorContractRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await s.Vendors.AnyAsync(x => x.Id == vendorId && x.IsActive, ct)) return Results.NotFound();
            try { var c = new VendorContract(s.OrganizationId, Guid.NewGuid(), vendorId, r.Name, r.StartsOn, r.EndsOn, clock.GetUtcNow()); s.VendorContracts.Add(c); await s.SaveChangesAsync(ct); return Results.Created($"/api/procurement/vendors/{vendorId}/contracts/{c.Id}", c); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        write.MapPost("/vendors/{vendorId:guid}/rate-cards", async (Guid vendorId, VendorRateCardRequest r, OperationsStore s, CancellationToken ct) =>
        {
            if (!await s.Vendors.AnyAsync(x => x.Id == vendorId && x.IsActive, ct)) return Results.NotFound();
            try { var c = new VendorRateCard(s.OrganizationId, Guid.NewGuid(), vendorId, r.ServiceCode, r.UnitRate, r.EffectiveOn, r.ExpiresOn); s.VendorRateCards.Add(c); await s.SaveChangesAsync(ct); return Results.Created($"/api/procurement/vendors/{vendorId}/rate-cards/{c.Id}", c); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        write.MapPost("/bids", async (BidRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await s.Vendors.AnyAsync(x => x.Id == r.VendorId && x.IsActive, ct)) return Results.NotFound();
            if (r.WorkItemId is not null && !await s.WorkItems.AnyAsync(x => x.Id == r.WorkItemId, ct)) return Results.BadRequest("Work item was not found.");
            try { var b = new ProcurementBid(s.OrganizationId, Guid.NewGuid(), r.VendorId, r.WorkItemId, r.Title, r.Amount, clock.GetUtcNow()); s.ProcurementBids.Add(b); await s.SaveChangesAsync(ct); return Results.Created($"/api/procurement/bids/{b.Id}", b); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        write.MapPost("/bids/{id:guid}/select", async (Guid id, OperationsStore s, CancellationToken ct) =>
        {
            var bid = await s.ProcurementBids.SingleOrDefaultAsync(x => x.Id == id, ct); if (bid is null) return Results.NotFound();
            var bids = await s.ProcurementBids.Where(x => x.WorkItemId == bid.WorkItemId).ToListAsync(ct); bids.ForEach(x => { if (x.Id == id) x.Select(); }); await s.SaveChangesAsync(ct); return Results.Ok(bid);
        });
        write.MapPost("/purchase-orders", async (PurchaseOrderRequest r, ClaimsPrincipal user, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            var vendor = await s.Vendors.SingleOrDefaultAsync(x => x.Id == r.VendorId, ct); if (vendor is null || !vendor.IsActive) return Results.NotFound();
            if (r.PropertyId is not null && !await s.Properties.AnyAsync(x => x.Id == r.PropertyId, ct)) return Results.BadRequest("Property was not found.");
            try { var p = new PurchaseOrder(s.OrganizationId, Guid.NewGuid(), r.VendorId, r.PropertyId, r.Number, r.Amount, r.ApprovalThreshold, clock.GetUtcNow()); s.PurchaseOrders.Add(p); Audit(s, user, "PurchaseOrderCreated", p.Id, clock.GetUtcNow()); await s.SaveChangesAsync(ct); return Results.Created($"/api/procurement/purchase-orders/{p.Id}", p); }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        write.MapPost("/purchase-orders/{id:guid}/submit", async (Guid id, ClaimsPrincipal user, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            var p = await s.PurchaseOrders.SingleOrDefaultAsync(x => x.Id == id, ct); if (p is null) return Results.NotFound();
            if (await s.VendorDocuments.AnyAsync(x => x.VendorId == p.VendorId && x.IsActive && (x.Type == VendorDocumentType.Insurance || x.Type == VendorDocumentType.License) && x.ExpiresOn < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct)) return Results.Problem(statusCode: 409, title: "Vendor has an expired active insurance or license document.");
            try { p.Submit(); Audit(s, user, "PurchaseOrderSubmitted", p.Id, clock.GetUtcNow()); await s.SaveChangesAsync(ct); return Results.Ok(p); } catch (InvalidOperationException e) { return Results.Conflict(e.Message); }
        });
        write.MapPost("/purchase-orders/{id:guid}/approve", async (Guid id, ClaimsPrincipal user, OperationsStore s, TimeProvider clock, CancellationToken ct) => await ChangePo(id, s, user, clock, ct, p => p.Approve(WorkScopeAccess.Actor(user), clock.GetUtcNow())));
        write.MapPost("/purchase-orders/{id:guid}/issue", async (Guid id, ClaimsPrincipal user, OperationsStore s, TimeProvider clock, CancellationToken ct) => await ChangePo(id, s, user, clock, ct, p => p.Issue()));
        write.MapPost("/purchase-orders/{id:guid}/match-invoice", async (Guid id, InvoiceMatchRequest r, ClaimsPrincipal user, OperationsStore s, TimeProvider clock, CancellationToken ct) =>
        {
            var p = await s.PurchaseOrders.SingleOrDefaultAsync(x => x.Id == id, ct); if (p is null) return Results.NotFound();
            if (!await s.PayableInvoices.AnyAsync(x => x.Id == r.PayableInvoiceId && x.VendorId == p.VendorId, ct)) return Results.BadRequest("Invoice was not found for this vendor.");
            if (await s.PurchaseOrderInvoiceMatches.AnyAsync(x => x.PurchaseOrderId == id && x.PayableInvoiceId == r.PayableInvoiceId, ct)) return Results.Conflict("Invoice is already matched.");
            try { p.Match(r.Amount); s.PurchaseOrderInvoiceMatches.Add(new PurchaseOrderInvoiceMatch(s.OrganizationId, Guid.NewGuid(), id, r.PayableInvoiceId, r.Amount, clock.GetUtcNow())); Audit(s, user, "PurchaseOrderInvoiceMatched", id, clock.GetUtcNow()); await s.SaveChangesAsync(ct); return Results.Ok(p); } catch (InvalidOperationException e) { return Results.Conflict(e.Message); }
        });
        write.MapPost("/authorizations", async (AuthorizationRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) => { if (!await s.Vendors.AnyAsync(x => x.Id == r.VendorId, ct) || !await s.WorkItems.AnyAsync(x => x.Id == r.WorkItemId, ct)) return Results.BadRequest("Vendor or work item was not found."); var a = new WorkAuthorization(s.OrganizationId, Guid.NewGuid(), r.VendorId, r.WorkItemId, r.Amount, clock.GetUtcNow()); s.WorkAuthorizations.Add(a); await s.SaveChangesAsync(ct); return Results.Created($"/api/procurement/authorizations/{a.Id}", a); });
        write.MapPost("/authorizations/{id:guid}/approve", async (Guid id, OperationsStore s, CancellationToken ct) => { var a = await s.WorkAuthorizations.SingleOrDefaultAsync(x => x.Id == id, ct); if (a is null) return Results.NotFound(); try { a.Approve(); await s.SaveChangesAsync(ct); return Results.Ok(a); } catch (InvalidOperationException e) { return Results.Conflict(e.Message); } });
        write.MapPost("/vendors/{vendorId:guid}/performance", async (Guid vendorId, PerformanceRequest r, OperationsStore s, TimeProvider clock, CancellationToken ct) => { if (!await s.Vendors.AnyAsync(x => x.Id == vendorId, ct)) return Results.NotFound(); var p = new VendorPerformanceReview(s.OrganizationId, Guid.NewGuid(), vendorId, r.Score, r.Notes, clock.GetUtcNow()); s.VendorPerformanceReviews.Add(p); await s.SaveChangesAsync(ct); return Results.Created($"/api/procurement/vendors/{vendorId}/performance/{p.Id}", p); });
    }
    private static async Task<IResult> ChangePo(Guid id, OperationsStore s, ClaimsPrincipal user, TimeProvider clock, CancellationToken ct, Action<PurchaseOrder> change) { var p = await s.PurchaseOrders.SingleOrDefaultAsync(x => x.Id == id, ct); if (p is null) return Results.NotFound(); try { change(p); Audit(s, user, "PurchaseOrderChanged", id, clock.GetUtcNow()); await s.SaveChangesAsync(ct); return Results.Ok(p); } catch (InvalidOperationException e) { return Results.Conflict(e.Message); } catch (ArgumentException e) { return Results.BadRequest(e.Message); } }
    private static void Audit(OperationsStore s, ClaimsPrincipal u, string type, Guid id, DateTimeOffset at) => s.Timeline.Add(TimelineEntry.Record(s.OrganizationId, WorkScopeAccess.Actor(u), at, type, "Procurement", id, null, null));
}
public sealed record VendorProfileRequest(string? TaxIdentifier, string? Address, string? Notes);
public sealed record VendorDocumentRequest(VendorDocumentType Type, string DocumentNumber, DateOnly ExpiresOn);
public sealed record VendorContractRequest(string Name, DateOnly StartsOn, DateOnly? EndsOn);
public sealed record VendorRateCardRequest(string ServiceCode, decimal UnitRate, DateOnly EffectiveOn, DateOnly? ExpiresOn);
public sealed record BidRequest(Guid VendorId, Guid? WorkItemId, string Title, decimal Amount);
public sealed record PurchaseOrderRequest(Guid VendorId, Guid? PropertyId, string Number, decimal Amount, decimal ApprovalThreshold);
public sealed record InvoiceMatchRequest(Guid PayableInvoiceId, decimal Amount);
public sealed record AuthorizationRequest(Guid VendorId, Guid WorkItemId, decimal Amount);
public sealed record PerformanceRequest(int Score, string? Notes);
