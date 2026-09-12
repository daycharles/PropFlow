using Microsoft.Extensions.Configuration;
using PropFlow.Application.Screening;
using PropFlow.Domain.Marketing;
using PropFlow.Infrastructure.Screening;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ConfiguredScreeningProviderTests
{
    private static ConfiguredScreeningProvider Provider(string? mode = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfiguredScreeningProvider.ModeKey] = mode
            })
            .Build();
        return new ConfiguredScreeningProvider(configuration);
    }

    private static ScreeningProviderRequest Request(string idempotencyKey = "app-7f3a:applicant-1") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Dana Reyes", "dana@example.test", "+1 555 0100",
            [ApplicationConsentType.BackgroundCheck, ApplicationConsentType.CreditCheck], idempotencyKey);

    [Fact]
    public async Task An_unconfigured_provider_returns_a_completed_verdict()
    {
        var result = await Provider().ScreenAsync(Request(), CancellationToken.None);
        Assert.Equal(ScreeningProviderOutcome.Completed, result.Outcome);
        Assert.NotEqual(ScreeningRecommendation.Unavailable, result.Recommendation);
        Assert.Null(result.FailureReason);
        Assert.NotNull(result.Summary);
    }

    // The outage path must be distinguishable from a Fail verdict: PF-S05.06 turns this into a
    // 503 with nothing written, while a Fail is stored.
    [Fact]
    public async Task Unavailable_mode_reports_an_outage_rather_than_a_verdict()
    {
        var result = await Provider("Unavailable").ScreenAsync(Request(), CancellationToken.None);
        Assert.Equal(ScreeningProviderOutcome.Unavailable, result.Outcome);
        Assert.Equal(ScreeningRecommendation.Unavailable, result.Recommendation);
        Assert.Null(result.Score);
        Assert.NotNull(result.FailureReason);
        // And the domain refuses to store it, so an outage cannot become a ScreeningResult.
        Assert.Throws<ArgumentException>(() => new ScreeningResult(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), result.Recommendation, result.Score, result.Summary, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("Pass", ScreeningRecommendation.Pass)]
    [InlineData("review", ScreeningRecommendation.Review)]
    [InlineData("FAIL", ScreeningRecommendation.Fail)]
    public async Task A_forced_mode_produces_that_verdict_case_insensitively(string mode, ScreeningRecommendation expected)
    {
        var result = await Provider(mode).ScreenAsync(Request(), CancellationToken.None);
        Assert.Equal(ScreeningProviderOutcome.Completed, result.Outcome);
        Assert.Equal(expected, result.Recommendation);
    }

    [Fact]
    public async Task An_unrecognized_mode_falls_back_to_the_derived_verdict()
    {
        var derived = await Provider().ScreenAsync(Request(), CancellationToken.None);
        var nonsense = await Provider("Banana").ScreenAsync(Request(), CancellationToken.None);
        Assert.Equal(derived.Recommendation, nonsense.Recommendation);
        Assert.Equal(derived.Score, nonsense.Score);
    }

    // Determinism is the property a retry depends on: the same idempotency key must get the
    // same answer, including from a different provider instance in a different process.
    [Fact]
    public async Task The_same_idempotency_key_always_gets_the_same_answer()
    {
        var first = await Provider().ScreenAsync(Request("app-9c21:applicant-2"), CancellationToken.None);
        var second = await Provider().ScreenAsync(Request("app-9c21:applicant-2"), CancellationToken.None);
        Assert.Equal(first.Recommendation, second.Recommendation);
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Summary, second.Summary);
    }

    [Fact]
    public void The_derived_verdict_is_pinned_to_known_keys_so_a_hash_change_is_caught()
    {
        // Not a golden-file ritual: these three pin the FNV-1a hash itself, so swapping it for
        // string.GetHashCode (which is randomized per process) fails here rather than showing
        // up as a flaky retry test later.
        Assert.Equal(ScreeningRecommendation.Pass, Verdict("propflow"));
        Assert.Equal(ScreeningRecommendation.Review, Verdict("app-0016:applicant-0001"));
        Assert.Equal(ScreeningRecommendation.Fail, Verdict("app-0002:applicant-0001"));
    }

    [Fact]
    public async Task A_different_idempotency_key_can_get_a_different_answer()
    {
        // The distribution is not a constant function — otherwise "deterministic" would be
        // satisfied by always returning Pass.
        var verdicts = new HashSet<ScreeningRecommendation>();
        for (var i = 0; i < 100; i++)
        {
            var result = await Provider().ScreenAsync(Request($"app-{i:0000}:applicant-0001"), CancellationToken.None);
            verdicts.Add(result.Recommendation);
        }
        Assert.Equal(3, verdicts.Count);
    }

    [Fact]
    public async Task A_scored_verdict_sits_in_the_band_for_its_recommendation()
    {
        foreach (var (mode, low, high) in new[] { ("Pass", 700, 850), ("Review", 620, 699), ("Fail", 300, 619) })
        {
            var result = await Provider(mode).ScreenAsync(Request(), CancellationToken.None);
            Assert.InRange(result.Score!.Value, low, high);
        }
    }

    private static ScreeningRecommendation Verdict(string key) =>
        Provider().ScreenAsync(Request(key), CancellationToken.None).GetAwaiter().GetResult().Recommendation;
}
