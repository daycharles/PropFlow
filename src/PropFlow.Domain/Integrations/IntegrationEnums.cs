namespace PropFlow.Domain.Integrations;

// The canonical entity types an external property-management system can hand us. Kept
// deliberately small — it mirrors the five record shapes the adapters map, nothing more.
public enum IntegrationEntityKind
{
    Property = 0,
    Space = 1,
    Occupancy = 2,
    WorkOrder = 3,
    Asset = 4
}

// Per-record sync state. A link is Pending the moment we learn an external id exists, Synced
// once a pull has seen it cleanly, and Failed if mapping that record threw.
public enum SyncState
{
    Pending = 0,
    Synced = 1,
    Failed = 2
}
