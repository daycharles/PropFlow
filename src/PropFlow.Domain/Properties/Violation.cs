namespace PropFlow.Domain.Properties;

public enum ViolationStatus { Open = 1, Remediating = 2, Resolved = 3, Waived = 4 }

public sealed class Violation : TenantEntity
{
    public Violation(Guid organizationId, Guid id, Guid propertyId, string title, DateOnly identifiedOn, int severity)
        : base(organizationId, id)
    {
        if (propertyId == Guid.Empty) throw new ArgumentException("Property is required.", nameof(propertyId));
        if (severity is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(severity));
        PropertyId = propertyId; Title = Required(title); IdentifiedOn = identifiedOn; Severity = severity;
    }
    public Guid PropertyId { get; private set; }
    public string Title { get; private set; }
    public DateOnly IdentifiedOn { get; private set; }
    public int Severity { get; private set; }
    public ViolationStatus Status { get; private set; } = ViolationStatus.Open;
    public DateOnly? ResolvedOn { get; private set; }
    public void StartRemediation() { if (Status != ViolationStatus.Open) throw new InvalidOperationException("Violation is not open."); Status = ViolationStatus.Remediating; }
    public void Resolve(DateOnly on) { if (Status is not (ViolationStatus.Open or ViolationStatus.Remediating)) throw new InvalidOperationException("Violation is not remediable."); Status = ViolationStatus.Resolved; ResolvedOn = on; }
    public void Waive() { if (Status is ViolationStatus.Resolved or ViolationStatus.Waived) throw new InvalidOperationException("Violation is already final."); Status = ViolationStatus.Waived; }
    private static string Required(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 200 ? value.Trim() : throw new ArgumentException("Title must contain 1 to 200 characters.", nameof(value));
}
