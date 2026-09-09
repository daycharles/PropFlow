using PropFlow.Domain.Communications;
using PropFlow.Domain.People;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ResidentTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset When = DateTimeOffset.Parse("2026-01-15T09:00:00-05:00");

    private static Resident New(string? email = "dana@example.test", string? phone = "+15550100101") =>
        new(Org, Guid.NewGuid(), "Dana Reyes", email, phone);

    [Fact]
    public void Trims_contact_fields_and_drops_blanks()
    {
        var resident = new Resident(Org, Guid.NewGuid(), "  Dana Reyes  ", "  dana@example.test ", "   ");

        Assert.Equal("Dana Reyes", resident.FullName);
        Assert.Equal("dana@example.test", resident.Email);
        Assert.Null(resident.Phone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name)
    {
        Assert.Throws<ArgumentException>(() => new Resident(Org, Guid.NewGuid(), name, null, null));
    }

    [Fact]
    public void Rejects_a_line_break_in_the_name_or_contact_fields()
    {
        var lf = ((char)10).ToString();
        Assert.Throws<ArgumentException>(() => new Resident(Org, Guid.NewGuid(), "Dana" + lf + "Reyes", null, null));
        Assert.Throws<ArgumentException>(() => new Resident(Org, Guid.NewGuid(), "Dana", "dana@x.test" + lf + "evil", null));
    }

    [Fact]
    public void Consent_starts_unknown_and_blocks_contact()
    {
        var resident = New();

        Assert.Equal(ConsentDecision.Unknown, resident.SmsConsent);
        Assert.False(resident.AllowsContact(MessageChannel.Sms));
        Assert.False(resident.AllowsContact(MessageChannel.Email));
    }

    [Fact]
    public void Granting_consent_allows_contact_only_with_an_address()
    {
        var resident = New(phone: null);
        resident.SetConsent(MessageChannel.Sms, granted: true, When);

        Assert.Equal(ConsentDecision.Granted, resident.SmsConsent);
        Assert.Equal(When.ToUniversalTime(), resident.SmsConsentAt);
        Assert.False(resident.AllowsContact(MessageChannel.Sms)); // granted but no phone

        resident.UpdateContact(email: null, phone: "+15550100101");
        Assert.True(resident.AllowsContact(MessageChannel.Sms));
    }

    [Fact]
    public void Revoking_consent_blocks_contact_again()
    {
        var resident = New();
        resident.SetConsent(MessageChannel.Email, granted: true, When);
        Assert.True(resident.AllowsContact(MessageChannel.Email));

        resident.SetConsent(MessageChannel.Email, granted: false, When.AddHours(1));
        Assert.Equal(ConsentDecision.Revoked, resident.EmailConsent);
        Assert.False(resident.AllowsContact(MessageChannel.Email));
    }

    [Fact]
    public void Consent_is_tracked_per_channel()
    {
        var resident = New();
        resident.SetConsent(MessageChannel.Sms, granted: true, When);

        Assert.True(resident.AllowsContact(MessageChannel.Sms));
        Assert.False(resident.AllowsContact(MessageChannel.Email));
    }
}
