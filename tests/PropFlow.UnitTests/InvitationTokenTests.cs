using PropFlow.Infrastructure.Identity;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class InvitationTokenTests
{
    [Fact]
    public void Generate_produces_a_url_safe_token_with_no_padding()
    {
        var token = InvitationToken.Generate();

        Assert.Equal(43, token.Length); // 32 bytes -> ceil(32/3*4) - padding = 43
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Generate_does_not_repeat()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => InvitationToken.Generate()).ToHashSet();

        Assert.Equal(1000, tokens.Count);
    }

    [Theory]
    [InlineData("Person@Example.com", "person@example.com")]
    [InlineData("  person@example.com  ", "person@example.com")]
    [InlineData("PERSON@EXAMPLE.COM", "person@example.com")]
    public void CanonicalEmail_trims_and_lowercases(string input, string expected)
    {
        Assert.Equal(expected, InvitationToken.CanonicalEmail(input));
    }

    [Fact]
    public void CanonicalEmail_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => InvitationToken.CanonicalEmail(null!));
    }

    [Fact]
    public void ExpiryFrom_defaults_to_seven_days()
    {
        var issuedAt = DateTimeOffset.Parse("2026-09-11T00:00:00Z");

        Assert.Equal(DateTimeOffset.Parse("2026-09-18T00:00:00Z"), InvitationToken.ExpiryFrom(issuedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExpiryFrom_rejects_a_non_positive_lifetime(int lifetimeDays)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InvitationToken.ExpiryFrom(DateTimeOffset.UtcNow, lifetimeDays));
    }

    [Fact]
    public void IsExpired_is_false_at_and_before_the_boundary_instant()
    {
        var expiresAt = DateTimeOffset.Parse("2026-09-18T00:00:00Z");

        Assert.False(InvitationToken.IsExpired(expiresAt, expiresAt));
        Assert.False(InvitationToken.IsExpired(expiresAt, expiresAt.AddSeconds(-1)));
    }

    [Fact]
    public void IsExpired_is_true_after_the_boundary_instant()
    {
        var expiresAt = DateTimeOffset.Parse("2026-09-18T00:00:00Z");

        Assert.True(InvitationToken.IsExpired(expiresAt, expiresAt.AddSeconds(1)));
    }
}
