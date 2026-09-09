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
}
