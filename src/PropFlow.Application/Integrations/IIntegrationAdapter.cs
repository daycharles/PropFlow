using PropFlow.Domain.Integrations;

namespace PropFlow.Application.Integrations;

// An adapter reads one external property-management system and returns its records as a
// canonical snapshot. Pull-only for now: PropFlow is the system of record and does not write
// back. Implementations live in Infrastructure; the mock adapter is the only one in milestone 6.
public interface IIntegrationAdapter
{
    // Machine identifier, lower-case, matching IntegrationConnection.SourceSystem ("mock").
    string SourceSystem { get; }

    // Human label for the Integration Health screen ("Mock property system").
    string DisplayName { get; }

    // What this adapter can actually do. Declared, never inferred: an empty snapshot list is
    // ambiguous (nothing there this run, or not supported at all?), and the reconciler and the
    // API have to be able to tell a caller *why* a connection cannot supply, say, work orders
    // instead of silently returning an empty list.
    IntegrationAdapterDescriptor Descriptor { get; }

    Task<IntegrationSnapshot> PullAsync(CancellationToken cancellationToken);
}

// The capability statement for one adapter.
//
// `SuppliedKinds` is the set of canonical kinds the adapter populates. A kind outside the set is
// not "empty this run" — it is unavailable from this source, and a reconciler must not retire
// existing links of that kind just because the snapshot's list for it came back empty.
//
// `SupportsIncrementalPull` is false for everything that exists today: `PullAsync` takes no
// cursor and returns a whole-source snapshot. The flag exists so a later adapter that does carry
// a watermark can say so without reshaping this interface again, and so the sync scheduler can
// treat "cheap delta" and "expensive full snapshot" differently.
public sealed record IntegrationAdapterDescriptor(
    IntegrationCategory Category,
    IReadOnlyList<IntegrationEntityKind> SuppliedKinds,
    bool SupportsIncrementalPull)
{
    // Every canonical kind `IntegrationSnapshot` carries, in enum order.
    public static IReadOnlyList<IntegrationEntityKind> AllKinds { get; } =
    [
        IntegrationEntityKind.Property,
        IntegrationEntityKind.Space,
        IntegrationEntityKind.Occupancy,
        IntegrationEntityKind.WorkOrder,
        IntegrationEntityKind.Asset
    ];

    public bool Supplies(IntegrationEntityKind kind) => SuppliedKinds.Contains(kind);

    // A property-management adapter that returns every canonical kind as a whole-source snapshot —
    // the shape of both adapters FS-S19 ships.
    public static IntegrationAdapterDescriptor FullPropertyManagementSnapshot { get; } =
        new(IntegrationCategory.PropertyManagement, AllKinds, SupportsIncrementalPull: false);
}

// The set of adapters wired into this deployment. Backed by DI registration.
public interface IIntegrationCatalog
{
    IReadOnlyList<IIntegrationAdapter> Available { get; }

    IIntegrationAdapter? Resolve(string sourceSystem);
}
