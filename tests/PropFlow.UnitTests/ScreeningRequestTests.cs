using PropFlow.Domain.Marketing;
using Xunit;

namespace PropFlow.UnitTests;

public sealed class ScreeningRequestTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private static ScreeningRequest Request() =>
        new(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "app-7f3a:background:1");

    [Fact]
    public void A_new_request_is_pending_with_no_attempts()
    {
        var request = Request();
        Assert.Equal(ScreeningRequestStatus.Pending, request.Status);
        Assert.Equal(0, request.Attempts);
        Assert.Null(request.LastAttemptedAt);
        Assert.Null(request.LastError);
        Assert.Null(request.CompletedAt);
        Assert.False(request.IsTerminal);
    }

    [Fact]
    public void An_idempotency_key_is_required_and_bounded()
    {
        Assert.Throws<ArgumentException>(() => new ScreeningRequest(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "  "));
        Assert.Throws<ArgumentException>(() => new ScreeningRequest(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('k', ScreeningRequest.IdempotencyKeyMaxLength + 1)));
        Assert.Throws<ArgumentException>(() => new ScreeningRequest(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "line\nbreak"));
    }

    [Fact]
    public void Begin_attempt_counts_the_attempt_and_moves_in_flight()
    {
        var request = Request();
        request.BeginAttempt(Now);
        Assert.Equal(ScreeningRequestStatus.InFlight, request.Status);
        Assert.Equal(1, request.Attempts);
        Assert.Equal(Now, request.LastAttemptedAt);
    }

    [Fact]
    public void A_second_begin_attempt_while_in_flight_is_refused_so_attempts_cannot_inflate()
    {
        var request = Request();
        request.BeginAttempt(Now);
        Assert.Throws<InvalidOperationException>(() => request.BeginAttempt(Now));
        Assert.Equal(1, request.Attempts);
    }

    [Fact]
    public void Complete_finishes_the_request_and_clears_the_last_error()
    {
        var request = Request();
        request.BeginAttempt(Now);
        request.RecordFailure("provider timeout", Now, maxAttempts: 3);
        request.BeginAttempt(Now.AddMinutes(1));
        request.Complete(Now.AddMinutes(2));
        Assert.Equal(ScreeningRequestStatus.Completed, request.Status);
        Assert.Equal(Now.AddMinutes(2), request.CompletedAt);
        Assert.Null(request.LastError);
        Assert.True(request.IsTerminal);
    }

    [Fact]
    public void A_failure_returns_the_request_to_pending_and_records_the_error()
    {
        var request = Request();
        request.BeginAttempt(Now);
        request.RecordFailure("provider timeout", Now, maxAttempts: 3);
        Assert.Equal(ScreeningRequestStatus.Pending, request.Status);
        Assert.Equal("provider timeout", request.LastError);
        Assert.Equal(1, request.Attempts);
        Assert.False(request.IsTerminal);
    }

    // The exhaustion boundary, stated explicitly: at maxAttempts - 1 the request is still
    // retryable, at maxAttempts it is abandoned. This is why "provider retry and failure
    // tests" for FS-S05 need no container.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Retries_are_exhausted_exactly_at_max_attempts(int maxAttempts)
    {
        var request = Request();
        for (var attempt = 1; attempt < maxAttempts; attempt++)
        {
            request.BeginAttempt(Now);
            request.RecordFailure($"attempt {attempt} failed", Now, maxAttempts);
            Assert.Equal(ScreeningRequestStatus.Pending, request.Status);
            Assert.Equal(attempt, request.Attempts);
            Assert.False(request.IsTerminal);
        }

        request.BeginAttempt(Now);
        Assert.Equal(maxAttempts, request.Attempts);
        request.RecordFailure("last attempt failed", Now, maxAttempts);
        Assert.Equal(ScreeningRequestStatus.Abandoned, request.Status);
        Assert.True(request.IsTerminal);
        Assert.Equal("last attempt failed", request.LastError);
    }

    [Fact]
    public void An_abandoned_request_refuses_every_further_transition()
    {
        var request = Request();
        request.BeginAttempt(Now);
        request.RecordFailure("fatal", Now, maxAttempts: 1);
        Assert.Equal(ScreeningRequestStatus.Abandoned, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.BeginAttempt(Now));
        Assert.Throws<InvalidOperationException>(() => request.Complete(Now));
        Assert.Throws<InvalidOperationException>(() => request.RecordFailure("again", Now, maxAttempts: 1));
    }

    [Fact]
    public void A_completed_request_refuses_every_further_transition()
    {
        var request = Request();
        request.BeginAttempt(Now);
        request.Complete(Now);
        Assert.Throws<InvalidOperationException>(() => request.BeginAttempt(Now));
        Assert.Throws<InvalidOperationException>(() => request.Complete(Now));
        Assert.Throws<InvalidOperationException>(() => request.RecordFailure("late", Now, maxAttempts: 3));
    }

    [Fact]
    public void Only_an_in_flight_request_can_complete_or_fail()
    {
        Assert.Throws<InvalidOperationException>(() => Request().Complete(Now));
        Assert.Throws<InvalidOperationException>(() => Request().RecordFailure("never started", Now, maxAttempts: 3));
    }

    [Fact]
    public void Max_attempts_must_be_at_least_one()
    {
        var request = Request();
        request.BeginAttempt(Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => request.RecordFailure("boom", Now, maxAttempts: 0));
    }

    [Fact]
    public void A_failure_needs_a_bounded_single_line_error()
    {
        var request = Request();
        request.BeginAttempt(Now);
        Assert.Throws<ArgumentException>(() => request.RecordFailure("  ", Now, maxAttempts: 3));
        Assert.Throws<ArgumentException>(() => request.RecordFailure(new string('e', ScreeningRequest.ErrorMaxLength + 1), Now, maxAttempts: 3));
    }

    // The unique index on (OrganizationId, IdempotencyKey) only makes a replay safe if the
    // request path and the retry path spell the key identically, so the spelling is one function.
    [Fact]
    public void The_idempotency_key_is_stable_for_an_application_and_applicant()
    {
        var application = Guid.NewGuid();
        var applicant = Guid.NewGuid();
        Assert.Equal(ScreeningRequest.KeyFor(application, applicant), ScreeningRequest.KeyFor(application, applicant));
        Assert.Equal($"application:{application:N}:applicant:{applicant:N}", ScreeningRequest.KeyFor(application, applicant));
    }

    [Fact]
    public void A_different_applicant_or_application_gets_a_different_key()
    {
        var application = Guid.NewGuid();
        var applicant = Guid.NewGuid();
        Assert.NotEqual(ScreeningRequest.KeyFor(application, applicant), ScreeningRequest.KeyFor(application, Guid.NewGuid()));
        Assert.NotEqual(ScreeningRequest.KeyFor(application, applicant), ScreeningRequest.KeyFor(Guid.NewGuid(), applicant));
    }

    [Fact]
    public void The_generated_key_fits_the_persisted_column_and_is_accepted_by_the_constructor()
    {
        var key = ScreeningRequest.KeyFor(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(key.Length <= ScreeningRequest.IdempotencyKeyMaxLength);
        Assert.Equal(key, new ScreeningRequest(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), key).IdempotencyKey);
    }

    [Fact]
    public void An_empty_application_or_applicant_has_no_key()
    {
        Assert.Throws<ArgumentException>(() => ScreeningRequest.KeyFor(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ScreeningRequest.KeyFor(Guid.NewGuid(), Guid.Empty));
    }
}

public sealed class ScreeningResultTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_result_keeps_only_the_verdict_score_and_bounded_summary()
    {
        var result = new ScreeningResult(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ScreeningRecommendation.Review, 648, "Thin file.", Now);
        Assert.Equal(ScreeningRecommendation.Review, result.Recommendation);
        Assert.Equal(648, result.Score);
        Assert.Equal("Thin file.", result.Summary);
        Assert.Equal(Now, result.ReceivedAt);
    }

    // An outage is not a verdict. PF-S05.06 turns Unavailable into a 503 with nothing written,
    // and this refusal is what makes "nothing written" a domain fact.
    [Fact]
    public void An_unavailable_provider_cannot_be_stored_as_a_result()
    {
        Assert.Throws<ArgumentException>(() =>
            new ScreeningResult(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ScreeningRecommendation.Unavailable, null, null, Now));
    }

    [Fact]
    public void A_summary_longer_than_the_bound_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            new ScreeningResult(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ScreeningRecommendation.Pass, 720, new string('s', ScreeningResult.SummaryMaxLength + 1), Now));
    }

    [Fact]
    public void A_negative_score_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScreeningResult(Org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ScreeningRecommendation.Fail, -1, null, Now));
    }
}
