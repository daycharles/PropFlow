using PropFlow.Domain.Marketing;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class RentalApplicationTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyCollection<ScreeningRecommendation> Clean = [ScreeningRecommendation.Pass];
    private static readonly IReadOnlyCollection<ScreeningRecommendation> Failed = [ScreeningRecommendation.Pass, ScreeningRecommendation.Fail];

    private static RentalApplication Draft()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        application.AddApplicant(Guid.NewGuid(), Guid.NewGuid(), ApplicantRole.Primary, 4200m, "Employed full time");
        return application;
    }

    private static RentalApplication At(ApplicationStatus status)
    {
        var application = Draft();
        if (status == ApplicationStatus.Draft) return application;
        application.Submit(Actor, Now);
        if (status == ApplicationStatus.Submitted) return application;
        if (status == ApplicationStatus.Withdrawn) { application.Withdraw(Actor, Now); return application; }
        application.MarkConsented(Actor);
        if (status == ApplicationStatus.ConsentGranted) return application;
        application.BeginScreening(Actor);
        if (status == ApplicationStatus.Screening) return application;
        application.CompleteScreening(Actor);
        if (status == ApplicationStatus.UnderReview) return application;
        if (status == ApplicationStatus.Approved) { application.Approve(Actor, Now, "MEETS_CRITERIA", null, Clean); return application; }
        if (status == ApplicationStatus.Denied) { application.Deny(Actor, Now, "INSUFFICIENT_INCOME"); return application; }
        throw new ArgumentOutOfRangeException(nameof(status));
    }

    [Fact]
    public void New_application_starts_in_draft()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(ApplicationStatus.Draft, application.Status);
        Assert.Null(application.SubmittedAt);
        Assert.Null(application.DecidedAt);
        Assert.False(application.IsTerminal);
    }

    [Fact]
    public void Application_requires_a_listing()
    {
        Assert.Throws<ArgumentException>(() => new RentalApplication(Org, Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void Happy_path_walks_draft_to_approved()
    {
        var application = Draft();
        application.Submit(Actor, Now);
        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(Now, application.SubmittedAt);
        application.MarkConsented(Actor);
        Assert.Equal(ApplicationStatus.ConsentGranted, application.Status);
        application.BeginScreening(Actor);
        Assert.Equal(ApplicationStatus.Screening, application.Status);
        application.CompleteScreening(Actor);
        Assert.Equal(ApplicationStatus.UnderReview, application.Status);
        var decision = application.Approve(Actor, Now.AddHours(1), "MEETS_CRITERIA", null, Clean);
        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Equal(ApplicationDecisionOutcome.Approved, decision.Outcome);
        Assert.Equal(Now.AddHours(1), application.DecidedAt);
        Assert.True(application.IsTerminal);
    }

    [Fact]
    public void Deny_ends_the_application_with_a_denial_record()
    {
        var application = At(ApplicationStatus.UnderReview);
        var decision = application.Deny(Actor, Now, "EVICTION_HISTORY", "Two filings in 24 months.");
        Assert.Equal(ApplicationStatus.Denied, application.Status);
        Assert.Equal(ApplicationDecisionOutcome.Denied, decision.Outcome);
        Assert.Equal("EVICTION_HISTORY", decision.Reason);
        Assert.Equal("Two filings in 24 months.", decision.Note);
        Assert.Equal(Actor, decision.DecidedBy);
        Assert.Equal(application.Id, decision.ApplicationId);
    }

    [Fact]
    public void Abandon_screening_returns_the_application_to_consent_granted()
    {
        var application = At(ApplicationStatus.Screening);
        application.AbandonScreening(Actor);
        Assert.Equal(ApplicationStatus.ConsentGranted, application.Status);
        // And a fresh screening attempt can start without re-collecting consent.
        application.BeginScreening(Actor);
        Assert.Equal(ApplicationStatus.Screening, application.Status);
    }

    // The load-bearing invariant: the consent gate is a domain refusal, not an endpoint check.
    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Begin_screening_is_refused_from_every_state_but_consent_granted(ApplicationStatus status)
    {
        var application = At(status);
        Assert.Throws<InvalidOperationException>(() => application.BeginScreening(Actor));
        Assert.Equal(status, application.Status);
    }

    [Theory]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.ConsentGranted)]
    [InlineData(ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Submit_is_refused_from_every_state_but_draft(ApplicationStatus status)
    {
        var application = At(status);
        Assert.Throws<InvalidOperationException>(() => application.Submit(Actor, Now));
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.ConsentGranted)]
    [InlineData(ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Mark_consented_is_refused_from_every_state_but_submitted(ApplicationStatus status)
    {
        var application = At(status);
        Assert.Throws<InvalidOperationException>(() => application.MarkConsented(Actor));
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.ConsentGranted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Screening_completion_and_abandonment_are_refused_outside_screening(ApplicationStatus status)
    {
        Assert.Throws<InvalidOperationException>(() => At(status).CompleteScreening(Actor));
        Assert.Throws<InvalidOperationException>(() => At(status).AbandonScreening(Actor));
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.ConsentGranted)]
    [InlineData(ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Decisions_are_refused_outside_under_review(ApplicationStatus status)
    {
        Assert.Throws<InvalidOperationException>(() => At(status).Approve(Actor, Now, "MEETS_CRITERIA", null, Clean));
        Assert.Throws<InvalidOperationException>(() => At(status).Deny(Actor, Now, "INSUFFICIENT_INCOME"));
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.ConsentGranted)]
    [InlineData(ApplicationStatus.Screening)]
    [InlineData(ApplicationStatus.UnderReview)]
    public void Any_non_terminal_application_can_be_withdrawn(ApplicationStatus status)
    {
        var application = At(status);
        application.Withdraw(Actor, Now);
        Assert.Equal(ApplicationStatus.Withdrawn, application.Status);
        Assert.Equal(Now, application.DecidedAt);
        Assert.True(application.IsTerminal);
    }

    [Theory]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Terminal_applications_refuse_every_mutation(ApplicationStatus status)
    {
        var application = At(status);
        Assert.Throws<InvalidOperationException>(() => application.Submit(Actor, Now));
        Assert.Throws<InvalidOperationException>(() => application.MarkConsented(Actor));
        Assert.Throws<InvalidOperationException>(() => application.BeginScreening(Actor));
        Assert.Throws<InvalidOperationException>(() => application.CompleteScreening(Actor));
        Assert.Throws<InvalidOperationException>(() => application.AbandonScreening(Actor));
        Assert.Throws<InvalidOperationException>(() => application.Approve(Actor, Now, "MEETS_CRITERIA", null, Clean));
        Assert.Throws<InvalidOperationException>(() => application.Deny(Actor, Now, "INSUFFICIENT_INCOME"));
        Assert.Throws<InvalidOperationException>(() => application.Withdraw(Actor, Now));
        Assert.Throws<InvalidOperationException>(() => application.AddApplicant(Guid.NewGuid(), Guid.NewGuid(), ApplicantRole.CoApplicant));
        Assert.Throws<InvalidOperationException>(() => application.RemoveApplicant(application.Applicants[0].ApplicantId));
        Assert.Equal(status, application.Status);
    }

    [Fact]
    public void There_is_no_way_out_of_a_terminal_state()
    {
        // WorkItem has Reopen; RentalApplication deliberately does not, so a decided
        // application can never be resurrected and re-decided.
        Assert.DoesNotContain(typeof(RentalApplication).GetMethods(), m => m.Name == "Reopen");
    }

    // The resolved open question, both halves.
    [Fact]
    public void Approving_over_a_failed_screening_without_a_note_is_refused()
    {
        var application = At(ApplicationStatus.UnderReview);
        Assert.Throws<InvalidOperationException>(() => application.Approve(Actor, Now, "INDIVIDUAL_ASSESSMENT", null, Failed));
        Assert.Throws<InvalidOperationException>(() => application.Approve(Actor, Now, "INDIVIDUAL_ASSESSMENT", "   ", Failed));
        Assert.Equal(ApplicationStatus.UnderReview, application.Status);
    }

    [Fact]
    public void Approving_over_a_failed_screening_with_a_note_succeeds_and_keeps_the_note()
    {
        var application = At(ApplicationStatus.UnderReview);
        var decision = application.Approve(Actor, Now, "INDIVIDUAL_ASSESSMENT", "Guarantor covers 3x rent; offence is 9 years old.", Failed);
        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Equal("Guarantor covers 3x rent; offence is 9 years old.", decision.Note);
    }

    [Fact]
    public void Approving_a_clean_screening_needs_no_note()
    {
        var application = At(ApplicationStatus.UnderReview);
        var decision = application.Approve(Actor, Now, "MEETS_CRITERIA", null, [ScreeningRecommendation.Pass, ScreeningRecommendation.Review]);
        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Null(decision.Note);
    }

    [Fact]
    public void A_decision_requires_a_reason_code_and_an_actor()
    {
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.UnderReview).Deny(Actor, Now, "  "));
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.UnderReview).Deny(Guid.Empty, Now, "INSUFFICIENT_INCOME"));
    }

    [Fact]
    public void Exactly_one_primary_applicant_is_allowed()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        application.AddApplicant(Guid.NewGuid(), Guid.NewGuid(), ApplicantRole.Primary);
        Assert.Throws<InvalidOperationException>(() => application.AddApplicant(Guid.NewGuid(), Guid.NewGuid(), ApplicantRole.Primary));
        Assert.NotNull(application.Primary);
        Assert.Single(application.Applicants);
    }

    [Fact]
    public void A_co_applicant_cannot_be_added_before_the_primary()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => application.AddApplicant(Guid.NewGuid(), Guid.NewGuid(), ApplicantRole.CoApplicant));
        Assert.Empty(application.Applicants);
    }

    [Fact]
    public void The_same_person_cannot_be_added_twice()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        var person = Guid.NewGuid();
        application.AddApplicant(Guid.NewGuid(), person, ApplicantRole.Primary);
        Assert.Throws<InvalidOperationException>(() => application.AddApplicant(Guid.NewGuid(), person, ApplicantRole.CoApplicant));
    }

    [Fact]
    public void Removing_the_primary_is_refused_while_others_remain()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        var primary = Guid.NewGuid();
        var co = Guid.NewGuid();
        application.AddApplicant(Guid.NewGuid(), primary, ApplicantRole.Primary);
        application.AddApplicant(Guid.NewGuid(), co, ApplicantRole.CoApplicant);
        Assert.Throws<InvalidOperationException>(() => application.RemoveApplicant(primary));
        application.RemoveApplicant(co);
        application.RemoveApplicant(primary);
        Assert.Empty(application.Applicants);
    }

    [Fact]
    public void An_application_with_no_primary_applicant_cannot_be_submitted()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => application.Submit(Actor, Now));
    }

    [Fact]
    public void Added_applicants_inherit_the_application_tenancy_and_carry_the_stored_pii()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        var row = application.AddApplicant(Guid.NewGuid(), Guid.NewGuid(), ApplicantRole.Primary, 5200m, "Employed full time");
        Assert.Equal(Org, row.OrganizationId);
        Assert.Equal(application.Id, row.ApplicationId);
        Assert.Equal(5200m, row.MonthlyIncome);
        Assert.Equal("Employed full time", row.EmploymentStatus);
    }

    [Fact]
    public void Negative_income_is_refused()
    {
        var application = new RentalApplication(Org, Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() => application.AddApplicant(Guid.NewGuid(), Guid.NewGuid(), ApplicantRole.Primary, -1m));
    }

    [Fact]
    public void Every_transition_requires_an_actor()
    {
        Assert.Throws<ArgumentException>(() => Draft().Submit(Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.Submitted).MarkConsented(Guid.Empty));
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.ConsentGranted).BeginScreening(Guid.Empty));
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.Screening).CompleteScreening(Guid.Empty));
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.Screening).AbandonScreening(Guid.Empty));
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.UnderReview).Approve(Guid.Empty, Now, "MEETS_CRITERIA", null, Clean));
        Assert.Throws<ArgumentException>(() => At(ApplicationStatus.Draft).Withdraw(Guid.Empty, Now));
    }
}
