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
}

/// <summary>Optional property narrowing for a field membership. No cross-schema FK is used:
/// Operations is tenant data while Identity is the control plane.</summary>
public sealed class MembershipPropertyBinding
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid PropertyId { get; set; }
}

/// <summary>PF-S01.02: a pending grant of membership to an organization, sent to an email
/// address rather than an existing user. <see cref="Token"/> and <see cref="Email"/> are stored
/// in the canonical form <c>InvitationToken</c> produces; acceptance re-derives the same
/// canonical email rather than trusting whatever case/whitespace the acceptor's client sends.
/// A membership is created only on acceptance (<see cref="AcceptedAt"/> set) — an unaccepted or
/// expired invitation grants nothing.</summary>
public sealed class Invitation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Read Only";
    public string Token { get; set; } = "";
    public Guid InvitedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    // Set only once accepted, distinguishing "accepted by a brand-new user" from "accepted by an
    // existing user gaining a new organization membership" without a second lookup.
    public Guid? AcceptedByUserId { get; set; }
}
