namespace PropFlow.Domain.Configuration;

// PF-S03.07. A closed vocabulary, the same shape ConfigurationEntityType uses: the events a
// staff user can opt in or out of being notified about, starting with the ones that already
// fire somewhere in the codebase (EmployeeAssigned, "AutomationApplied",
// IdentityAuditLog.EventTypes.InvitationSent). No delivery channel is wired to any of these yet
// - today only residents have a messaging pipeline (IResidentMessenger) - so this enum, and the
// preference it gates, is deliberately not a promise that anything gets sent.
public enum NotificationEventType { WorkAssigned, AutomationApplied, InvitationReceived }

// One row per (organization, user, event type) - opting OUT, not in: a missing row means
// enabled, matching the "everything on until you turn it off" default most notification
// settings use. NotificationPreferenceEndpoints materializes the full NotificationEventType set
// for the caller by defaulting any type with no row to Enabled = true, so a fresh user with zero
// rows reads as fully subscribed rather than fully silent.
public sealed class NotificationPreference : TenantEntity
{
    private NotificationPreference(Guid organizationId, Guid id) : base(organizationId, id) { }

    public static NotificationPreference Create(Guid organizationId, Guid id, Guid userId, NotificationEventType eventType, bool enabled) => new(organizationId, id)
    {
        UserId = userId != Guid.Empty ? userId : throw new ArgumentException("User is required.", nameof(userId)),
        EventType = Enum.IsDefined(eventType) ? eventType : throw new ArgumentOutOfRangeException(nameof(eventType)),
        Enabled = enabled,
    };

    public Guid UserId { get; private set; }
    public NotificationEventType EventType { get; private set; }
    public bool Enabled { get; private set; } = true;

    public void SetEnabled(bool enabled) => Enabled = enabled;
}
