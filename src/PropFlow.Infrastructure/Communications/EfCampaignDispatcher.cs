using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Communications;
using PropFlow.Domain.Communications;

namespace PropFlow.Infrastructure.Communications;

public sealed class EfCampaignDispatcher(CommunicationsStore store, TimeProvider clock) : ICampaignDispatcher
{
    public async Task<CampaignDispatchResult> DispatchAsync(Campaign campaign, IReadOnlyList<CampaignRecipient> recipients, CancellationToken cancellationToken)
    {
        var addresses = recipients.Select(x => x.Address.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var unsubscribed = await store.ChannelUnsubscribes.AsNoTracking()
            .Where(x => x.Channel == campaign.Channel && addresses.Contains(x.Address)).Select(x => x.Address).ToListAsync(cancellationToken);
        var blocked = unsubscribed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eligible = recipients.Where(x => !blocked.Contains(x.Address.Trim())).ToArray();
        await using var transaction = await store.Database.BeginTransactionAsync(cancellationToken);
        var keys = eligible.Select(x => $"campaign:{campaign.Id:N}:resident:{x.ResidentId:N}").ToArray();
        var existing = (await store.OutboxMessages.Where(x => keys.Contains(x.IdempotencyKey)).Select(x => x.IdempotencyKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var queued = 0;
        foreach (var recipient in eligible)
        {
            var key = $"campaign:{campaign.Id:N}:resident:{recipient.ResidentId:N}";
            if (existing.Add(key)) { queued++; store.OutboxMessages.Add(OutboxMessage.Create(store.OrganizationId, Guid.NewGuid(), campaign.Channel,
                recipient.Address, campaign.Subject, campaign.Body, key, clock.GetUtcNow())); }
        }
        campaign.Publish();
        await store.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CampaignDispatchResult(queued, recipients.Count - eligible.Length, blocked.Count);
    }
}
