using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Assets;
using PropFlow.Domain.Integrations;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Integrations;

// What one reconciliation pass did.
public sealed record ReconciliationResult(int Added, int Updated, int Failed, int Conflicted, int Retired)
{
    public static ReconciliationResult Empty { get; } = new(0, 0, 0, 0, 0);
}

// Drives a canonical snapshot into the operations schema: creates and updates the Property,
// Space, Resident, Occupancy, WorkItem and Asset rows the external records describe, raises a
// Conflict for everything it will not decide on its own, and retires links the source stopped
// reporting. This is the code that closes the gap docs/followups.md records, where a sync tracked
// external ids and reconciled nothing.
//
// ============================ WRITES ARE DOMAIN-FIRST, HASH-LAST ============================
//
// Each kind is written in two commits, in this order and never the other way round:
//
//   1. the PropFlow rows, through OperationsStore, committed;
//   2. then ExternalRecordLink.Reconcile(...) — which sets InternalId and ReconciledHash —
//      through IntegrationStore, committed.
//
// Those are two DbContexts on two connections. This codebase has no distributed transaction
// anywhere (verified 2026-09-12: every BeginTransactionAsync in src/, tests/ and tools/ is on a
// single context's Database, or on one raw Npgsql connection in DatabaseProvisioner; there is no
// TransactionScope and no enlistment), so the pair cannot be made atomic without introducing one.
//
// It does not need to be, because the ordering makes the failure survivable. A crash between the
// two commits leaves ReconciledHash behind ContentHash, so link.NeedsReconciliation is still true.
// The next run re-decides Update, re-applies the SAME mapped values to the SAME InternalId, and
// converges. The second application is a no-op on the data. Reversing the order — recording the
// hash first — turns the same crash into a permanently lost change that no later run can detect,
// because the link would claim to be current when it is not.
//
// This is deliberately the same argument the repo already accepted for post-commit outbox dispatch
// (docs/followups.md), with one improvement: there a lost dispatch is simply gone, whereas here the
// replay path exists and runs on every sync.
//
// So: do NOT "fix" this into a transaction spanning both contexts — there is no mechanism for one
// here — and do NOT move Reconcile() ahead of the domain write to make the two look symmetrical.
// The asymmetry is the design. ExternalRecordLink's own header carries the same note.
// ============================================================================================
public sealed class EfIntegrationReconciler(
    IntegrationStore integrations,
    OperationsStore operations,
    TimeProvider clock)
{
    // Kinds are processed parents-first so a child can see the id its parent was just given.
    // Resident is folded into the Occupancy pass because CanonicalOccupancy is where a resident
    // comes from — there is no CanonicalResident.
    public async Task<ReconciliationResult> ReconcileAsync(
        IntegrationConnection connection,
        IntegrationAdapterDescriptor descriptor,
        IntegrationSnapshot snapshot,
        IReadOnlyDictionary<(IntegrationEntityKind Kind, string ExternalId), ExternalRecordLink> links,
        Guid runId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (runId == Guid.Empty) throw new ArgumentException("Run is required.", nameof(runId));

        var now = clock.GetUtcNow();
        var context = new Pass(connection, descriptor, links, runId, now);

        await LoadProfilesAsync(context, connection.Id, cancellationToken);
        await LoadOpenConflictsAsync(context, connection.Id, cancellationToken);

        await ReconcilePropertiesAsync(context, snapshot, cancellationToken);
        await ReconcileSpacesAsync(context, snapshot, cancellationToken);
        await ReconcileOccupanciesAsync(context, snapshot, cancellationToken);
        await ReconcileWorkOrdersAsync(context, snapshot, cancellationToken);
        await ReconcileAssetsAsync(context, snapshot, cancellationToken);

        await RetireVanishedAsync(context, cancellationToken);

        return context.Result;
    }

    // --- Property ------------------------------------------------------------------------------

    private async Task ReconcilePropertiesAsync(Pass pass, IntegrationSnapshot snapshot, CancellationToken ct)
    {
        const IntegrationEntityKind kind = IntegrationEntityKind.Property;
        if (!pass.Supplies(kind)) return;

        var profile = pass.Profile(kind);
        var pending = new List<(ExternalRecordLink Link, Guid InternalId, string Hash)>();

        foreach (var record in snapshot.Properties)
        {
            var (link, hash) = pass.Link(kind, record.ExternalId, CanonicalHash.Of(record));
            var decision = ReconciliationRules.Decide(link, profile,
                ParentState.Resolved, ReconciliationRules.CheckProperty(record, profile));

            if (!pass.Handle(decision, kind, record.ExternalId, link)) continue;
            if (decision.Action is ReconcileAction.Unchanged)
            {
                pass.Resolve(kind, record.ExternalId, link.InternalId!.Value);
                continue;
            }

            var timeZone = profile!.ResolveTimeZone(record.TimeZone)!;
            try
            {
                Guid internalId;
                if (decision.Action is ReconcileAction.Create)
                {
                    internalId = Guid.NewGuid();
                    operations.Properties.Add(new Property(pass.OrganizationId, internalId,
                        profile.TargetPortfolioId!.Value, record.Name, timeZone));
                    pass.Result.Add();
                }
                else
                {
                    internalId = link.InternalId!.Value;
                    var existing = await operations.Properties.SingleOrDefaultAsync(x => x.Id == internalId, ct);
                    if (existing is null) { pass.Vanished(kind, record.ExternalId, link, internalId); continue; }
                    existing.Update(record.Name, timeZone);
                    pass.Result.Update();
                }
                pending.Add((link, internalId, hash));
                pass.Resolve(kind, record.ExternalId, internalId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                pass.Refused(kind, record.ExternalId, link, "Name", record.Name, exception.Message);
            }
        }

        await CommitAsync(pass, pending, ct);
    }

    // --- Space ---------------------------------------------------------------------------------

    private async Task ReconcileSpacesAsync(Pass pass, IntegrationSnapshot snapshot, CancellationToken ct)
    {
        const IntegrationEntityKind kind = IntegrationEntityKind.Space;
        if (!pass.Supplies(kind)) return;

        var profile = pass.Profile(kind);
        var propertyIds = snapshot.Properties.Select(x => x.ExternalId).ToHashSet(StringComparer.Ordinal);
        var pending = new List<(ExternalRecordLink Link, Guid InternalId, string Hash)>();

        foreach (var record in snapshot.Spaces)
        {
            var (link, hash) = pass.Link(kind, record.ExternalId, CanonicalHash.Of(record));
            var parents = pass.ParentStateOf(IntegrationEntityKind.Property, record.PropertyExternalId, propertyIds);
            var decision = ReconciliationRules.Decide(link, profile ?? pass.AnyApplyingProfile(kind),
                parents, ReconciliationRules.CheckSpace(record, profile));

            if (!pass.Handle(decision, kind, record.ExternalId, link)) continue;
            if (decision.Action is ReconcileAction.Unchanged)
            {
                pass.Resolve(kind, record.ExternalId, link.InternalId!.Value);
                continue;
            }

            var propertyId = pass.Resolved(IntegrationEntityKind.Property, record.PropertyExternalId)!.Value;
            var buildingId = pass.ResolvedOrNull(IntegrationEntityKind.Building, record.BuildingExternalId);
            if (record.BuildingExternalId is { Length: > 0 } building && buildingId is null)
                // The space still reconciles; the operator is told the building reference was dropped.
                pass.Conflict(kind, record.ExternalId, new ConflictDetail(ConflictReason.MissingParentLink,
                    "BuildingExternalId", building, null,
                    "PropFlow has no building linked to that external id, so the space was reconciled without one."));

            try
            {
                Guid internalId;
                if (decision.Action is ReconcileAction.Create)
                {
                    internalId = Guid.NewGuid();
                    operations.Spaces.Add(new Space(pass.OrganizationId, internalId, propertyId, buildingId, record.Code));
                    pass.Result.Add();
                }
                else
                {
                    internalId = link.InternalId!.Value;
                    var existing = await operations.Spaces.SingleOrDefaultAsync(x => x.Id == internalId, ct);
                    if (existing is null) { pass.Vanished(kind, record.ExternalId, link, internalId); continue; }
                    // buildingId ?? existing.BuildingId: a source that does not model buildings must
                    // not clear one an operator set. Same rule as ReconciliationRules.PreferSource.
                    existing.Update(propertyId, buildingId ?? existing.BuildingId, record.Code);
                    pass.Result.Update();
                }
                pending.Add((link, internalId, hash));
                pass.Resolve(kind, record.ExternalId, internalId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                pass.Refused(kind, record.ExternalId, link, "Code", record.Code, exception.Message);
            }
        }

        await CommitAsync(pass, pending, ct);
    }

    // --- Resident and Occupancy ------------------------------------------------------------------

    private async Task ReconcileOccupanciesAsync(Pass pass, IntegrationSnapshot snapshot, CancellationToken ct)
    {
        const IntegrationEntityKind kind = IntegrationEntityKind.Occupancy;
        if (!pass.Supplies(kind)) return;

        var profile = pass.Profile(kind);
        var spaceIds = snapshot.Spaces.Select(x => x.ExternalId).ToHashSet(StringComparer.Ordinal);
        var pending = new List<(ExternalRecordLink Link, Guid InternalId, string Hash)>();

        foreach (var record in snapshot.Occupancies)
        {
            var (link, hash) = pass.Link(kind, record.ExternalId, CanonicalHash.Of(record));
            var parents = pass.ParentStateOf(IntegrationEntityKind.Space, record.SpaceExternalId, spaceIds);
            var decision = ReconciliationRules.Decide(link, profile ?? pass.AnyApplyingProfile(kind),
                parents, ReconciliationRules.CheckOccupancy(record, profile));

            if (!pass.Handle(decision, kind, record.ExternalId, link)) continue;

            var spaceId = pass.Resolved(IntegrationEntityKind.Space, record.SpaceExternalId)!.Value;

            // The resident rides along on the occupancy: CanonicalOccupancy carries the person but
            // no external id for them, so the link is keyed on the synthetic id. One named place.
            var residentExternalId = SyntheticExternalId.ForResident(record.ExternalId);
            var (residentLink, residentHash) = pass.Link(IntegrationEntityKind.Resident, residentExternalId,
                CanonicalHash.Of((record.ResidentName, record.Email, record.Phone)));
            // A resident is derived rather than sent, so SyncAsync's ingest pass only creates the
            // link; the "last seen" hash is recorded here, where the content actually is. Without
            // this ContentHash stays null, NeedsReconciliation is permanently true, and every run
            // would rewrite every resident.
            residentLink.Observe(residentHash, pass.Now, pass.RunId);

            try
            {
                var residentId = residentLink.InternalId ?? Guid.NewGuid();
                if (residentLink.InternalId is null)
                {
                    operations.Residents.Add(new Resident(pass.OrganizationId, residentId,
                        record.ResidentName, record.Email, record.Phone));
                }
                else if (residentLink.NeedsReconciliation)
                {
                    var existingResident = await operations.Residents.SingleOrDefaultAsync(x => x.Id == residentId, ct);
                    if (existingResident is not null)
                    {
                        existingResident.Rename(record.ResidentName);
                        existingResident.UpdateContact(
                            ReconciliationRules.PreferSource(record.Email, existingResident.Email),
                            ReconciliationRules.PreferSource(record.Phone, existingResident.Phone));
                    }
                }
                pending.Add((residentLink, residentId, residentHash));
                pass.Resolve(IntegrationEntityKind.Resident, residentExternalId, residentId);

                if (decision.Action is ReconcileAction.Unchanged)
                {
                    pass.Resolve(kind, record.ExternalId, link.InternalId!.Value);
                    continue;
                }

                Guid internalId;
                if (decision.Action is ReconcileAction.Create)
                {
                    internalId = Guid.NewGuid();
                    var occupancy = new Occupancy(pass.OrganizationId, internalId, residentId, spaceId, record.MovedInOn);
                    if (record.MovedOutOn is { } movedOut) occupancy.EndOn(movedOut);
                    operations.Occupancies.Add(occupancy);
                    pass.Result.Add();
                }
                else
                {
                    internalId = link.InternalId!.Value;
                    var existing = await operations.Occupancies.SingleOrDefaultAsync(x => x.Id == internalId, ct);
                    if (existing is null) { pass.Vanished(kind, record.ExternalId, link, internalId); continue; }

                    // Occupancy exposes no way to move MovedInOn or to un-end a tenancy — by design,
                    // a tenancy's start is a fact. A source that changes either is reporting a
                    // different tenancy, which a human has to decide about.
                    if (existing.MovedInOn != record.MovedInOn)
                    {
                        pass.Conflict(kind, record.ExternalId, new ConflictDetail(ConflictReason.ValidationRefusal,
                            "MovedInOn", record.MovedInOn.ToString("O"), existing.MovedInOn.ToString("O"),
                            "An occupancy's move-in date is immutable in PropFlow. The source now reports a different one."));
                        link.MarkConflicted("The source changed the move-in date.", pass.Now, pass.RunId);
                        pass.Result.Conflict();
                        continue;
                    }
                    if (record.MovedOutOn is { } movedOut && existing.MovedOutOn != movedOut) existing.EndOn(movedOut);
                    pass.Result.Update();
                }
                pending.Add((link, internalId, hash));
                pass.Resolve(kind, record.ExternalId, internalId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                pass.Refused(kind, record.ExternalId, link, "ResidentName", record.ResidentName, exception.Message);
            }
        }

        await CommitAsync(pass, pending, ct);
    }

    // --- Work order ------------------------------------------------------------------------------

    private async Task ReconcileWorkOrdersAsync(Pass pass, IntegrationSnapshot snapshot, CancellationToken ct)
    {
        const IntegrationEntityKind kind = IntegrationEntityKind.WorkOrder;
        if (!pass.Supplies(kind)) return;

        var profile = pass.Profile(kind);
        var propertyIds = snapshot.Properties.Select(x => x.ExternalId).ToHashSet(StringComparer.Ordinal);
        var pending = new List<(ExternalRecordLink Link, Guid InternalId, string Hash)>();

        foreach (var record in snapshot.WorkOrders)
        {
            var (link, hash) = pass.Link(kind, record.ExternalId, CanonicalHash.Of(record));
            var parents = pass.ParentStateOf(IntegrationEntityKind.Property, record.PropertyExternalId, propertyIds);
            var decision = ReconciliationRules.Decide(link, profile,
                parents, ReconciliationRules.CheckWorkOrder(record, profile));

            if (!pass.Handle(decision, kind, record.ExternalId, link)) continue;
            if (decision.Action is ReconcileAction.Unchanged)
            {
                pass.Resolve(kind, record.ExternalId, link.InternalId!.Value);
                continue;
            }

            var propertyId = pass.Resolved(IntegrationEntityKind.Property, record.PropertyExternalId)!.Value;
            var spaceId = pass.ResolvedOrNull(IntegrationEntityKind.Space, record.SpaceExternalId);
            var status = profile!.MapWorkStatus(record.Status)!.Value;
            var creator = profile.DefaultCreatorId!.Value;

            try
            {
                Guid internalId;
                if (decision.Action is ReconcileAction.Create)
                {
                    internalId = Guid.NewGuid();
                    var work = new WorkItem(pass.OrganizationId, internalId, record.Title, propertyId, creator);
                    work.SetLocation(propertyId, null, spaceId, null);
                    work.Edit(record.Title, record.Description, null, WorkPriority.Normal);
                    // Publish moves Draft -> New and stamps CreatedAt with the source's opened-at,
                    // so imported work keeps the age it actually has.
                    work.Publish(record.OpenedAt);
                    ApplyStatus(work, status, record.ClosedAt ?? record.OpenedAt);
                    operations.WorkItems.Add(work);
                    pass.Result.Add();
                }
                else
                {
                    internalId = link.InternalId!.Value;
                    var existing = await operations.WorkItems.SingleOrDefaultAsync(x => x.Id == internalId, ct);
                    if (existing is null) { pass.Vanished(kind, record.ExternalId, link, internalId); continue; }

                    // Reopen is the only sanctioned exit from a terminal state (.claude/rules/traps.md),
                    // so a source that moves work back out of Completed/Cancelled goes through it
                    // rather than around any RefuseWhenTerminal guard.
                    if (existing.IsTerminal && status is not (WorkStatus.Completed or WorkStatus.Cancelled))
                        existing.Reopen(creator, pass.Now);

                    if (!existing.IsTerminal)
                    {
                        existing.Edit(record.Title, record.Description, null, existing.Priority);
                        existing.SetLocation(propertyId, null, spaceId, existing.ResidentId);
                    }
                    ApplyStatus(existing, status, record.ClosedAt ?? pass.Now);
                    pass.Result.Update();
                }
                pending.Add((link, internalId, hash));
                pass.Resolve(kind, record.ExternalId, internalId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                pass.Refused(kind, record.ExternalId, link, "Status", record.Status, exception.Message);
            }
        }

        await CommitAsync(pass, pending, ct);
    }

    // ChangeStatus refuses a no-op transition and refuses Draft, and it refuses anything at all on
    // terminal work. Both are domain invariants, not conditions to work around, so the caller only
    // asks for a transition that is actually a transition.
    private static void ApplyStatus(WorkItem work, WorkStatus status, DateTimeOffset at)
    {
        if (work.Status == status) return;
        if (status == WorkStatus.Draft) return;
        if (work.IsTerminal) return;
        work.ChangeStatus(status, at);
    }

    // --- Asset -----------------------------------------------------------------------------------

    private async Task ReconcileAssetsAsync(Pass pass, IntegrationSnapshot snapshot, CancellationToken ct)
    {
        const IntegrationEntityKind kind = IntegrationEntityKind.Asset;
        if (!pass.Supplies(kind)) return;

        var profile = pass.Profile(kind);
        var propertyIds = snapshot.Properties.Select(x => x.ExternalId).ToHashSet(StringComparer.Ordinal);
        var pending = new List<(ExternalRecordLink Link, Guid InternalId, string Hash)>();

        foreach (var record in snapshot.Assets)
        {
            var (link, hash) = pass.Link(kind, record.ExternalId, CanonicalHash.Of(record));
            var parents = pass.ParentStateOf(IntegrationEntityKind.Property, record.PropertyExternalId, propertyIds);
            var decision = ReconciliationRules.Decide(link, profile ?? pass.AnyApplyingProfile(kind),
                parents, ReconciliationRules.CheckAsset(record, profile));

            if (!pass.Handle(decision, kind, record.ExternalId, link)) continue;
            if (decision.Action is ReconcileAction.Unchanged)
            {
                pass.Resolve(kind, record.ExternalId, link.InternalId!.Value);
                continue;
            }

            var propertyId = pass.Resolved(IntegrationEntityKind.Property, record.PropertyExternalId)!.Value;
            var spaceId = pass.ResolvedOrNull(IntegrationEntityKind.Space, record.SpaceExternalId);
            // Blank is ABSENT, and Other is the honest representation of "this source does not
            // classify its assets". A non-blank value with no rule never reaches here — CheckAsset
            // turned it into a conflict rather than letting it degrade to Other.
            var assetKind = string.IsNullOrWhiteSpace(record.Kind)
                ? AssetKind.Other
                : profile!.MapAssetKind(record.Kind)!.Value;

            try
            {
                Guid internalId;
                Asset asset;
                if (decision.Action is ReconcileAction.Create)
                {
                    internalId = Guid.NewGuid();
                    asset = new Asset(pass.OrganizationId, internalId, propertyId, spaceId, assetKind, record.Name);
                    operations.Assets.Add(asset);
                    pass.Result.Add();
                }
                else
                {
                    internalId = link.InternalId!.Value;
                    var existing = await operations.Assets.SingleOrDefaultAsync(x => x.Id == internalId, ct);
                    if (existing is null) { pass.Vanished(kind, record.ExternalId, link, internalId); continue; }
                    asset = existing;
                    pass.Result.Update();
                }

                asset.Describe(record.Name, assetKind,
                    ReconciliationRules.PreferSource(record.Manufacturer, asset.Manufacturer),
                    ReconciliationRules.PreferSource(record.Model, asset.Model),
                    ReconciliationRules.PreferSource(record.SerialNumber, asset.SerialNumber));
                // Warranty and service life are PropFlow-only fields that no canonical record
                // carries. Passing nulls would erase whatever an operator entered, so they are read
                // back and written unchanged.
                asset.SetLifecycle(record.InstalledOn ?? asset.InstalledOn,
                    asset.WarrantyExpiresOn, asset.ExpectedServiceLifeYears);

                pending.Add((link, internalId, hash));
                pass.Resolve(kind, record.ExternalId, internalId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                pass.Refused(kind, record.ExternalId, link, "Name", record.Name, exception.Message);
            }
        }

        await CommitAsync(pass, pending, ct);
    }

    // --- The retirement sweep ---------------------------------------------------------------------

    // Links this connection has that the current run did not touch: the source stopped reporting
    // them. Retire the link and raise a conflict; never delete the PropFlow row. An upstream
    // disappearance is not authority to delete a tenant's work order — the source may have changed
    // its filter, lost a record, or be mid-outage, and PropFlow cannot tell those apart.
    //
    // Only kinds the adapter actually supplies are swept. A kind outside
    // IntegrationAdapterDescriptor.SuppliedKinds came back empty because the adapter does not
    // provide it, not because everything of that kind vanished — retiring on that would wipe a
    // tenant's links the first time they pointed a narrower adapter at the same connection.
    private async Task RetireVanishedAsync(Pass pass, CancellationToken ct)
    {
        var suppliedKinds = pass.Descriptor.SuppliedKinds.ToHashSet();
        // Residents are synthesised from occupancies rather than supplied, so they are swept
        // whenever occupancies are.
        if (suppliedKinds.Contains(IntegrationEntityKind.Occupancy))
            suppliedKinds.Add(IntegrationEntityKind.Resident);

        var stale = pass.AllLinks
            .Where(link => link.SyncState != SyncState.Retired
                && link.LastRunId != pass.RunId
                && suppliedKinds.Contains(link.Kind))
            .ToList();

        foreach (var link in stale)
        {
            pass.Conflict(link.Kind, link.ExternalId, new ConflictDetail(ConflictReason.UpstreamDisappearance,
                "ExternalId", null, link.InternalId?.ToString(),
                "The source stopped reporting this record. The PropFlow row was left untouched; decide whether it should be archived."));
            link.Retire(pass.RunId, pass.Now);
            pass.Result.Retire();
        }

        if (stale.Count > 0) await integrations.SaveChangesAsync(ct);
    }

    // --- Commit ordering ---------------------------------------------------------------------------

    // Step 1 then step 2, never the reverse. See the header.
    private async Task CommitAsync(Pass pass, List<(ExternalRecordLink Link, Guid InternalId, string Hash)> pending,
        CancellationToken ct)
    {
        if (operations.ChangeTracker.HasChanges()) await operations.SaveChangesAsync(ct);
        foreach (var (link, internalId, hash) in pending)
            link.Reconcile(internalId, hash, pass.RunId, pass.Now);
        await integrations.SaveChangesAsync(ct);
    }

    private async Task LoadProfilesAsync(Pass pass, Guid connectionId, CancellationToken ct)
    {
        var profiles = await integrations.MappingProfiles
            .Include(x => x.Rules)
            .Where(x => x.ConnectionId == connectionId)
            .ToListAsync(ct);
        foreach (var profile in profiles) pass.Profiles[profile.Kind] = profile;
    }

    private async Task LoadOpenConflictsAsync(Pass pass, Guid connectionId, CancellationToken ct)
    {
        var open = await integrations.Conflicts
            .Where(x => x.ConnectionId == connectionId && x.Status == ConflictStatus.Open)
            .ToListAsync(ct);
        foreach (var conflict in open) pass.OpenConflicts[conflict.Key] = conflict;
    }

    // Mutable state for one reconciliation pass, kept off the reconciler so the reconciler itself
    // stays stateless and safe to resolve per scope.
    private sealed class Pass(
        IntegrationConnection connection,
        IntegrationAdapterDescriptor descriptor,
        IReadOnlyDictionary<(IntegrationEntityKind Kind, string ExternalId), ExternalRecordLink> links,
        Guid runId,
        DateTimeOffset now)
    {
        public Guid OrganizationId { get; } = connection.OrganizationId;
        public Guid ConnectionId { get; } = connection.Id;
        public IntegrationAdapterDescriptor Descriptor { get; } = descriptor;
        public Guid RunId { get; } = runId;
        public DateTimeOffset Now { get; } = now;
        public Counters Result { get; } = new();
        public Dictionary<IntegrationEntityKind, MappingProfile> Profiles { get; } = [];
        public Dictionary<ConflictKey, Conflict> OpenConflicts { get; } = [];
        public IReadOnlyCollection<ExternalRecordLink> AllLinks => links.Values.ToList();

        private readonly Dictionary<(IntegrationEntityKind, string), Guid> resolved = [];
        private readonly List<Conflict> raised = [];

        public IReadOnlyList<Conflict> Raised => raised;

        public bool Supplies(IntegrationEntityKind kind) => Descriptor.Supplies(kind);
        public MappingProfile? Profile(IntegrationEntityKind kind) => Profiles.GetValueOrDefault(kind);

        // Space, Occupancy and Asset need no profile fact of their own, but they must still respect
        // the connection's ReportOnly stance. Without a profile row of their own they inherit the
        // mode of the parent Property profile, which is the one an operator actually promotes.
        public MappingProfile? AnyApplyingProfile(IntegrationEntityKind kind) =>
            Profiles.GetValueOrDefault(kind) ?? Profiles.GetValueOrDefault(IntegrationEntityKind.Property);

        public (ExternalRecordLink Link, string Hash) Link(IntegrationEntityKind kind, string externalId, string hash)
        {
            var link = links[(kind, externalId)];
            return (link, hash);
        }

        public void Resolve(IntegrationEntityKind kind, string externalId, Guid internalId) =>
            resolved[(kind, externalId)] = internalId;

        public Guid? Resolved(IntegrationEntityKind kind, string externalId) =>
            resolved.TryGetValue((kind, externalId), out var id) ? id : null;

        public Guid? ResolvedOrNull(IntegrationEntityKind kind, string? externalId) =>
            externalId is { Length: > 0 } ? Resolved(kind, externalId) : null;

        public ParentState ParentStateOf(IntegrationEntityKind parentKind, string parentExternalId,
            IReadOnlySet<string> inSnapshot)
        {
            if (Resolved(parentKind, parentExternalId) is not null) return ParentState.Resolved;
            return inSnapshot.Contains(parentExternalId)
                ? ParentState.NotReconciledYet
                : ParentState.MissingFromSnapshot;
        }

        /// <summary>
        /// Applies the non-writing outcomes and says whether the caller should go on to write.
        /// Conflict and Skip are terminal for this record; Create, Update and Unchanged are not.
        /// </summary>
        public bool Handle(ReconcileDecision decision, IntegrationEntityKind kind, string externalId,
            ExternalRecordLink link)
        {
            switch (decision.Action)
            {
                case ReconcileAction.Conflict:
                    Conflict(kind, externalId, decision.Conflict!);
                    link.MarkConflicted(decision.Conflict!.Detail ?? decision.Conflict.Reason.ToString(), Now, RunId);
                    Result.Conflict();
                    return false;
                case ReconcileAction.Skip:
                    return false;
                default:
                    return true;
            }
        }

        // Re-observation is idempotent: an open conflict for the same divergence is updated, never
        // duplicated. IX_Conflicts_OpenDivergence enforces that at the database too, so a bug here
        // surfaces as a unique violation rather than a silently growing queue.
        public void Conflict(IntegrationEntityKind kind, string externalId, ConflictDetail detail)
        {
            var key = new ConflictKey(ConnectionId, kind, externalId, detail.Reason, detail.Field);
            if (OpenConflicts.TryGetValue(key, out var existing))
            {
                existing.Observe(RunId, detail.ObservedValue, detail.CurrentValue, detail.Detail, Now);
                return;
            }
            var conflict = new Conflict(OrganizationId, Guid.NewGuid(), ConnectionId, kind, externalId,
                detail.Reason, detail.Field, detail.ObservedValue, detail.CurrentValue, detail.Detail, RunId, Now);
            OpenConflicts[key] = conflict;
            raised.Add(conflict);
        }

        // The PropFlow row a link points at is gone — deleted by a human, most likely. Clear nothing
        // and say so; the next run will re-create it, because the link is left needing
        // reconciliation with no internal id to update.
        public void Vanished(IntegrationEntityKind kind, string externalId, ExternalRecordLink link, Guid internalId)
        {
            Conflict(kind, externalId, new ConflictDetail(ConflictReason.AmbiguousMatch, "InternalId",
                null, internalId.ToString(),
                "The PropFlow row this external record was linked to no longer exists. Resolve to let the next sync re-create it."));
            link.MarkConflicted("The linked PropFlow row no longer exists.", Now, RunId);
            Result.Conflict();
        }

        public void Refused(IntegrationEntityKind kind, string externalId, ExternalRecordLink link,
            string field, string? observed, string message)
        {
            Conflict(kind, externalId, new ConflictDetail(ConflictReason.ValidationRefusal, field, observed, null,
                EfIntegrationOperations.Sanitize(message)));
            link.MarkFailed(EfIntegrationOperations.Sanitize(message), Now, RunId);
            Result.Fail();
        }
    }

    private sealed class Counters
    {
        private int added, updated, failed, conflicted, retired;
        public void Add() => added++;
        public void Update() => updated++;
        public void Fail() => failed++;
        public void Conflict() => conflicted++;
        public void Retire() => retired++;
        public static implicit operator ReconciliationResult(Counters c) =>
            new(c.added, c.updated, c.failed, c.conflicted, c.retired);
    }
}
