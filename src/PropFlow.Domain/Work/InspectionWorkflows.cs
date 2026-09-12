using System.Text.Json;

namespace PropFlow.Domain.Work;

public enum InspectionKind { MoveIn, MoveOut, Routine }
public enum InspectionStatus { Draft, InProgress, Completed, Approved }
public enum FindingStatus { Open, Resolved, Waived }
public enum FindingSeverity { Informational, Minor, Major, Critical }
public enum TurnStatus { Planned, InProgress, Ready, Completed, Cancelled }
public enum TurnTaskStatus { Pending, InProgress, Completed, Blocked }

public sealed class InspectionTemplate : TenantEntity
{
    private InspectionTemplate(Guid organizationId, Guid id) : base(organizationId, id) { }
    public InspectionTemplate(Guid organizationId, Guid id, string name, int version, IReadOnlyList<string> checklist, Guid actorId, DateTimeOffset createdAt) : base(organizationId, id)
    {
        Name = Required(name, 200); Version = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version));
        ChecklistJson = Normalize(checklist); CreatedBy = RequiredId(actorId, nameof(actorId)); CreatedAt = createdAt.ToUniversalTime();
    }
    public string Name { get; private set; } = "";
    public int Version { get; private set; }
    public string ChecklistJson { get; private set; } = "[]";
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsArchived { get; private set; }
    public IReadOnlyList<string> Checklist => JsonSerializer.Deserialize<List<string>>(ChecklistJson) ?? [];
    public void Archive() => IsArchived = true;
    private void SetChecklist(IReadOnlyList<string> value) => ChecklistJson = Normalize(value);
    private static string Normalize(IReadOnlyList<string> value)
    {
        if (value is null || value.Count == 0 || value.Count > 200) throw new ArgumentException("A template needs 1 to 200 checklist items.", nameof(value));
        var items = value.Select(x => x?.Trim()).ToArray();
        if (items.Any(x => string.IsNullOrWhiteSpace(x) || x!.Length > 500)) throw new ArgumentException("Checklist items must contain 1 to 500 characters.", nameof(value));
        return JsonSerializer.Serialize(items);
    }
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.");
    private static Guid RequiredId(Guid id, string name) => id == Guid.Empty ? throw new ArgumentException("ID is required.", name) : id;
}

public sealed class Inspection : TenantEntity
{
    private Inspection(Guid organizationId, Guid id) : base(organizationId, id) { }
    public Inspection(Guid organizationId, Guid id, Guid propertyId, Guid? spaceId, Guid templateId, InspectionKind kind, Guid actorId, DateTimeOffset at) : base(organizationId, id)
    { PropertyId = RequiredId(propertyId, nameof(propertyId)); SpaceId = spaceId; TemplateId = RequiredId(templateId, nameof(templateId)); Kind = kind; CreatedBy = RequiredId(actorId, nameof(actorId)); CreatedAt = at.ToUniversalTime(); }
    public Guid PropertyId { get; private set; }
    public Guid? SpaceId { get; private set; }
    public Guid TemplateId { get; private set; }
    public InspectionKind Kind { get; private set; }
    public InspectionStatus Status { get; private set; } = InspectionStatus.Draft;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public void Start() { if (Status != InspectionStatus.Draft) throw new InvalidOperationException("Only draft inspections can start."); Status = InspectionStatus.InProgress; }
    public void Complete(DateTimeOffset at) { if (Status is not (InspectionStatus.InProgress or InspectionStatus.Draft)) throw new InvalidOperationException("Inspection is not open."); Status = InspectionStatus.Completed; CompletedAt = at.ToUniversalTime(); }
    public void Approve(DateTimeOffset at) { if (Status != InspectionStatus.Completed) throw new InvalidOperationException("Only completed inspections can be approved."); Status = InspectionStatus.Approved; ApprovedAt = at.ToUniversalTime(); }
    private static Guid RequiredId(Guid id, string name) => id == Guid.Empty ? throw new ArgumentException("ID is required.", name) : id;
}

