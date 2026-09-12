namespace PropFlow.Domain.Work;

public enum WorkStatus { Draft, New, Assigned, Scheduled, OnTheWay, InProgress, OnHold, Completed, Cancelled }
public enum WorkPriority { Low, Normal, High, Critical }
public enum WorkType { MaintenanceRequest, WorkOrder, InspectionFollowUp, PreventiveMaintenance, UnitTurnTask, ProjectTask }

public sealed class WorkItem : TenantEntity
{
    public WorkItem(Guid organizationId, Guid id, string title) : base(organizationId, id) { Title = Required(title, 200); CreatorId = id; }
    public WorkItem(Guid organizationId, Guid id, string title, Guid propertyId, Guid creatorId, WorkType workType = WorkType.WorkOrder) : base(organizationId, id) { Title = Required(title, 200); PropertyId = RequiredId(propertyId, nameof(propertyId)); CreatorId = RequiredId(creatorId, nameof(creatorId)); WorkType = workType; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public WorkType WorkType { get; private set; }
    public WorkStatus Status { get; private set; } = WorkStatus.Draft;
    public WorkPriority Priority { get; private set; } = WorkPriority.Normal;
    public Guid PropertyId { get; private set; }
    public Guid? BuildingId { get; private set; }
    public Guid? SpaceId { get; private set; }
    // The piece of equipment this work is about, if any. Drives the asset's maintenance history
    // and repeat-repair detection (M6). Must live at the same property as the work item.
    public Guid? AssetId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? VendorId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    // Resident is intentionally an opaque id until occupancy is introduced in M4.
    public Guid? ResidentId { get; private set; }
    public Guid CreatorId { get; private set; }
    public DateTimeOffset? ScheduledStart { get; private set; }
    public DateTimeOffset? ScheduledEnd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public decimal? Cost { get; private set; }
    public string? InternalNotes { get; private set; }
    public string? ResidentVisibleNotes { get; private set; }
    // PF-S03.04. Assigned once, at creation, only when the organization has configured
    // numbering for WorkItem. Work created before numbering was turned on keeps this null
    // forever - there is no retroactive backfill.
    public string? DisplayNumber { get; private set; }
    public void Edit(string title, string? description, Guid? categoryId, WorkPriority priority) { RefuseWhenTerminal(); Title = Required(title, 200); Description = Optional(description, 4000); CategoryId = categoryId; Priority = priority; }
    public void SetLocation(Guid propertyId, Guid? buildingId, Guid? spaceId, Guid? residentId)
    {
        PropertyId = RequiredId(propertyId, nameof(propertyId));
        BuildingId = buildingId;
        SpaceId = spaceId;
        ResidentId = residentId;
    }
    // Null clears the link. Cross-property consistency (the asset must belong to PropertyId) is
    // enforced in the application layer, which has the asset to check against.
    public void SetAsset(Guid? assetId) => AssetId = assetId;
    public void SetDueDate(DateTimeOffset? dueDate) => DueDate = dueDate?.ToUniversalTime();
    public void SetCost(decimal? cost)
    {
        if (cost is < 0) throw new ArgumentOutOfRangeException(nameof(cost));
        Cost = cost;
    }
    public void SetNotes(string? internalNotes, string? residentVisibleNotes)
    {
        InternalNotes = Optional(internalNotes, 4000);
        ResidentVisibleNotes = Optional(residentVisibleNotes, 4000);
    }
    // The numbering allocator (EfWorkOperations) calls this at most once, right after
    // construction and before the first save - the guard is a programming-error backstop, not
    // something a client request can trigger.
    public void SetDisplayNumber(string displayNumber)
    {
        if (DisplayNumber is not null) throw new InvalidOperationException("A display number has already been assigned.");
        if (string.IsNullOrWhiteSpace(displayNumber) || displayNumber.Trim().Length > 50)
            throw new ArgumentException("Display number must contain 1 to 50 characters.", nameof(displayNumber));
        DisplayNumber = displayNumber.Trim();
    }
    // Completed and cancelled work takes no new assignment. The guard lives on the mutating
    // overloads so neither the event-raising overloads nor a future caller can route around it,
    // and it says the same thing ChangeStatus does about the same two statuses.
    public bool IsTerminal => Status is WorkStatus.Completed or WorkStatus.Cancelled;
    public void AssignVendor(Guid vendorId) { RefuseWhenTerminal(); VendorId = RequiredId(vendorId, nameof(vendorId)); if (Status == WorkStatus.New) Status = WorkStatus.Assigned; }
    public VendorAssigned? AssignVendor(Guid vendorId, Guid actorId, DateTimeOffset occurredAt) { if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId)); if (VendorId == vendorId) return null; var prior = VendorId; AssignVendor(vendorId); return new VendorAssigned(Guid.NewGuid(), OrganizationId, actorId, occurredAt.ToUniversalTime(), Id, prior, vendorId); }
    public void AssignEmployee(Guid employeeId) { RefuseWhenTerminal(); EmployeeId = RequiredId(employeeId, nameof(employeeId)); if (Status == WorkStatus.New) Status = WorkStatus.Assigned; }
    public EmployeeAssigned? AssignEmployee(Guid employeeId, Guid actorId, DateTimeOffset occurredAt) { if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId)); if (EmployeeId == employeeId) return null; var prior = EmployeeId; AssignEmployee(employeeId); return new EmployeeAssigned(Guid.NewGuid(), OrganizationId, actorId, occurredAt.ToUniversalTime(), Id, prior, employeeId); }
    public void Publish(DateTimeOffset at) { if (Status != WorkStatus.Draft) throw new InvalidOperationException("Only drafts may be published."); Status = WorkStatus.New; CreatedAt = at.ToUniversalTime(); }
    public void Schedule(DateTimeOffset start, DateTimeOffset? end) { RefuseWhenTerminal(); if (VendorId is null && EmployeeId is null) throw new InvalidOperationException("Scheduling requires a vendor or employee."); if (end is not null && end < start) throw new ArgumentException("Schedule end must follow start."); ScheduledStart = start.ToUniversalTime(); ScheduledEnd = end?.ToUniversalTime(); Status = WorkStatus.Scheduled; }
    public void ChangeStatus(WorkStatus status, DateTimeOffset at) { if (Status is WorkStatus.Completed or WorkStatus.Cancelled) throw new InvalidOperationException("Completed and cancelled work is terminal."); if (status == WorkStatus.Draft || status == Status) throw new InvalidOperationException("Invalid status transition."); Status = status; if (status == WorkStatus.Completed) CompletedAt = at.ToUniversalTime(); }
    public void SetPriority(WorkPriority priority) { RefuseWhenTerminal(); Priority = priority; }
    // The one sanctioned way out of a terminal state: an explicit, audited reopen. Everything
    // else (Edit, Schedule, ChangeStatus, the assignment overloads) still refuses terminal work.
    public WorkReopened Reopen(Guid actorId, DateTimeOffset at)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        if (!IsTerminal) throw new InvalidOperationException("Only completed or cancelled work can be reopened.");
        var from = Status;
        Status = VendorId is not null || EmployeeId is not null ? WorkStatus.Assigned : WorkStatus.New;
        CompletedAt = null;
        return new WorkReopened(Guid.NewGuid(), OrganizationId, actorId, at.ToUniversalTime(), Id, from, Status);
    }
    public bool CanDelete(Guid actorId) => Status == WorkStatus.Draft && CreatorId == actorId;
    private void RefuseWhenTerminal() { if (IsTerminal) throw new InvalidOperationException("Completed and cancelled work is terminal."); }
    private static Guid RequiredId(Guid id, string name) => id == Guid.Empty ? throw new ArgumentException("ID is required.", name) : id;
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value must contain 1 to {max} characters.");
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentException("Value is too long.");
}

