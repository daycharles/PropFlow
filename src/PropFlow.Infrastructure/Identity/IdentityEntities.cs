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

/// <summary>PF-S01.04: a per-organization addition to a role's built-in default capabilities
/// (<see cref="Application.Capabilities.ForRole"/>). <see cref="IsRevocation"/> distinguishes a
/// grant (a capability the defaults do not give this role) from a revocation (a default
/// capability withheld for this organization); see <c>RoleCapabilityMatrix.Effective</c> for how
/// the two combine. There is deliberately one row shape for both rather than two tables, since an
/// organization's customization of one role is naturally edited as a single list.</summary>
public sealed class RoleCapabilityOverride
{
    public Guid OrganizationId { get; set; }
    public string Role { get; set; } = "";
    public string Capability { get; set; } = "";
    public bool IsRevocation { get; set; }
}

/// <summary>PF-S01.05: a named group of memberships within one organization (e.g. "Norfolk
/// Turnover Crew"). Teams are a grouping/addressing construct only in this pass - narrowing work
/// or property access by team membership is follow-up work (noted in full-suite-scope.md), not
/// silently implied by a row existing here.</summary>
public sealed class Team
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>A membership's placement on a team. Keyed through (OrganizationId, UserId) rather
/// than a membership surrogate id, matching <see cref="OrganizationMembership"/>'s own key.</summary>
public sealed class TeamMembership
{
    public Guid TeamId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>PF-S01.06: one signed-in session/device, tracked so a user (or an admin, in a later
/// pass) can see and revoke a single device without the "sign out everywhere" effect of
/// <c>UserManager.UpdateSecurityStampAsync</c> (still used unchanged by <c>LogoutAsync</c> for the
/// bulk case). <see cref="Id"/> is issued at login and carried in the cookie as the
/// <c>TenantAccess.SessionClaim</c> claim; <see cref="RevokedAt"/> set means the cookie is rejected
/// on its next validation even though <c>SecurityStamp</c> still matches.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>PF-S01.07: one append-only identity/membership-administration event - invitations
/// sent, invitations accepted, a membership's role changed, a membership removed, or a role's
/// capability overrides changed. Deliberately a single flat shape (mirroring
/// <c>PropFlow.Domain.Timeline.TimelineEntry</c>'s "no event-specific columns" approach for the
/// Work module) rather than one table per event type, since this is a control-plane audit trail
/// read as a list, not a source of business logic. <see cref="ActorUserId"/> is null for the one
/// action with no authenticated actor: a brand-new user accepting their own invitation.
/// <see cref="TargetLabel"/> is free text identifying what changed when it isn't
/// <see cref="TargetUserId"/> - an invited email address, or the role name for a
/// <c>RoleCapabilitiesChanged</c> entry.</summary>
public sealed class IdentityAuditEntry
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string EventType { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? TargetLabel { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
