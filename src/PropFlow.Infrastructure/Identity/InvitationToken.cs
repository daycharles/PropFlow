using System.Security.Cryptography;

namespace PropFlow.Infrastructure.Identity;

// PF-S01.01: token generation and validation for the organization-invitation flow (FS-S01).
// Deliberately pure/logic-only — no EF Core dependency — so it is testable without a database
// and safe to land ahead of the persistence layer (PF-S01.02) and endpoints (PF-S01.03).
public static class InvitationToken
{
    public const int DefaultLifetimeDays = 7;

    // 32 bytes -> 43-char unpadded base64url. Same order of entropy as an ASP.NET Identity
    // security stamp; long enough that guessing is not a viable attack even at scale, short
    // enough to sit in a URL query string without encoding surprises.
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Mirrors OrganizationSlug's normalize-before-store/compare discipline: an invitation is
    // looked up and accepted by the same canonical form it was created with, regardless of case
    // or incidental whitespace in what an administrator typed or an acceptor pastes back.
    public static string CanonicalEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    public static DateTimeOffset ExpiryFrom(DateTimeOffset issuedAt, int lifetimeDays = DefaultLifetimeDays)
    {
        if (lifetimeDays <= 0) throw new ArgumentOutOfRangeException(nameof(lifetimeDays), "Lifetime must be positive.");
        return issuedAt.AddDays(lifetimeDays);
    }

    // Expiry is exclusive at the boundary instant itself: an invitation is valid up to and
    // including its ExpiresAt instant, consistent with the domain's other "OccurredAt <= X"
    // comparisons rather than a strict "<".
    public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now > expiresAt;
}
