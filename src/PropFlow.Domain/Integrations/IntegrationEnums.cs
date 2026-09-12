namespace PropFlow.Domain.Integrations;

// The canonical entity types an external property-management system can hand us.
//
// Resident and Building are not canonical record shapes in their own right: they are PropFlow
// rows a canonical record reconciles into. A resident is linked through the synthetic id
// SyntheticExternalId.ForResident because CanonicalOccupancy carries no resident external id; a
// building through CanonicalSpace.BuildingExternalId. Both need their own ExternalRecordLink so
// the reconciler stays idempotent about the row it created last run.
//
// Adding members is safe: IntegrationStore.cs:43 maps this column with HasConversion<string>(),
// so the persisted value is the member name and no existing row's meaning shifts.
public enum IntegrationEntityKind
{
    Property = 0,
    Space = 1,
    Occupancy = 2,
    WorkOrder = 3,
    Asset = 4,
    Resident = 5,
    Building = 6
}

// Per-record sync state. A link is Pending the moment we learn an external id exists, Synced once
// a pull has seen it cleanly, Failed if mapping that record threw, Conflicted when reconciliation
// raised a divergence a human has to settle, and Retired when the source system stopped reporting
// the record — which is never a delete on the PropFlow side, see ExternalRecordLink.Retire.
//
// Adding members is safe for the same reason: IntegrationStore.cs:44 is HasConversion<string>().
public enum SyncState
{
    Pending = 0,
    Synced = 1,
    Failed = 2,
    Conflicted = 3,
    Retired = 4
}
