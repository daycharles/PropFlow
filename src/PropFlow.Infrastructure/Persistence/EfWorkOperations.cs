using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Work;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;

namespace PropFlow.Infrastructure.Persistence;

public sealed class EfWorkOperations(OperationsStore store, TimeProvider clock) : IWorkOperations
{
    // Kept for existing internal callers; API callers supply the client concurrency version.
    public Task<AssignmentOutcome> AssignVendorAsync(Guid id, Guid vendorId, Guid actorId, CancellationToken ct) =>
        AssignVendorAsync(id, vendorId, actorId, null, ct);
    public async Task<WorkListPage> ListAsync(WorkListQuery q, CancellationToken ct)
    {
        var query = store.WorkItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Search)) { var term = q.Search.Trim(); query = query.Where(x => EF.Functions.ILike(x.Title, $"%{term}%") || (x.Description != null && EF.Functions.ILike(x.Description, $"%{term}%"))); }
        if (q.CategoryId is { } category) query = query.Where(x => x.CategoryId == category);
        if (q.Status is { } status) query = query.Where(x => x.Status == status);
        if (q.Priority is { } priority) query = query.Where(x => x.Priority == priority);
        if (q.PropertyId is { } property) query = query.Where(x => x.PropertyId == property);
        if (q.SpaceId is { } space) query = query.Where(x => x.SpaceId == space);
        var total = await query.CountAsync(ct);
        query = (q.Sort?.ToLowerInvariant(), q.Descending) switch
        {
            ("status", false) => query.OrderBy(x => x.Status).ThenBy(x => x.Id), ("status", true) => query.OrderByDescending(x => x.Status).ThenBy(x => x.Id),
            ("priority", false) => query.OrderBy(x => x.Priority).ThenBy(x => x.Id), ("priority", true) => query.OrderByDescending(x => x.Priority).ThenBy(x => x.Id),
            ("due", false) => query.OrderBy(x => x.DueDate).ThenBy(x => x.Id), ("due", true) => query.OrderByDescending(x => x.DueDate).ThenBy(x => x.Id),
            ("created", false) => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id), ("created", true) => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            ("title", true) => query.OrderByDescending(x => x.Title).ThenBy(x => x.Id), _ => query.OrderBy(x => x.Title).ThenBy(x => x.Id)
        };
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new(items, total, q.Page, q.PageSize);
    }

    public Task<WorkItem?> GetAsync(Guid id, CancellationToken ct) => store.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<uint?> VersionAsync(Guid id, CancellationToken ct) => store.WorkItems.AsNoTracking().Where(x => x.Id == id).Select(x => (uint?)EF.Property<uint>(x, "Version")).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<TimelineEntry>?> TimelineAsync(Guid id, CancellationToken ct) { if (!await store.WorkItems.AnyAsync(x => x.Id == id, ct)) return null; return await store.Timeline.AsNoTracking().Where(x => x.WorkId == id).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync(ct); }

    public async Task<WorkItem> CreateAsync(CreateWorkCommand c, CancellationToken ct)
    {
        if (!await store.Properties.AnyAsync(x => x.Id == c.PropertyId, ct)) throw new KeyNotFoundException("Property not found.");
        var work = new WorkItem(store.OrganizationId, Guid.NewGuid(), c.Title, c.PropertyId, c.ActorId, c.WorkType);
        work.Edit(c.Title, c.Description, c.CategoryId, c.Priority); work.SetLocation(c.PropertyId, c.BuildingId, c.SpaceId, c.ResidentId); work.SetDueDate(c.DueDate); work.SetCost(c.Cost); work.SetNotes(c.InternalNotes, c.ResidentVisibleNotes); work.Publish(clock.GetUtcNow());
        store.WorkItems.Add(work); store.Timeline.Add(Event(work, c.ActorId, "WorkCreated", null, work.Title)); await store.SaveChangesAsync(ct); return work;
    }

    public async Task<WorkWriteOutcome> UpdateAsync(Guid id, UpdateWorkCommand c, CancellationToken ct)
    {
        var work = await store.WorkItems.SingleOrDefaultAsync(x => x.Id == id, ct); if (work is null) return WorkWriteOutcome.NotFound;
        store.Entry(work).Property("Version").OriginalValue = c.Version;
        var now = clock.GetUtcNow(); var oldTitle = work.Title; var oldPriority = work.Priority; var oldStatus = work.Status; var oldStart = work.ScheduledStart;
        work.Edit(c.Title, c.Description, c.CategoryId, c.Priority); work.SetLocation(c.PropertyId, c.BuildingId, c.SpaceId, c.ResidentId); work.SetDueDate(c.DueDate); work.SetCost(c.Cost); work.SetNotes(c.InternalNotes, c.ResidentVisibleNotes);
        if (c.Status is { } status && status != work.Status) work.ChangeStatus(status, now);
        if (c.ScheduledStart is { } start && (start != oldStart || c.ScheduledEnd != work.ScheduledEnd)) work.Schedule(start, c.ScheduledEnd);
        if (oldTitle != work.Title) store.Timeline.Add(Event(work, c.ActorId, "WorkUpdated", oldTitle, work.Title));
        if (oldPriority != work.Priority) store.Timeline.Add(Event(work, c.ActorId, "PriorityChanged", oldPriority.ToString(), work.Priority.ToString()));
        if (oldStatus != work.Status) store.Timeline.Add(Event(work, c.ActorId, "StatusChanged", oldStatus.ToString(), work.Status.ToString()));
        if (oldStart != work.ScheduledStart) store.Timeline.Add(Event(work, c.ActorId, "Scheduled", oldStart?.ToString("O"), work.ScheduledStart?.ToString("O")));
        try { await store.SaveChangesAsync(ct); return WorkWriteOutcome.Updated; } catch (DbUpdateConcurrencyException) { return WorkWriteOutcome.Conflict; }
    }

    public async Task<AssignmentOutcome> AssignVendorAsync(Guid id, Guid vendorId, Guid actorId, uint? version, CancellationToken ct)
    {
        var work = await store.WorkItems.SingleOrDefaultAsync(x => x.Id == id, ct); if (work is null || !await store.Vendors.AnyAsync(x => x.Id == vendorId, ct)) return AssignmentOutcome.NotFound;
        if (version is { } v) store.Entry(work).Property("Version").OriginalValue = v;
        var change = work.AssignVendor(vendorId, actorId, clock.GetUtcNow()); if (change is null) return AssignmentOutcome.Unchanged;
        store.Timeline.Add(TimelineEntry.From(change)); try { await store.SaveChangesAsync(ct); return AssignmentOutcome.Updated; } catch (DbUpdateConcurrencyException) { return AssignmentOutcome.Conflict; }
    }

    public async Task<AssignmentOutcome> BulkAssignVendorAsync(IReadOnlyList<BulkVendorAssignment> items, Guid vendorId, Guid actorId, CancellationToken ct)
    {
        if (items.Count is 0 or > 100 || items.Select(x => x.WorkId).Distinct().Count() != items.Count) return AssignmentOutcome.NotFound;
        if (!await store.Vendors.AnyAsync(x => x.Id == vendorId, ct)) return AssignmentOutcome.NotFound;
        await using var transaction = await store.Database.BeginTransactionAsync(ct);
        var ids = items.Select(x => x.WorkId).ToArray(); var works = await store.WorkItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct); if (works.Count != items.Count) return AssignmentOutcome.NotFound;
        var byId = items.ToDictionary(x => x.WorkId); var changed = false;
        foreach (var work in works) { store.Entry(work).Property("Version").OriginalValue = byId[work.Id].Version; var e = work.AssignVendor(vendorId, actorId, clock.GetUtcNow()); if (e is not null) { changed = true; store.Timeline.Add(TimelineEntry.From(e)); } }
        try { await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return changed ? AssignmentOutcome.Updated : AssignmentOutcome.Unchanged; } catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); return AssignmentOutcome.Conflict; }
    }

    private TimelineEntry Event(WorkItem work, Guid actor, string type, string? oldValue, string? newValue) => TimelineEntry.Record(work.OrganizationId, actor, clock.GetUtcNow(), type, "WorkItem", work.Id, oldValue, newValue, work.Id, JsonSerializer.Serialize(new { oldValue, newValue }));
}
