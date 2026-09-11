namespace PropFlow.Domain.Leasing;

public enum LeaseStatus { Draft, Active, Renewed, NoticeGiven, Ended }
public enum LeaseNoticeType { Renewal, MoveOut }
public enum LeaseNoticeStatus { Open, Completed, Cancelled }

public sealed class Lease(Guid organizationId, Guid id, Guid residentId, Guid spaceId, DateOnly startsOn, DateOnly endsOn, decimal monthlyRent, decimal? securityDeposit)
    : TenantEntity(organizationId, id)
{
    public Guid ResidentId { get; private set; } = Required(residentId, nameof(residentId));
    public Guid SpaceId { get; private set; } = Required(spaceId, nameof(spaceId));
    public DateOnly StartsOn { get; private set; } = startsOn;
    public DateOnly EndsOn { get; private set; } = ValidateDates(startsOn, endsOn);
    public decimal MonthlyRent { get; private set; } = ValidateMoney(monthlyRent, nameof(monthlyRent));
    public decimal? SecurityDeposit { get; private set; } = securityDeposit is null ? null : ValidateMoney(securityDeposit.Value, nameof(securityDeposit));
    public LeaseStatus Status { get; private set; } = LeaseStatus.Draft;
    public DateOnly? NoticeDate { get; private set; }
    public DateOnly? MoveOutOn { get; private set; }

    public void Activate() { if (Status is not LeaseStatus.Draft) throw new InvalidOperationException("Only a draft lease can be activated."); Status = LeaseStatus.Active; }
    public void Renew(DateOnly endsOn, decimal monthlyRent)
    {
        EndsOn = ValidateDates(StartsOn, endsOn); MonthlyRent = ValidateMoney(monthlyRent, nameof(monthlyRent)); Status = LeaseStatus.Renewed;
    }
    public void GiveNotice(DateOnly noticeDate, DateOnly moveOutOn)
    {
        if (moveOutOn < noticeDate || moveOutOn < StartsOn) throw new ArgumentException("Move-out must be on or after the notice and lease start dates.", nameof(moveOutOn));
        NoticeDate = noticeDate; MoveOutOn = moveOutOn; Status = LeaseStatus.NoticeGiven;
    }
    public void End(DateOnly movedOutOn)
    {
        if (movedOutOn < StartsOn) throw new ArgumentException("Move-out cannot precede lease start.", nameof(movedOutOn));
        MoveOutOn = movedOutOn; Status = LeaseStatus.Ended;
    }
    public void Transfer(Guid residentId, Guid spaceId)
    {
        if (Status is LeaseStatus.Draft or LeaseStatus.Ended) throw new InvalidOperationException("Only an active lease can be transferred.");
        ResidentId = Required(residentId, nameof(residentId));
        SpaceId = Required(spaceId, nameof(spaceId));
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException("ID is required.", name) : value;
    private static DateOnly ValidateDates(DateOnly start, DateOnly end) => end < start ? throw new ArgumentException("Lease end cannot precede lease start.", nameof(end)) : end;
    private static decimal ValidateMoney(decimal value, string name) => value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
}

public sealed class LeaseNotice(Guid organizationId, Guid id, Guid leaseId, LeaseNoticeType type, DateOnly dueOn, string? notes)
    : TenantEntity(organizationId, id)
{
    public Guid LeaseId { get; private set; } = leaseId == Guid.Empty ? throw new ArgumentException("Lease is required.", nameof(leaseId)) : leaseId;
    public LeaseNoticeType Type { get; private set; } = type;
    public DateOnly DueOn { get; private set; } = dueOn;
    public string? Notes { get; private set; } = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    public LeaseNoticeStatus Status { get; private set; } = LeaseNoticeStatus.Open;
    public void Complete() => Status = LeaseNoticeStatus.Completed;
    public void Cancel() => Status = LeaseNoticeStatus.Cancelled;
}
