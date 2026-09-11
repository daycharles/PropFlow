namespace PropFlow.Domain.Properties;

public sealed class ComplianceEvidence : TenantEntity
{
    private ComplianceEvidence(Guid organizationId, Guid id) : base(organizationId, id) { }
    public ComplianceEvidence(Guid organizationId, Guid id, Guid attachmentId, Guid? incidentId, Guid? violationId,
        DateTimeOffset createdAt, DateTimeOffset? retainUntil, bool legalHold) : base(organizationId, id)
    { AttachmentId = attachmentId; IncidentId = incidentId; ViolationId = violationId; CreatedAt = createdAt.ToUniversalTime(); RetainUntil = retainUntil?.ToUniversalTime(); LegalHold = legalHold; }
    public Guid AttachmentId { get; private set; }
    public Guid? IncidentId { get; private set; }
    public Guid? ViolationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RetainUntil { get; private set; }
    public bool LegalHold { get; private set; }
}
