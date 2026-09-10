using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Work;

namespace PropFlow.Infrastructure.Identity;

public sealed class MembershipAccess(IdentityStore store)
{
    public Task<OrganizationMembership?> FindActiveAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        (from membership in store.Memberships.AsNoTracking()
         join organization in store.Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id
         where membership.UserId == userId && membership.OrganizationId == organizationId
             && membership.IsActive && organization.IsActive
         select membership).SingleOrDefaultAsync(cancellationToken);

    public Task<OrganizationMembership?> FindActiveBySlugAsync(Guid userId, string slug, CancellationToken cancellationToken) =>
        (from membership in store.Memberships.AsNoTracking()
         join organization in store.Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id
         where membership.UserId == userId && membership.IsActive && organization.IsActive && organization.Slug == slug
         select membership).SingleOrDefaultAsync(cancellationToken);

    public Task<Organization?> OrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        store.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organizationId, cancellationToken);

    public async Task<WorkAccessScope> WorkScopeAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var membership = await FindActiveAsync(userId, organizationId, cancellationToken);
        if (membership is null) return WorkAccessScope.None;
        var propertyIds = await store.MembershipPropertyBindings.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.UserId == userId)
            .Select(x => x.PropertyId).ToHashSetAsync(cancellationToken);
        return new WorkAccessScope(propertyIds, membership.EmployeeId, membership.VendorId);
    }
}
