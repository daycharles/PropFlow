namespace PropFlow.Domain.Properties;

public sealed class ComplianceOccurrence : TenantEntity
{
    private ComplianceOccurrence(Guid organizationId, Guid id) : base(organizationId, id) { }
    public ComplianceOccurrence(Guid organizationId, Guid id, Guid obligationId, DateOnly dueOn, string? idempotencyKey = null)
        : base(organizationId, id) { ObligationId = obligationId; DueOn = dueOn; IdempotencyKey = idempotencyKey ?? ComplianceObligation.OccurrenceKey(obligationId, dueOn); }
    public Guid SourceObligationId => ObligationId;
    public Guid ObligationId { get; private set; }
    public DateOnly DueOn { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
}