public sealed class InspectionFinding : TenantEntity
{
    private InspectionFinding(Guid organizationId, Guid id) : base(organizationId, id) { }
    public InspectionFinding(Guid organizationId, Guid id, Guid inspectionId, string area, string description, FindingSeverity severity) : base(organizationId, id)
    { InspectionId = RequiredId(inspectionId, nameof(inspectionId)); Area = Required(area, 200); Description = Required(description, 2000); Severity = severity; }
    public Guid InspectionId { get; private set; }
    public string Area { get; private set; } = "";
    public string Description { get; private set; } = "";
    public FindingSeverity Severity { get; private set; }
    public FindingStatus Status { get; private set; } = FindingStatus.Open;
    public string PhotoAttachmentIdsJson { get; private set; } = "[]";
    public DateTimeOffset? ResolvedAt { get; private set; }
    public void SetPhotos(IReadOnlyList<Guid> ids) { if (ids.Count > 20 || ids.Any(x => x == Guid.Empty)) throw new ArgumentException("A finding can reference up to 20 valid photos.", nameof(ids)); PhotoAttachmentIdsJson = JsonSerializer.Serialize(ids); }
    public void Resolve(DateTimeOffset at) { if (Status != FindingStatus.Open) throw new InvalidOperationException("Finding is not open."); Status = FindingStatus.Resolved; ResolvedAt = at.ToUniversalTime(); }
    public void Waive() { if (Status != FindingStatus.Open) throw new InvalidOperationException("Finding is not open."); Status = FindingStatus.Waived; }
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.");
    private static Guid RequiredId(Guid id, string name) => id == Guid.Empty ? throw new ArgumentException("ID is required.", name) : id;
}

public sealed class UnitTurn : TenantEntity
{
    private UnitTurn(Guid organizationId, Guid id) : base(organizationId, id) { }
    public UnitTurn(Guid organizationId, Guid id, Guid propertyId, Guid spaceId, Guid moveOutInspectionId, DateOnly targetReadyOn, Guid actorId, DateTimeOffset at) : base(organizationId, id)
    { PropertyId = RequiredId(propertyId, nameof(propertyId)); SpaceId = RequiredId(spaceId, nameof(spaceId)); MoveOutInspectionId = RequiredId(moveOutInspectionId, nameof(moveOutInspectionId)); TargetReadyOn = targetReadyOn; CreatedBy = RequiredId(actorId, nameof(actorId)); CreatedAt = at.ToUniversalTime(); }
    public Guid PropertyId { get; private set; }
    public Guid SpaceId { get; private set; }
    public Guid MoveOutInspectionId { get; private set; }
    public TurnStatus Status { get; private set; } = TurnStatus.Planned;
    public DateOnly TargetReadyOn { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public void Start() { if (Status != TurnStatus.Planned) throw new InvalidOperationException("Only planned turns can start."); Status = TurnStatus.InProgress; }
    public void MarkReady(DateTimeOffset at) { if (Status != TurnStatus.InProgress) throw new InvalidOperationException("Turn must be in progress."); Status = TurnStatus.Ready; ReadyAt = at.ToUniversalTime(); }
    private static Guid RequiredId(Guid id, string name) => id == Guid.Empty ? throw new ArgumentException("ID is required.", name) : id;
}

public sealed class UnitTurnTask : TenantEntity
{
    private UnitTurnTask(Guid organizationId, Guid id) : base(organizationId, id) { }
    public UnitTurnTask(Guid organizationId, Guid id, Guid turnId, string title, Guid? workId, int sequence) : base(organizationId, id)
    { TurnId = RequiredId(turnId, nameof(turnId)); Title = !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 500 ? title.Trim() : throw new ArgumentException("Task title is required.", nameof(title)); WorkId = workId; Sequence = sequence; }
    public Guid TurnId { get; private set; }
    public string Title { get; private set; } = "";
    public Guid? WorkId { get; private set; }
    public int Sequence { get; private set; }
    public TurnTaskStatus Status { get; private set; } = TurnTaskStatus.Pending;
    public void LinkWork(Guid workId) => WorkId = RequiredId(workId, nameof(workId));
    public void ChangeStatus(TurnTaskStatus status) => Status = status;
    private static Guid RequiredId(Guid id, string name) => id == Guid.Empty ? throw new ArgumentException("ID is required.", name) : id;
}
