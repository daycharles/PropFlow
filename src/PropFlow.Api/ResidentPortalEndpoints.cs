using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Leasing;
using PropFlow.Domain.Work;
using PropFlow.Application.Attachments;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public static class ResidentPortalEndpoints
{
    public static void MapResidentPortalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/portal").RequireAuthorization(Capabilities.ResidentPortalRead);
        group.MapGet("/me", async (ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var resident = await store.Residents.AsNoTracking().Where(x => x.Id == residentId).Select(x => new { x.Id, x.FullName, x.Email, x.Phone, SmsConsent = x.SmsConsent.ToString(), EmailConsent = x.EmailConsent.ToString() }).SingleOrDefaultAsync(ct);
            if (resident is null) return Results.NotFound();
            var occupancy = await store.Occupancies.AsNoTracking().Where(x => x.ResidentId == residentId && x.MovedOutOn == null).OrderByDescending(x => x.MovedInOn).Select(x => new { x.Id, x.SpaceId, x.MovedInOn }).FirstOrDefaultAsync(ct);
            var leases = await store.Leases.AsNoTracking().Where(x => x.ResidentId == residentId).OrderByDescending(x => x.StartsOn).Select(x => new { x.Id, x.SpaceId, x.StartsOn, x.EndsOn, x.MonthlyRent, x.Status, x.MoveOutOn }).ToListAsync(ct);
            var householdMembers = await store.HouseholdMembers.AsNoTracking().Where(x => x.ResidentId == residentId).OrderBy(x => x.FullName).Select(x => new { x.Id, x.FullName, x.Relationship, x.Email }).ToListAsync(ct);
            var requests = await store.WorkItems.AsNoTracking().Where(x => x.ResidentId == residentId).OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.Title, x.Description, x.Status, x.Priority, x.ResidentVisibleNotes, x.CreatedAt, x.ScheduledStart, x.ScheduledEnd }).ToListAsync(ct);
            return Results.Ok(new { resident, occupancy, leases, householdMembers, requests });
        });
        group.MapPut("/profile", async (PortalProfileRequest request, ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var resident = await store.Residents.SingleOrDefaultAsync(x => x.Id == residentId, ct); if (resident is null) return Results.NotFound();
            try { resident.Rename(request.FullName); resident.UpdateContact(request.Email, request.Phone); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
            await store.SaveChangesAsync(ct);
            return Results.Ok(new { resident.Id, resident.FullName, resident.Email, resident.Phone, SmsConsent = resident.SmsConsent.ToString(), EmailConsent = resident.EmailConsent.ToString() });
        }).RequireAuthorization(Capabilities.ResidentPortalRequest);
        group.MapPost("/household-members", async (HouseholdMemberRequest request, ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            try
            {
                var member = new PropFlow.Domain.People.HouseholdMember(store.OrganizationId, Guid.NewGuid(), residentId.Value, request.FullName, request.Relationship, request.Email);
                store.HouseholdMembers.Add(member); await store.SaveChangesAsync(ct);
                return Results.Created($"/api/portal/household-members/{member.Id}", new { member.Id, member.FullName, member.Relationship, member.Email });
            }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ResidentPortalRequest);
        group.MapPut("/preferences", async (PortalPreferencesRequest request, ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var resident = await store.Residents.SingleOrDefaultAsync(x => x.Id == residentId, ct); if (resident is null) return Results.NotFound();
            resident.SetConsent(MessageChannel.Email, request.EmailEnabled, clock.GetUtcNow());
            resident.SetConsent(MessageChannel.Sms, request.SmsEnabled, clock.GetUtcNow());
            await store.SaveChangesAsync(ct);
            return Results.Ok(new { emailEnabled = request.EmailEnabled, smsEnabled = request.SmsEnabled });
        }).RequireAuthorization(Capabilities.ResidentPortalRequest);
        group.MapGet("/documents", async (ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var documents = await (from attachment in store.Attachments.AsNoTracking()
                                   join work in store.WorkItems.AsNoTracking() on new { attachment.OrganizationId, WorkId = attachment.WorkId } equals new { work.OrganizationId, WorkId = work.Id }
                                   where work.ResidentId == residentId && attachment.ResidentVisible
                                   orderby attachment.CreatedAt descending
                                   select new { attachment.Id, attachment.WorkId, attachment.FileName, attachment.ContentType, attachment.Length, attachment.CreatedAt, DownloadUrl = $"/api/portal/documents/{attachment.Id}" })
                .Take(200).ToListAsync(ct);
            return Results.Ok(documents);
        });
        group.MapGet("/documents/{attachmentId:guid}", async (Guid attachmentId, ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, IAttachmentStorage storage, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var attachment = await (from item in store.Attachments.AsNoTracking()
                                    join work in store.WorkItems.AsNoTracking() on new { item.OrganizationId, WorkId = item.WorkId } equals new { work.OrganizationId, WorkId = work.Id }
                                    where item.Id == attachmentId && item.ResidentVisible && work.ResidentId == residentId
                                    select item).SingleOrDefaultAsync(ct);
            if (attachment is null) return Results.NotFound();
            var content = await storage.OpenReadAsync(attachment.StorageKey, ct);
            return content is null ? Results.NotFound() : Results.File(content, attachment.ContentType, attachment.FileName);
        });
        group.MapGet("/lease-documents", async (ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var documents = await (from document in store.LeaseDocuments.AsNoTracking()
                                   join lease in store.Leases.AsNoTracking() on new { document.OrganizationId, LeaseId = document.LeaseId } equals new { lease.OrganizationId, LeaseId = lease.Id }
                                   where lease.ResidentId == residentId && document.ResidentVisible
                                   orderby document.CreatedAt descending
                                   select new { document.Id, document.LeaseId, document.Title, document.DocumentUrl, document.Status, document.CreatedAt, document.SignedAt, document.SignedBy })
                .Take(200).ToListAsync(ct);
            return Results.Ok(documents);
        });
        group.MapGet("/payments", async (ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            return Results.Ok(await store.ResidentPayments.AsNoTracking().Where(x => x.ResidentId == residentId).OrderByDescending(x => x.DueOn).Take(200).ToListAsync(ct));
        });
        group.MapGet("/charges", async (ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        { var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid(); return Results.Ok(await (from charge in store.LeaseCharges.AsNoTracking() join lease in store.Leases.AsNoTracking() on new { charge.OrganizationId, LeaseId = charge.LeaseId } equals new { lease.OrganizationId, LeaseId = lease.Id } where lease.ResidentId == residentId orderby charge.DueOn select charge).Take(200).ToListAsync(ct)); });
        group.MapPost("/payments", async (ResidentPaymentRequest request, ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var lease = await store.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.LeaseId && x.ResidentId == residentId, ct); if (lease is null) return Results.NotFound();
            try { var payment = new ResidentPayment(store.OrganizationId, Guid.NewGuid(), lease.Id, residentId.Value, request.Amount, request.DueOn, request.Reference); store.ResidentPayments.Add(payment); await store.SaveChangesAsync(ct); return Results.Created($"/api/portal/payments/{payment.Id}", payment); }
            catch (ArgumentException exception) { return Results.Problem(statusCode: 400, title: exception.Message); }
        }).RequireAuthorization(Capabilities.ResidentPortalRequest);
        group.MapPost("/service-requests", async (ServiceRequest request, ClaimsPrincipal user, MembershipAccess memberships, OperationsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var residentId = await ResidentId(user, memberships, ct); if (residentId is null) return Results.Forbid();
            var space = await store.Spaces.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SpaceId, ct); if (space is null) return Results.NotFound();
            if (!await store.Occupancies.AnyAsync(x => x.ResidentId == residentId && x.SpaceId == request.SpaceId && x.MovedOutOn == null, ct)) return Results.Forbid();
            var work = new WorkItem(store.OrganizationId, Guid.NewGuid(), request.Title, space.PropertyId, residentId.Value, WorkType.MaintenanceRequest);
            work.SetLocation(space.PropertyId, space.BuildingId, space.Id, residentId); work.Edit(request.Title, request.Description, null, WorkPriority.Normal); work.SetNotes(null, request.Description); work.Publish(clock.GetUtcNow());
            store.WorkItems.Add(work); await store.SaveChangesAsync(ct); return Results.Created($"/api/portal/service-requests/{work.Id}", new { work.Id, work.Title, work.Status, work.ResidentVisibleNotes });
        }).RequireAuthorization(Capabilities.ResidentPortalRequest);
    }

    private static async Task<Guid?> ResidentId(ClaimsPrincipal user, MembershipAccess memberships, CancellationToken ct)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier); if (!Guid.TryParse(value, out var userId)) return null;
        var organization = user.FindFirstValue(TenantAccess.OrganizationClaim); if (!Guid.TryParse(organization, out var organizationId)) return null;
        return (await memberships.FindActiveAsync(userId, organizationId, ct))?.ResidentId;
    }
}

public sealed record ServiceRequest(Guid SpaceId, string Title, string? Description);
public sealed record PortalProfileRequest(string FullName, string? Email, string? Phone);
public sealed record PortalPreferencesRequest(bool EmailEnabled, bool SmsEnabled);
public sealed record HouseholdMemberRequest(string FullName, string Relationship, string? Email);
public sealed record ResidentPaymentRequest(Guid LeaseId, decimal Amount, DateOnly DueOn, string? Reference);
