namespace PropFlow.Domain.Leasing;

public enum LeaseDocumentStatus { Draft, Sent, Signed, Expired }

public sealed class LeaseDocument(Guid organizationId, Guid id, Guid leaseId, string title, string documentUrl, bool residentVisible)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public string Title { get; private set; } = Required(title, nameof(title), 200);
    public string DocumentUrl { get; private set; } = Required(documentUrl, nameof(documentUrl), 1000);
    public bool ResidentVisible { get; private set; } = residentVisible;
    public LeaseDocumentStatus Status { get; private set; } = LeaseDocumentStatus.Draft;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SignedAt { get; private set; }
    public string? SignedBy { get; private set; }

    public void Send() { if (Status != LeaseDocumentStatus.Draft) throw new InvalidOperationException("Only a draft document can be sent."); Status = LeaseDocumentStatus.Sent; }
    public void Sign(string signedBy)
    {
        if (Status != LeaseDocumentStatus.Sent) throw new InvalidOperationException("Only a sent document can be signed.");
        SignedBy = Required(signedBy, nameof(signedBy), 200); SignedAt = DateTimeOffset.UtcNow; Status = LeaseDocumentStatus.Signed;
    }
    public void Expire() { if (Status == LeaseDocumentStatus.Signed) throw new InvalidOperationException("A signed document cannot be expired."); Status = LeaseDocumentStatus.Expired; }

    private static string Required(string value, string name, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.", name);
}