// The automation triggers. Raised by the application layer *after* the work change commits; each
// carries what happened — the evaluator re-reads the committed work item for condition matching.
public sealed record WorkCreated(Guid EventId, Guid OrganizationId, Guid ActorId, DateTimeOffset OccurredAt, Guid WorkId) : IDomainEvent;
public sealed record WorkStatusChanged(Guid EventId, Guid OrganizationId, Guid ActorId, DateTimeOffset OccurredAt, Guid WorkId, WorkStatus PreviousStatus, WorkStatus NewStatus) : IDomainEvent;
// Only a resident-visible note raises this; an internal note never reaches resident messaging.
public sealed record WorkNoteAdded(Guid EventId, Guid OrganizationId, Guid ActorId, DateTimeOffset OccurredAt, Guid WorkId, bool ResidentVisible, string Note) : IDomainEvent;

public sealed record VendorAssigned(Guid EventId, Guid OrganizationId, Guid ActorId, DateTimeOffset OccurredAt, Guid WorkId, Guid? PreviousVendorId, Guid VendorId) : IDomainEvent;
public sealed record EmployeeAssigned(Guid EventId, Guid OrganizationId, Guid ActorId, DateTimeOffset OccurredAt, Guid WorkId, Guid? PreviousEmployeeId, Guid EmployeeId) : IDomainEvent;
public sealed record WorkReopened(Guid EventId, Guid OrganizationId, Guid ActorId, DateTimeOffset OccurredAt, Guid WorkId, WorkStatus PreviousStatus, WorkStatus NewStatus) : IDomainEvent;
