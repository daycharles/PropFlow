using Microsoft.EntityFrameworkCore;

namespace PropFlow.Infrastructure.Identity;

public sealed class MembershipAccess(IdentityStore store)
{
    public Task<OrganizationMembership?> FindActiveAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        (from membership in store.Memberships.AsNoTracking()
         join organization in store.Organizations.AsNoTracking() on membership.OrganizationId equals organization.Id
         where membership.UserId == userId && membership.OrganizationId == organizationId
             && membership.IsActive && organization.IsActive
         select membership).SingleOrDefaultAsync(cancellationToken);
}
