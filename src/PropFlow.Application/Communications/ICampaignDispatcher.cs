using PropFlow.Domain.Communications;

namespace PropFlow.Application.Communications;

public sealed record CampaignRecipient(Guid ResidentId, string Address);
public sealed record CampaignDispatchResult(int Queued, int SkippedConsent, int SkippedUnsubscribed);

public interface ICampaignDispatcher
{
    Task<CampaignDispatchResult> DispatchAsync(Campaign campaign, IReadOnlyList<CampaignRecipient> recipients, CancellationToken cancellationToken);
}
