using PropFlow.Domain;

namespace PropFlow.Domain.Communications;

public sealed class ProviderCallbackReceipt(Guid organizationId, Guid id, string bodyDigest, DateTimeOffset receivedAt)
    : TenantEntity(organizationId, id)
{
    public string BodyDigest { get; private set; } = bodyDigest.Trim();
    public DateTimeOffset ReceivedAt { get; private set; } = receivedAt.ToUniversalTime();
}
