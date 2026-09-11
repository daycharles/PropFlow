using Microsoft.EntityFrameworkCore;

namespace PropFlow.Infrastructure.Identity;

// PF-S01.05: teams as a grouping/addressing construct for organization memberships. Does not yet
// narrow work or property access by team - that is deliberately follow-up work (see
// docs/full-suite-scope.md), not implied by a membership simply being on a team.
public sealed class TeamService(IdentityStore store)
{
    public async Task<Team> CreateAsync(Guid organizationId, string name, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 200) throw new ArgumentException("Team name must be 1-200 characters.", nameof(name));
        if (await store.Teams.AnyAsync(x => x.OrganizationId == organizationId && x.Name == trimmed, ct))
            throw new InvalidOperationException($"A team named '{trimmed}' already exists in this organization.");

        var team = new Team { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = trimmed };
        store.Teams.Add(team);
        await store.SaveChangesAsync(ct);
        return team;
    }

    public Task<List<Team>> ListAsync(Guid organizationId, CancellationToken ct) =>
        store.Teams.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public enum MembershipOutcome { Applied, TeamNotFound, MembershipNotFound }

    // Requires an active OrganizationMembership for (organizationId, userId) to already exist -
    // a team placement without a membership underneath it would be an orphaned row the UI has no
    // way to explain.
    public async Task<MembershipOutcome> AddMemberAsync(Guid organizationId, Guid teamId, Guid userId, CancellationToken ct)
    {
        var team = await store.Teams.SingleOrDefaultAsync(x => x.Id == teamId && x.OrganizationId == organizationId, ct);
        if (team is null) return MembershipOutcome.TeamNotFound;
        var membership = await store.Memberships.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.UserId == userId && x.IsActive, ct);
        if (membership is null) return MembershipOutcome.MembershipNotFound;

        if (!await store.TeamMemberships.AnyAsync(x => x.TeamId == teamId && x.UserId == userId, ct))
        {
            store.TeamMemberships.Add(new TeamMembership { TeamId = teamId, OrganizationId = organizationId, UserId = userId });
            await store.SaveChangesAsync(ct);
        }
        return MembershipOutcome.Applied;
    }

    public async Task RemoveMemberAsync(Guid organizationId, Guid teamId, Guid userId, CancellationToken ct)
    {
        var membership = await store.TeamMemberships.SingleOrDefaultAsync(
            x => x.TeamId == teamId && x.OrganizationId == organizationId && x.UserId == userId, ct);
        if (membership is null) return;
        store.TeamMemberships.Remove(membership);
        await store.SaveChangesAsync(ct);
    }

    public Task<List<Guid>> ListMemberIdsAsync(Guid organizationId, Guid teamId, CancellationToken ct) =>
        store.TeamMemberships.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.TeamId == teamId)
            .Select(x => x.UserId)
            .ToListAsync(ct);
}
