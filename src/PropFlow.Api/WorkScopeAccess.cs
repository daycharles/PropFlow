using System.Security.Claims;
using PropFlow.Application;
using PropFlow.Application.Work;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Api;

// The field-role scoping check, shared by every endpoint that resolves a single work item for a
// caller who might be a Technician or Vendor. A non-field role has no subject and sees everything
// its capability allows; a field role is narrowed to the property/assignment its membership names
// (WorkAccessScope.Allows). Kept in one place so WorkEndpoints and AttachmentEndpoints cannot
// drift apart on what "your assigned work" means.
internal static class WorkScopeAccess
{
    public static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static async Task<(WorkAccessScope? Scope, WorkScopeSubject? Subject)> ScopeAsync(
        ClaimsPrincipal user, MembershipAccess memberships, CancellationToken ct)
    {
        var subject = user.FindFirstValue("organization_role") switch
        {
            "Technician" => WorkScopeSubject.Technician,
            "Vendor" => WorkScopeSubject.Vendor,
            _ => (WorkScopeSubject?)null,
        };
        if (subject is null) return (null, null);
        return (await memberships.WorkScopeAsync(Actor(user), TenantAccess.Resolve(user), ct), subject);
    }

    public static async Task<bool> AllowsAsync(WorkItem item, ClaimsPrincipal user, MembershipAccess memberships, CancellationToken ct)
    {
        var (scope, subject) = await ScopeAsync(user, memberships, ct);
        return subject is null || scope!.Allows(item.PropertyId, item.EmployeeId, item.VendorId, subject.Value);
    }
}
