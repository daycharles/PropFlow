using Microsoft.EntityFrameworkCore;

namespace PropFlow.Infrastructure.Identity;

// PF-S01.06: per-device session tracking, additive alongside the existing SecurityStamp-based
// "sign out everywhere" logout (SessionAuthentication.LogoutAsync, unchanged). A row here is
// created at login and consulted on every cookie validation; revoking one row signs out that
// single device without touching anyone else's session or bumping SecurityStamp.
public sealed class UserSessionService(IdentityStore store, TimeProvider clock)
{
    public async Task<UserSession> StartAsync(Guid userId, Guid organizationId, string? userAgent, string? ipAddress, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationId = organizationId,
            CreatedAt = now,
            LastSeenAt = now,
            UserAgent = Truncate(userAgent, 512),
            IpAddress = Truncate(ipAddress, 64),
        };
        store.UserSessions.Add(session);
        await store.SaveChangesAsync(ct);
        return session;
    }

    // Returns false when the session is missing or revoked - ValidateCookieAsync treats either as
    // "reject the cookie" without distinguishing them to the caller.
    public async Task<bool> TouchAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await store.UserSessions.FindAsync([sessionId], ct);
        if (session is null || session.RevokedAt is not null) return false;
        session.LastSeenAt = clock.GetUtcNow();
        await store.SaveChangesAsync(ct);
        return true;
    }

    public Task<List<UserSession>> ListActiveAsync(Guid userId, Guid organizationId, CancellationToken ct) =>
        store.UserSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.OrganizationId == organizationId && x.RevokedAt == null)
            .OrderByDescending(x => x.LastSeenAt)
            .ToListAsync(ct);

    public enum RevokeOutcome { Revoked, NotFound }

    // Scoped to the calling user's own sessions - this is self-service revocation (PF-S01.06),
    // not an admin console for revoking someone else's device.
    public async Task<RevokeOutcome> RevokeAsync(Guid userId, Guid organizationId, Guid sessionId, CancellationToken ct)
    {
        var session = await store.UserSessions.SingleOrDefaultAsync(
            x => x.Id == sessionId && x.UserId == userId && x.OrganizationId == organizationId, ct);
        if (session is null || session.RevokedAt is not null) return RevokeOutcome.NotFound;
        session.RevokedAt = clock.GetUtcNow();
        await store.SaveChangesAsync(ct);
        return RevokeOutcome.Revoked;
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
