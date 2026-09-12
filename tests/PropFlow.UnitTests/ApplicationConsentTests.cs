using PropFlow.Domain.Communications;
using PropFlow.Domain.Marketing;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ApplicationConsentTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Application = Guid.NewGuid();
    private static readonly Guid Applicant = Guid.NewGuid();
    private static readonly Guid Recorder = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationConsent Row(
        ConsentDecision decision,
        DateTimeOffset at,
        ApplicationConsentType type = ApplicationConsentType.BackgroundCheck,
        Guid? applicant = null) =>
        new(Org, Guid.NewGuid(), Application, applicant ?? Applicant, type, decision, at, Recorder, "203.0.113.7");

    [Fact]
    public void A_consent_row_records_who_when_and_from_where()
    {
        var consent = Row(ConsentDecision.Granted, Now);
        Assert.Equal(Application, consent.ApplicationId);
        Assert.Equal(Applicant, consent.ApplicantId);
        Assert.Equal(ConsentDecision.Granted, consent.Decision);
        Assert.Equal(Recorder, consent.RecordedBy);
        Assert.Equal(Now, consent.RecordedAt);
        Assert.Equal("203.0.113.7", consent.Source);
        Assert.True(consent.AllowsScreening);
    }

    // Unknown is the absence of a row, not something a row can say.
    [Fact]
    public void An_unknown_decision_cannot_be_recorded()
    {
        Assert.Throws<ArgumentException>(() => Row(ConsentDecision.Unknown, Now));
    }

    [Fact]
    public void A_source_is_required_and_bounded()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationConsent(Org, Guid.NewGuid(), Application, Applicant, ApplicationConsentType.CreditCheck, ConsentDecision.Granted, Now, Recorder, "   "));
        Assert.Throws<ArgumentException>(() => new ApplicationConsent(Org, Guid.NewGuid(), Application, Applicant, ApplicationConsentType.CreditCheck, ConsentDecision.Granted, Now, Recorder, new string('s', ApplicationConsent.SourceMaxLength + 1)));
    }

    [Fact]
    public void A_consent_row_exposes_no_way_to_change_its_decision()
    {
        // Revocation is a new row. Anything that could rewrite this one would destroy the
        // evidence that consent was held at the moment screening ran.
        Assert.DoesNotContain(
            typeof(ApplicationConsent).GetMethods(),
            m => m.Name is "Revoke" or "Grant" or "SetDecision" or "Update");
    }

    [Fact]
    public void A_later_revoke_beats_an_earlier_grant()
    {
        ApplicationConsent[] rows = [Row(ConsentDecision.Granted, Now), Row(ConsentDecision.Revoked, Now.AddHours(1))];
        Assert.False(ApplicationConsent.Allows(rows, Application, Applicant, ApplicationConsentType.BackgroundCheck));
    }

    [Fact]
    public void A_later_grant_beats_an_earlier_revoke()
    {
        ApplicationConsent[] rows = [Row(ConsentDecision.Revoked, Now), Row(ConsentDecision.Granted, Now.AddHours(1))];
        Assert.True(ApplicationConsent.Allows(rows, Application, Applicant, ApplicationConsentType.BackgroundCheck));
    }

    [Fact]
    public void The_reduction_does_not_depend_on_the_order_rows_arrive_in()
    {
        var granted = Row(ConsentDecision.Granted, Now);
        var revoked = Row(ConsentDecision.Revoked, Now.AddHours(1));
        Assert.False(ApplicationConsent.Allows([granted, revoked], Application, Applicant, ApplicationConsentType.BackgroundCheck));
        Assert.False(ApplicationConsent.Allows([revoked, granted], Application, Applicant, ApplicationConsentType.BackgroundCheck));
    }

    [Fact]
    public void Effective_consent_is_computed_per_applicant_and_per_check()
    {
        var other = Guid.NewGuid();
        ApplicationConsent[] rows =
        [
            Row(ConsentDecision.Granted, Now, ApplicationConsentType.BackgroundCheck),
            Row(ConsentDecision.Granted, Now, ApplicationConsentType.CreditCheck),
            Row(ConsentDecision.Revoked, Now.AddMinutes(5), ApplicationConsentType.CreditCheck),
            Row(ConsentDecision.Granted, Now, ApplicationConsentType.BackgroundCheck, other)
        ];

        var effective = ApplicationConsent.Effective(rows);
        Assert.Equal(3, effective.Count);
        Assert.True(effective[new(Application, Applicant, ApplicationConsentType.BackgroundCheck)].AllowsScreening);
        Assert.False(effective[new(Application, Applicant, ApplicationConsentType.CreditCheck)].AllowsScreening);
        Assert.True(effective[new(Application, other, ApplicationConsentType.BackgroundCheck)].AllowsScreening);
        // Revoking one applicant's credit check leaves the other applicant untouched.
        Assert.False(ApplicationConsent.Allows(rows, Application, Applicant, ApplicationConsentType.CreditCheck));
        Assert.True(ApplicationConsent.Allows(rows, Application, other, ApplicationConsentType.BackgroundCheck));
    }

    [Fact]
    public void A_check_with_no_row_at_all_is_not_consented()
    {
        ApplicationConsent[] rows = [Row(ConsentDecision.Granted, Now, ApplicationConsentType.BackgroundCheck)];
        Assert.False(ApplicationConsent.Allows(rows, Application, Applicant, ApplicationConsentType.EvictionHistory));
        Assert.False(ApplicationConsent.Allows([], Application, Applicant, ApplicationConsentType.BackgroundCheck));
    }
}
