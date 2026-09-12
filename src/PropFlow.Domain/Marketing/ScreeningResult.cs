namespace PropFlow.Domain.Marketing;

// The verdict that came back for one ScreeningRequest. Append-only: a provider that revises its
// answer writes a second row, because the first row is what the approve/deny decision was made
// against and is adverse-action evidence.
//
// Recommendation, Score and a bounded Summary are the whole of what PropFlow keeps. The raw
// credit report stays with the provider.
public sealed class ScreeningResult : TenantEntity
{
    public const int SummaryMaxLength = 4000;

    // EF materialization.
    private ScreeningResult(Guid organizationId, Guid id) : base(organizationId, id) { }

    public ScreeningResult(
        Guid organizationId,
        Guid id,
        Guid screeningRequestId,
        Guid applicantId,
        ScreeningRecommendation recommendation,
        int? score,
        string? summary,
        DateTimeOffset receivedAt)
        : base(organizationId, id)
    {
        // An outage is not a verdict. PF-S05.06 turns Unavailable into a 503 with nothing
        // written, and this refusal is what makes "nothing written" a domain fact rather than
        // an endpoint convention that a later caller could forget.
        if (recommendation == ScreeningRecommendation.Unavailable)
            throw new ArgumentException("A provider outage is not a stored verdict.", nameof(recommendation));
        if (score is < 0) throw new ArgumentOutOfRangeException(nameof(score));
        ScreeningRequestId = ApplicationText.RequireId(screeningRequestId, nameof(screeningRequestId));
        ApplicantId = ApplicationText.RequireId(applicantId, nameof(applicantId));
        Recommendation = recommendation;
        Score = score;
        Summary = ApplicationText.OptionalText(summary, nameof(summary), SummaryMaxLength);
        ReceivedAt = receivedAt.ToUniversalTime();
    }

    public Guid ScreeningRequestId { get; private set; }
    public Guid ApplicantId { get; private set; }
    public ScreeningRecommendation Recommendation { get; private set; }
    public int? Score { get; private set; }
    public string? Summary { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
}
