using PropFlow.Domain.Communications;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ChannelConsentTests
{
    private static readonly DateTimeOffset When = DateTimeOffset.Parse("2026-09-09T09:00:00-04:00");

    [Fact]
    public void Unset_consent_blocks_contact()
    {
        var consent = ChannelConsent.Unset(MessageChannel.Sms);

        Assert.Equal(ConsentDecision.Unknown, consent.Decision);
        Assert.False(consent.AllowsContact);
        Assert.Null(consent.DecidedAt);
    }

    [Fact]
    public void Grant_allows_contact_and_normalizes_the_timestamp_to_utc()
    {
        var granted = ChannelConsent.Unset(MessageChannel.Email).Grant(When);

        Assert.True(granted.AllowsContact);
        Assert.Equal(When, granted.DecidedAt);
        Assert.Equal(TimeSpan.Zero, granted.DecidedAt!.Value.Offset);
    }

    [Fact]
    public void Revoke_blocks_contact_again()
    {
        var revoked = ChannelConsent.Unset(MessageChannel.Sms).Grant(When).Revoke(When.AddHours(1));

        Assert.Equal(ConsentDecision.Revoked, revoked.Decision);
        Assert.False(revoked.AllowsContact);
    }

    [Fact]
    public void Transitions_do_not_mutate_the_prior_value()
    {
        var granted = ChannelConsent.Unset(MessageChannel.Sms).Grant(When);
        var revoked = granted.Revoke(When.AddHours(1));

        Assert.True(granted.AllowsContact);
        Assert.False(revoked.AllowsContact);
    }
}
