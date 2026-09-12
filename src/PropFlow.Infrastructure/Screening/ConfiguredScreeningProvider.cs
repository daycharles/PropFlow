using System.Text;
using Microsoft.Extensions.Configuration;
using PropFlow.Application.Screening;
using PropFlow.Domain.Marketing;

namespace PropFlow.Infrastructure.Screening;

// FS-S05 ships no real bureau integration. This stands in for one and, crucially, makes the
// Pass / Review / Fail and outage paths reachable from a test and from a local demo:
//   Screening:ProviderMode = Available (default) | Unavailable | Pass | Review | Fail
// A real adapter replaces this class without touching the endpoints, which only see
// IScreeningProvider.
//
// In Available mode the verdict is derived from the idempotency key, not drawn at random: a
// retried request carries the same key and must get the same answer, which is the whole point
// of the key. That is also why the hash below is written out by hand — string.GetHashCode is
// randomized per process, so it would give the same request different verdicts after a restart.
public sealed class ConfiguredScreeningProvider(IConfiguration configuration) : IScreeningProvider
{
    public const string ModeKey = "Screening:ProviderMode";

    public Task<ScreeningProviderResult> ScreenAsync(ScreeningProviderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = configuration[ModeKey];

        if (string.Equals(mode, "Unavailable", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ScreeningProviderResult(
                ScreeningProviderOutcome.Unavailable,
                ScreeningRecommendation.Unavailable,
                null,
                null,
                "The screening provider is unavailable."));

        // A forced verdict for demos and for tests that need a specific recommendation.
        // Unavailable is excluded here because it is the outage mode handled above, not a
        // verdict this branch can produce.
        var recommendation = Enum.TryParse<ScreeningRecommendation>(mode, ignoreCase: true, out var forced)
            && forced != ScreeningRecommendation.Unavailable
            ? forced
            : Derive(request.IdempotencyKey);

        return Task.FromResult(new ScreeningProviderResult(
            ScreeningProviderOutcome.Completed,
            recommendation,
            ScoreFor(recommendation, request.IdempotencyKey),
            $"Stand-in screening: {recommendation} across {request.GrantedConsents.Count} consented check(s).",
            null));
    }

    // Roughly 70% Pass, 20% Review, 10% Fail, so a demo that walks a handful of applications
    // through shows more than one outcome without anyone having to reconfigure it.
    private static ScreeningRecommendation Derive(string idempotencyKey) => (StableHash(idempotencyKey) % 10) switch
    {
        < 7 => ScreeningRecommendation.Pass,
        < 9 => ScreeningRecommendation.Review,
        _ => ScreeningRecommendation.Fail
    };

    private static int ScoreFor(ScreeningRecommendation recommendation, string idempotencyKey)
    {
        var hash = StableHash(idempotencyKey);
        return recommendation switch
        {
            ScreeningRecommendation.Pass => 700 + (int)(hash % 151),
            ScreeningRecommendation.Review => 620 + (int)(hash % 80),
            ScreeningRecommendation.Fail => 300 + (int)(hash % 320),
            _ => 0
        };
    }

    // FNV-1a over the UTF-8 bytes. Deterministic across processes and runs, which
    // string.GetHashCode is not.
    private static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return hash;
    }
}
