using PropFlow.Domain.Marketing;

namespace PropFlow.Application.Screening;

// Built on the IPaymentGateway template, deliberately not on IIntegrationAdapter: an adapter's
// PullAsync takes no subject and returns a whole-source snapshot, while screening is
// request/response about exactly one person — PII in, a verdict out.
//
// Outcomes, not exceptions (.claude/rules/architecture.md). Unavailable is the provider-outage
// case and is deliberately distinct from a Fail recommendation, exactly as
// PaymentGatewayOutcome.Unavailable is distinct from Declined: a Fail is a real answer about
// the applicant and is stored as a ScreeningResult, an outage is no answer at all and
// PF-S05.06 turns it into a 503 with nothing written.
public enum ScreeningProviderOutcome { Completed, Unavailable }

// Only what the provider needs to identify the subject, plus the consents that authorize the
// checks. PropFlow never handles the SSN, date of birth or licence number a bureau would also
// want — the applicant supplies those through the provider's own hosted flow, which is why
// there is no field for them here and no column for them in the domain.
public sealed record ScreeningProviderRequest(
    Guid OrganizationId,
    Guid ApplicationId,
    Guid ApplicantId,
    string FullName,
    string Email,
    string? Phone,
    IReadOnlyCollection<ApplicationConsentType> GrantedConsents,
    string IdempotencyKey);

// Recommendation is ScreeningRecommendation.Unavailable whenever Outcome is Unavailable, so a
// caller that only reads the recommendation still cannot mistake an outage for a verdict —
// ScreeningResult refuses to store that member.
public sealed record ScreeningProviderResult(
    ScreeningProviderOutcome Outcome,
    ScreeningRecommendation Recommendation,
    int? Score,
    string? Summary,
    string? FailureReason);

public interface IScreeningProvider
{
    // The idempotency key on the request is what makes a retry safe: the provider is expected
    // to return the first answer for a key it has already seen rather than re-running the pull.
    Task<ScreeningProviderResult> ScreenAsync(ScreeningProviderRequest request, CancellationToken ct);
}
