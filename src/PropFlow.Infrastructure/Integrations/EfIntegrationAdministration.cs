using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Integrations;
using PropFlow.Domain.Integrations;

namespace PropFlow.Infrastructure.Integrations;

// The conflict queue, the mapping configuration, the run history and manual retirement, all
// scoped to one connection inside the current tenant.
//
// Every method takes the connection id and checks it, so a conflict or a run from another
// connection cannot be reached by guessing an id. RLS already confines rows to the organization;
// this is the narrower check that keeps one connection's queue out of another's.
public sealed class EfIntegrationAdministration(IntegrationStore store, TimeProvider clock)
    : IIntegrationAdministration
{
    public const int MaxPageSize = 200;

    // --- Conflicts ------------------------------------------------------------------------------

    public async Task<ConflictsPage?> ConflictsAsync(Guid connectionId, ConflictFilter filter, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await ConnectionExistsAsync(connectionId, cancellationToken)) return null;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var all = store.Conflicts.AsNoTracking().Where(x => x.ConnectionId == connectionId);
        // The badge is the connection's open total and must not move as the caller pages.
        var openCount = await all.CountAsync(x => x.Status == ConflictStatus.Open, cancellationToken);

        // The latest COMPLETED run, not the latest run: a run that failed mid-pull saw only part of
        // the source, so judging staleness against it would mark every conflict it never reached as
        // fixed. A Running one is worse — it has not finished looking yet.
        var latestRunId = await LatestCompletedRunIdAsync(connectionId, cancellationToken);
        var staleCount = latestRunId is null
            ? 0
            : await all.CountAsync(x => x.Status == ConflictStatus.Open && x.LastSeenInRunId != latestRunId,
                cancellationToken);

        var rows = all;
        if (filter.Status is { } status) rows = rows.Where(x => x.Status == status);
        if (filter.Kind is { } kind) rows = rows.Where(x => x.Kind == kind);
        if (filter.Reason is { } reason) rows = rows.Where(x => x.Reason == reason);

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            // Open first, then most recently seen: a queue is worked from the top. Ordering by the
            // Status column itself would NOT do that — it is HasConversion<string>(), so the
            // database sorts the member names and "Ignored" lands above "Open".
            .OrderBy(x => x.Status == ConflictStatus.Open ? 0 : 1)
            .ThenByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ConflictsPage(items.Select(c => Project(c, latestRunId)).ToList(),
            total, openCount, staleCount, page, pageSize);
    }

    public async Task<ConflictWriteOutcome> CloseConflictAsync(Guid connectionId, Guid conflictId, Guid actorId,
        string? note, bool ignore, CancellationToken cancellationToken)
    {
        var conflict = await store.Conflicts
            .SingleOrDefaultAsync(x => x.Id == conflictId && x.ConnectionId == connectionId, cancellationToken);
        if (conflict is null) return ConflictWriteOutcome.NotFound;
        // Conflict.Resolve/Ignore throw on a closed row. Pre-checked so the endpoint answers 409
        // instead of an InvalidOperationException reaching the global handler as a 500.
        if (!conflict.IsOpen) return ConflictWriteOutcome.AlreadyClosed;

        var now = clock.GetUtcNow();
        if (ignore) conflict.Ignore(actorId, now, note); else conflict.Resolve(actorId, now, note);
        await store.SaveChangesAsync(cancellationToken);
        return ConflictWriteOutcome.Updated;
    }

    // --- Mapping profiles and rules ----------------------------------------------------------------

    public async Task<IReadOnlyList<MappingProfileView>?> MappingProfilesAsync(Guid connectionId,
        CancellationToken cancellationToken)
    {
        if (!await ConnectionExistsAsync(connectionId, cancellationToken)) return null;
        var profiles = await store.MappingProfiles.AsNoTracking()
            .Include(x => x.Rules)
            .Where(x => x.ConnectionId == connectionId)
            .OrderBy(x => x.Kind)
            .ToListAsync(cancellationToken);
        return profiles.Select(Project).ToList();
    }

    public async Task<(MappingWriteOutcome Outcome, Guid Id)> SaveMappingProfileAsync(Guid connectionId,
        IntegrationEntityKind kind, SaveMappingProfileCommand command, CancellationToken cancellationToken)
    {
        if (!await ConnectionExistsAsync(connectionId, cancellationToken))
            return (MappingWriteOutcome.NotFound, Guid.Empty);

        var now = clock.GetUtcNow();
        var profile = await LoadProfileAsync(connectionId, kind, cancellationToken);
        var created = profile is null;
        if (profile is null)
        {
            // Constructed ReportOnly, always. There is no overload that starts in AutoApply, so a
            // first connect cannot silently rewrite a tenant's rows.
            profile = new MappingProfile(store.OrganizationId, Guid.NewGuid(), connectionId, kind, now);
            store.MappingProfiles.Add(profile);
        }

        // SetDefaults demotes an AutoApply profile that this edit invalidated. Deliberate, and in
        // the safe direction.
        profile.SetDefaults(command.TargetPortfolioId, command.DefaultCreatorId, command.DefaultTimeZoneId, now);
        await store.SaveChangesAsync(cancellationToken);
        return (created ? MappingWriteOutcome.Created : MappingWriteOutcome.Updated, profile.Id);
    }

    public async Task<(MappingWriteOutcome Outcome, Guid Id)> AddMappingRuleAsync(Guid connectionId,
        IntegrationEntityKind kind, SaveMappingRuleCommand command, CancellationToken cancellationToken)
    {
        if (!await ConnectionExistsAsync(connectionId, cancellationToken))
            return (MappingWriteOutcome.NotFound, Guid.Empty);
        // A rule whose source field belongs to another kind is a caller mistake the profile would
        // throw on. Reported as an outcome so it is a 400 with a usable message rather than a 500.
        if (MappingVocabulary.AppliesTo(command.SourceField) != kind)
            return (MappingWriteOutcome.NotApplicable, Guid.Empty);

        var now = clock.GetUtcNow();
        var profile = await LoadProfileAsync(connectionId, kind, cancellationToken);
        if (profile is null)
        {
            profile = new MappingProfile(store.OrganizationId, Guid.NewGuid(), connectionId, kind, now);
            store.MappingProfiles.Add(profile);
        }

        // The MappingRule constructor throws ArgumentException on blank or over-long values; that
        // is a caller input error and ApiExceptionHandler already maps it to 400.
        var rule = new MappingRule(store.OrganizationId, Guid.NewGuid(), profile.Id,
            command.SourceField, command.SourceValue, command.TargetValue);
        profile.AddRule(rule, now);
        await store.SaveChangesAsync(cancellationToken);
        return (MappingWriteOutcome.Created, rule.Id);
    }

    public async Task<MappingWriteOutcome> RemoveMappingRuleAsync(Guid connectionId, IntegrationEntityKind kind,
        Guid ruleId, CancellationToken cancellationToken)
    {
        var profile = await LoadProfileAsync(connectionId, kind, cancellationToken);
        if (profile is null) return MappingWriteOutcome.NotFound;
        if (!profile.RemoveRule(ruleId, clock.GetUtcNow())) return MappingWriteOutcome.NotFound;

        // RemoveRule detaches the rule from the profile's collection; the row itself has to go.
        var rule = await store.MappingRules.SingleOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is not null) store.MappingRules.Remove(rule);
        await store.SaveChangesAsync(cancellationToken);
        return MappingWriteOutcome.Updated;
    }

    public async Task<(MappingPromotionOutcome Outcome, IReadOnlyList<MappingIssue> Issues)> PromoteAsync(
        Guid connectionId, IntegrationEntityKind kind, CancellationToken cancellationToken)
    {
        var profile = await LoadProfileAsync(connectionId, kind, cancellationToken);
        if (profile is null) return (MappingPromotionOutcome.NotFound, []);

        // Validate() is pure, so the refusal costs nothing and never touches the source system.
        // This is the guard rail: a profile missing a portfolio id or a work-order status rule
        // cannot start writing to a tenant's rows.
        var issues = profile.Validate();
        if (issues.Any(i => i.Severity == MappingIssueSeverity.Error))
            return (MappingPromotionOutcome.HasErrors, issues);

        profile.PromoteToAutoApply(clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
        return (MappingPromotionOutcome.Promoted, issues);
    }

    public async Task<MappingWriteOutcome> RevertToReportOnlyAsync(Guid connectionId, IntegrationEntityKind kind,
        CancellationToken cancellationToken)
    {
        var profile = await LoadProfileAsync(connectionId, kind, cancellationToken);
        if (profile is null) return MappingWriteOutcome.NotFound;
        profile.RevertToReportOnly(clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
        return MappingWriteOutcome.Updated;
    }

    // --- Runs ---------------------------------------------------------------------------------------

    public async Task<SyncRunsPage?> RunsAsync(Guid connectionId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await ConnectionExistsAsync(connectionId, cancellationToken)) return null;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var rows = store.SyncRuns.AsNoTracking().Where(x => x.ConnectionId == connectionId);
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderByDescending(x => x.StartedAt)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SyncRunsPage(items.Select(Project).ToList(), total, page, pageSize);
    }

    public async Task<SyncRunView?> RunAsync(Guid connectionId, Guid runId, CancellationToken cancellationToken)
    {
        var run = await store.SyncRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runId && x.ConnectionId == connectionId, cancellationToken);
        return run is null ? null : Project(run);
    }

    // --- Retire -------------------------------------------------------------------------------------

    public async Task<RecordRetireOutcome> RetireRecordAsync(Guid connectionId, Guid recordId, Guid actorId,
        CancellationToken cancellationToken)
    {
        var link = await store.RecordLinks
            .SingleOrDefaultAsync(x => x.Id == recordId && x.ConnectionId == connectionId, cancellationToken);
        if (link is null) return RecordRetireOutcome.NotFound;
        if (link.SyncState == SyncState.Retired) return RecordRetireOutcome.AlreadyRetired;

        // The PropFlow row the link points at is deliberately left alone. Retiring a link says
        // "stop reconciling this", not "delete the tenant's work order".
        link.RetireManually(actorId, clock.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
        return RecordRetireOutcome.Retired;
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private Task<bool> ConnectionExistsAsync(Guid connectionId, CancellationToken cancellationToken) =>
        store.Connections.AsNoTracking().AnyAsync(x => x.Id == connectionId, cancellationToken);

    // Tracked, with rules, because every caller of this mutates the profile.
    private Task<MappingProfile?> LoadProfileAsync(Guid connectionId, IntegrationEntityKind kind,
        CancellationToken cancellationToken) =>
        store.MappingProfiles
            .Include(x => x.Rules)
            .SingleOrDefaultAsync(x => x.ConnectionId == connectionId && x.Kind == kind, cancellationToken);

    private Task<Guid?> LatestCompletedRunIdAsync(Guid connectionId, CancellationToken cancellationToken) =>
        store.SyncRuns.AsNoTracking()
            .Where(x => x.ConnectionId == connectionId && x.Status == SyncRunStatus.Completed)
            .OrderByDescending(x => x.StartedAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    // latestRunId null — the connection has never completed a run — means nothing can be judged
    // stale yet, so every conflict reads as currently seen.
    private static ConflictView Project(Conflict c, Guid? latestRunId) => new(
        c.Id, c.ConnectionId, c.Kind, c.ExternalId, c.Reason, c.Field, c.ObservedValue, c.CurrentValue,
        c.Detail, c.Status, c.FirstSeenInRunId, c.LastSeenInRunId, c.FirstSeenAt, c.LastSeenAt,
        c.ObservationCount, c.ResolvedByUserId, c.ResolvedAt, c.ResolutionNote,
        latestRunId is null || c.LastSeenInRunId == latestRunId);

    private static SyncRunView Project(SyncRun r) => new(
        r.Id, r.ConnectionId, r.Trigger, r.AttemptNumber, r.Status, r.StartedAt, r.HeartbeatAt, r.CompletedAt,
        r.Seen, r.Added, r.Updated, r.Failed, r.Conflicted, r.Error, r.SnapshotHash);

    private static MappingProfileView Project(MappingProfile p)
    {
        var issues = p.Validate();
        return new MappingProfileView(
            p.Id, p.ConnectionId, p.Kind, p.Mode, p.TargetPortfolioId, p.DefaultCreatorId, p.DefaultTimeZoneId,
            p.CreatedAt, p.UpdatedAt,
            p.Rules.OrderBy(r => r.SourceField).ThenBy(r => r.SourceValue)
                .Select(r => new MappingRuleView(r.Id, r.SourceField, r.SourceValue, r.TargetValue)).ToList(),
            issues,
            !issues.Any(i => i.Severity == MappingIssueSeverity.Error),
            MappingVocabulary.For(p.Kind));
    }
}
