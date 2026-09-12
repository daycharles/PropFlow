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

// The nine kinds of external system FS-S19 (#207) names: "PMS, accounting, screening, payment,
// banking, messaging, calendar, e-signature, and utility adapters". Closed on purpose. FS-S19
// ships the machinery — contract, reconciler, conflict queue, sync health — and exactly two
// PropertyManagement adapters (the deterministic mock and the sandbox HTTP adapter). Every other
// member is a declared slot with a follow-on story behind it, not a shipped integration, so a
// caller that reads an adapter's category learns what the adapter is *for* before it is written.
//
// Why a closed enum rather than a string: the five canonical shapes in `CanonicalRecords.cs` are
// all property-management shapes, so an accounting or banking adapter has nothing to reconcile
// *into* until its own story adds shapes. Naming the categories keeps that gap visible instead of
// letting an adapter register under a free-text category the reconciler silently ignores.
//
// `Screening` is reserved and must stay empty: screening is FS-S05's `IScreeningProvider`, a
// narrow request/response port over one subject. `IIntegrationAdapter.PullAsync` takes no subject
// and returns a whole-source snapshot, which is the wrong shape for it. Do not wire a screening
// adapter here.
//
// Default is `PropertyManagement` (0) because it is the only category with a working
// reconciliation path; an adapter in any other category must say so explicitly.
public enum IntegrationCategory
{
    PropertyManagement = 0,
    Accounting = 1,
    Screening = 2,
    Payment = 3,
    Banking = 4,
    Messaging = 5,
    Calendar = 6,
    ESignature = 7,
    Utility = 8
}

// Per-record sync state. A link is Pending the moment we learn an external id exists, Synced
// once a pull has seen it cleanly, and Failed if mapping that record threw.
public enum SyncState
{
    Pending = 0,
    Synced = 1,
    Failed = 2
}
