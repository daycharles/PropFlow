using Microsoft.AspNetCore.Identity;

namespace PropFlow.Infrastructure.Identity;

// Identity is a control plane: a person can have memberships in multiple organizations.
public sealed class ApplicationUser : IdentityUser<Guid> { }

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class OrganizationMembership
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Read Only";
    public bool IsActive { get; set; } = true;
    // Field identities are deliberately bound to the membership, not to a global user profile:
    // one person may represent different vendors/employees for different organizations.
    public Guid? EmployeeId { get; set; }
    public Guid? VendorId { get; set; }
    public Guid? ResidentId { get; set; }
}

/// <summary>Optional property narrowing for a field membership. No cross-schema FK is used:
/// Operations is tenant data while Identity is the control plane.</summary>
public sealed class MembershipPropertyBinding
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid PropertyId { get; set; }
}
