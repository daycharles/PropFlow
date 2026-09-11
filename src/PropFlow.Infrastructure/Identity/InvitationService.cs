using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PropFlow.Infrastructure.Identity;

// PF-S01.03: creates and accepts organization invitations. Kept separate from MembershipAccess
// (read-only membership lookups) and SessionAuthentication (login/logout) since this is the one
// place a new ApplicationUser or OrganizationMembership gets written outside of seeding.
public sealed class InvitationService(IdentityStore store, UserManager<ApplicationUser> users, TimeProvider clock)
{
    // Re-inviting a still-pending address replaces its token/role/expiry rather than creating a
    // second row: only one invitation per (organization, email) is ever live at a time.
    public async Task<Invitation> CreateAsync(Guid organizationId, string email, string role, Guid invitedByUserId, CancellationToken ct)
    {
        var canonicalEmail = InvitationToken.CanonicalEmail(email);
        var now = clock.GetUtcNow();
        var pending = await store.Invitations.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.Email == canonicalEmail && x.AcceptedAt == null, ct);

        if (pending is not null)
        {
            pending.Role = role;
            pending.Token = InvitationToken.Generate();
            pending.InvitedByUserId = invitedByUserId;
            pending.CreatedAt = now;
            pending.ExpiresAt = InvitationToken.ExpiryFrom(now);
            await store.SaveChangesAsync(ct);
            return pending;
        }

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = canonicalEmail,
            Role = role,
            Token = InvitationToken.Generate(),
            InvitedByUserId = invitedByUserId,
            CreatedAt = now,
            ExpiresAt = InvitationToken.ExpiryFrom(now)
        };
        store.Invitations.Add(invitation);
        await store.SaveChangesAsync(ct);
        return invitation;
    }

    public Task<List<Invitation>> ListPendingAsync(Guid organizationId, CancellationToken ct) =>
        store.Invitations.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.AcceptedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public enum AcceptOutcome { Accepted, NotFound, Expired, AlreadyAccepted, WeakPassword }

    public async Task<(AcceptOutcome Outcome, IEnumerable<string> Errors)> AcceptAsync(
        string token, string password, CancellationToken ct)
    {
        var invitation = await store.Invitations.SingleOrDefaultAsync(x => x.Token == token, ct);
        if (invitation is null) return (AcceptOutcome.NotFound, []);
        if (invitation.AcceptedAt is not null) return (AcceptOutcome.AlreadyAccepted, []);
        if (InvitationToken.IsExpired(invitation.ExpiresAt, clock.GetUtcNow())) return (AcceptOutcome.Expired, []);

        // The email is already canonical (CreateAsync stores it that way); FindByEmailAsync
        // itself normalizes via Identity's configured NormalizedEmail lookup.
        var user = await users.FindByEmailAsync(invitation.Email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = invitation.Email, Email = invitation.Email };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) return (AcceptOutcome.WeakPassword, created.Errors.Select(x => x.Description));
        }

        var existingMembership = await store.Memberships.SingleOrDefaultAsync(
            x => x.OrganizationId == invitation.OrganizationId && x.UserId == user.Id, ct);
        if (existingMembership is null)
            store.Memberships.Add(new OrganizationMembership
            {
                OrganizationId = invitation.OrganizationId,
                UserId = user.Id,
                Role = invitation.Role,
                IsActive = true
            });
        else
        {
            // Re-accepting (e.g. a re-sent invitation for a since-deactivated member) reactivates
            // and re-grants the invited role rather than silently no-op-ing.
            existingMembership.Role = invitation.Role;
            existingMembership.IsActive = true;
        }

        invitation.AcceptedAt = clock.GetUtcNow();
        invitation.AcceptedByUserId = user.Id;
        await store.SaveChangesAsync(ct);
        return (AcceptOutcome.Accepted, []);
    }
}
