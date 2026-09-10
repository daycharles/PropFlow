using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application.Work;
using PropFlow.Domain.Communications;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;
using PropFlow.Infrastructure.Communications;

namespace PropFlow.Infrastructure.Persistence;

public sealed class EfWorkOperations(OperationsStore store, CommunicationsStore comms, TimeProvider clock) : IWorkOperations
{
    // Kept for existing internal callers; API callers supply the client concurrency version.
    public Task<AssignmentOutcome> AssignVendorAsync(Guid id, Guid vendorId, Guid actorId, CancellationToken ct) =>
        AssignVendorAsync(id, vendorId, actorId, null, ct);
    public async Task<WorkListPage> ListAsync(WorkListQuery q, CancellationToken ct)
    {
        var query = store.WorkItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Escape LIKE metacharacters so a search for "50%" or "a_b" is a literal match, not a
            // wildcard (and not a forced full scan). EF still parameterizes the value.
            var pattern = "%" + q.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Title, pattern, "\\")
                || (x.Description != null && EF.Functions.ILike(x.Description, pattern, "\\")));
        }
        if (q.CategoryId is { } category) query = query.Where(x => x.CategoryId == category);
        if (q.Status is { } status) query = query.Where(x => x.Status == status);
        if (q.Priority is { } priority) query = query.Where(x => x.Priority == priority);
        if (q.PropertyId is { } property) query = query.Where(x => x.PropertyId == property);
        if (q.SpaceId is { } space) query = query.Where(x => x.SpaceId == space);
        var total = await query.CountAsync(ct);
        // One query: the vendor/property/category names and the xmin version join onto the row, so the
        // list costs a single round trip regardless of page size. Ordering and paging stay in the same
        // SELECT as the joins; EF cannot order through a record constructor, hence the anonymous shape.
        var joined =
            from x in query
            join p in store.Properties on x.PropertyId equals p.Id into properties
            from p in properties.DefaultIfEmpty()
            join c in store.Categories on x.CategoryId equals (Guid?)c.Id into categories
            from c in categories.DefaultIfEmpty()
            join v in store.Vendors on x.VendorId equals (Guid?)v.Id into vendors
            from v in vendors.DefaultIfEmpty()
            select new
            {
                Work = x,
                PropertyName = p == null ? null : p.Name,
                CategoryName = c == null ? null : c.Name,
                VendorName = v == null ? null : v.Name,
                Version = EF.Property<uint>(x, "Version")
            };
        joined = (q.Sort?.ToLowerInvariant(), q.Descending) switch
        {
            ("status", false) => joined.OrderBy(t => t.Work.Status).ThenBy(t => t.Work.Id), ("status", true) => joined.OrderByDescending(t => t.Work.Status).ThenBy(t => t.Work.Id),
            ("priority", false) => joined.OrderBy(t => t.Work.Priority).ThenBy(t => t.Work.Id), ("priority", true) => joined.OrderByDescending(t => t.Work.Priority).ThenBy(t => t.Work.Id),
            // "due" is the original spelling and stays a working alias for "dueDate".
            ("due" or "duedate", false) => joined.OrderBy(t => t.Work.DueDate).ThenBy(t => t.Work.Id), ("due" or "duedate", true) => joined.OrderByDescending(t => t.Work.DueDate).ThenBy(t => t.Work.Id),
            ("created", false) => joined.OrderBy(t => t.Work.CreatedAt).ThenBy(t => t.Work.Id), ("created", true) => joined.OrderByDescending(t => t.Work.CreatedAt).ThenBy(t => t.Work.Id),
            ("title", true) => joined.OrderByDescending(t => t.Work.Title).ThenBy(t => t.Work.Id), _ => joined.OrderBy(t => t.Work.Title).ThenBy(t => t.Work.Id)
        };
        var rows = await joined.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var items = rows.Select(t => new WorkListItem(t.Work.Id, t.Work.Title, t.Work.Description, t.Work.Status, t.Work.Priority, t.Work.WorkType,
            t.Work.PropertyId, t.PropertyName, t.Work.BuildingId, t.Work.SpaceId,
            t.Work.CategoryId, t.CategoryName, t.Work.VendorId, t.VendorName, t.Work.EmployeeId,
            t.Work.DueDate, t.Work.CreatedAt, t.Version)).ToList();
        return new(items, total, q.Page, q.PageSize);
    }

    public Task<WorkItem?> GetAsync(Guid id, CancellationToken ct) => store.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<uint?> VersionAsync(Guid id, CancellationToken ct) => store.WorkItems.AsNoTracking().Where(x => x.Id == id).Select(x => (uint?)EF.Property<uint>(x, "Version")).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<TimelineItem>?> TimelineAsync(Guid id, CancellationToken ct)
    {
        if (!await store.WorkItems.AnyAsync(x => x.Id == id, ct)) return null;

        // An entry may hang off a work item either directly (WorkId) or only by related-object
        // reference. The OR keeps both reachable without duplicating the rows that set both.
        var events = await store.Timeline.AsNoTracking()
            .Where(x => x.WorkId == id || (x.RelatedObjectType == "WorkItem" && x.RelatedObjectId == id))
            .Select(x => new TimelineItem(x.Id, x.EventType, x.OccurredAt, x.ActorId,
                x.OldValue, x.NewValue, x.RelatedObjectType, x.RelatedObjectId, x.Changes, false))
            .ToListAsync(ct);

        // Communications live in their own context; a work item's messages are folded into its
        // history on read, so there is no cross-context write.
        var messages = await comms.OutboxMessages.AsNoTracking().Where(x => x.WorkId == id)
            .Select(x => new { x.Id, x.Channel, x.RecipientAddress, x.Subject, x.Status, x.CreatedAt, x.LastAttemptAt, x.ProviderReference, x.FailureReason, x.ResidentVisible })
            .ToListAsync(ct);

        events.AddRange(messages.Select(m => new TimelineItem(
            m.Id,
            m.Status switch
            {
                OutboxStatus.Sent => "MessageSent",
                OutboxStatus.Failed => "MessageFailed",
                OutboxStatus.Sending => "MessageSending",
                _ => "MessageQueued"
            },
            m.LastAttemptAt ?? m.CreatedAt, null,
            m.Channel.ToString(), $"{m.Channel} to {m.RecipientAddress}",
            "OutboxMessage", m.Id,
            JsonSerializer.Serialize(new { channel = m.Channel.ToString(), recipient = m.RecipientAddress, subject = m.Subject, status = m.Status.ToString(), providerReference = m.ProviderReference, failureReason = m.FailureReason }),
            m.ResidentVisible)));

        return events.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToList();
    }

    public async Task<WorkItem> CreateAsync(CreateWorkCommand c, CancellationToken ct)
    {
        if (!await store.Properties.AnyAsync(x => x.Id == c.PropertyId, ct)) throw new KeyNotFoundException("Property not found.");
        await EnsureChildReferencesAsync(c.BuildingId, c.SpaceId, c.CategoryId, c.ResidentId, ct);
        var work = new WorkItem(store.OrganizationId, Guid.NewGuid(), c.Title, c.PropertyId, c.ActorId, c.WorkType);
        work.Edit(c.Title, c.Description, c.CategoryId, c.Priority); work.SetLocation(c.PropertyId, c.BuildingId, c.SpaceId, c.ResidentId); work.SetDueDate(c.DueDate); work.SetCost(c.Cost); work.SetNotes(c.InternalNotes, c.ResidentVisibleNotes); work.Publish(clock.GetUtcNow());
        store.WorkItems.Add(work); store.Timeline.Add(Event(work, c.ActorId, "WorkCreated", null, work.Title)); await store.SaveChangesAsync(ct); return work;
    }

    public async Task<WorkWriteOutcome> UpdateAsync(Guid id, UpdateWorkCommand c, CancellationToken ct)
    {
        var work = await store.WorkItems.SingleOrDefaultAsync(x => x.Id == id, ct); if (work is null) return WorkWriteOutcome.NotFound;
        if (!await store.Properties.AnyAsync(x => x.Id == c.PropertyId, ct)) throw new KeyNotFoundException("Property not found.");
        await EnsureChildReferencesAsync(c.BuildingId, c.SpaceId, c.CategoryId, c.ResidentId, ct);
        store.Entry(work).Property("Version").OriginalValue = c.Version;
        var now = clock.GetUtcNow(); var oldTitle = work.Title; var oldPriority = work.Priority; var oldStatus = work.Status; var oldStart = work.ScheduledStart;
        work.Edit(c.Title, c.Description, c.CategoryId, c.Priority); work.SetLocation(c.PropertyId, c.BuildingId, c.SpaceId, c.ResidentId); work.SetDueDate(c.DueDate); work.SetCost(c.Cost); work.SetNotes(c.InternalNotes, c.ResidentVisibleNotes);
        if (c.Status is { } status && status != work.Status) work.ChangeStatus(status, now);
        if (c.ScheduledStart is { } start && (start != oldStart || c.ScheduledEnd != work.ScheduledEnd)) work.Schedule(start, c.ScheduledEnd);
        if (oldTitle != work.Title) store.Timeline.Add(Event(work, c.ActorId, "WorkUpdated", oldTitle, work.Title));
        if (oldPriority != work.Priority) store.Timeline.Add(Event(work, c.ActorId, "PriorityChanged", oldPriority.ToString(), work.Priority.ToString()));
        if (oldStatus != work.Status) store.Timeline.Add(Event(work, c.ActorId, "StatusChanged", oldStatus.ToString(), work.Status.ToString()));
        if (oldStart != work.ScheduledStart) store.Timeline.Add(Event(work, c.ActorId, "Scheduled", oldStart?.ToString("O"), work.ScheduledStart?.ToString("O"), now));
        try { await store.SaveChangesAsync(ct); return WorkWriteOutcome.Updated; } catch (DbUpdateConcurrencyException) { return WorkWriteOutcome.Conflict; }
    }

    public async Task<AssignmentOutcome> AssignVendorAsync(Guid id, Guid vendorId, Guid actorId, uint? version, CancellationToken ct)
    {
        var work = await store.WorkItems.SingleOrDefaultAsync(x => x.Id == id, ct); if (work is null || !await store.Vendors.AnyAsync(x => x.Id == vendorId, ct)) return AssignmentOutcome.NotFound;
        // Ask before telling: the domain throws on terminal work, and this layer reports outcomes.
        if (work.IsTerminal) return AssignmentOutcome.NotAssignable;
        if (version is { } v) store.Entry(work).Property("Version").OriginalValue = v;
        var change = work.AssignVendor(vendorId, actorId, clock.GetUtcNow()); if (change is null) return AssignmentOutcome.Unchanged;
        store.Timeline.Add(TimelineEntry.From(change)); try { await store.SaveChangesAsync(ct); return AssignmentOutcome.Updated; } catch (DbUpdateConcurrencyException) { return AssignmentOutcome.Conflict; }
    }

    public async Task<AssignmentOutcome> AssignEmployeeAsync(Guid id, Guid employeeId, Guid actorId, uint? version, CancellationToken ct)
    {
        var work = await store.WorkItems.SingleOrDefaultAsync(x => x.Id == id, ct); if (work is null || !await store.Employees.AnyAsync(x => x.Id == employeeId, ct)) return AssignmentOutcome.NotFound;
        if (work.IsTerminal) return AssignmentOutcome.NotAssignable;
        if (version is { } v) store.Entry(work).Property("Version").OriginalValue = v;
        var change = work.AssignEmployee(employeeId, actorId, clock.GetUtcNow()); if (change is null) return AssignmentOutcome.Unchanged;
        store.Timeline.Add(TimelineEntry.From(change));
        try { await store.SaveChangesAsync(ct); return AssignmentOutcome.Updated; } catch (DbUpdateConcurrencyException) { return AssignmentOutcome.Conflict; }
    }

    public async Task<BulkAssignmentSummary> BulkAssignVendorAsync(IReadOnlyList<BulkWorkItemRef> items, Guid vendorId, Guid actorId, CancellationToken ct)
    {
        var total = items.Count;
        if (items.Count is 0 or > 100 || items.Select(x => x.WorkId).Distinct().Count() != items.Count) return new(AssignmentOutcome.NotFound, 0, 0, total);
        if (!await store.Vendors.AnyAsync(x => x.Id == vendorId, ct)) return new(AssignmentOutcome.NotFound, 0, 0, total);
        await using var transaction = await store.Database.BeginTransactionAsync(ct);
        var ids = items.Select(x => x.WorkId).ToArray(); var works = await store.WorkItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct); if (works.Count != items.Count) return new(AssignmentOutcome.NotFound, 0, 0, total);
        // All-or-nothing includes assignability: one terminal item refuses the whole batch, before
        // anything is mutated. A select-all over an unfiltered list must not quietly hand a vendor
        // to completed and cancelled work.
        if (works.Any(x => x.IsTerminal)) return new(AssignmentOutcome.NotAssignable, 0, 0, total);
        var byId = items.ToDictionary(x => x.WorkId); var changed = 0;
        foreach (var work in works) { store.Entry(work).Property("Version").OriginalValue = byId[work.Id].Version; var e = work.AssignVendor(vendorId, actorId, clock.GetUtcNow()); if (e is not null) { changed++; store.Timeline.Add(TimelineEntry.From(e)); } }
        try { await store.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return new(changed > 0 ? AssignmentOutcome.Updated : AssignmentOutcome.Unchanged, changed, total - changed, total); }
        // All-or-nothing: the transaction rolls back, so nothing was changed and nothing is reported as changed.
        catch (DbUpdateConcurrencyException) { await transaction.RollbackAsync(ct); return new(AssignmentOutcome.Conflict, 0, 0, total); }
    }

    public async Task<BulkAssignmentSummary> BulkApplyAsync(BulkWorkCommand command, CancellationToken ct)
    {
        var items = command.Items;
        var total = items.Count;
        if (total is 0 or > 100 || items.Select(x => x.WorkId).Distinct().Count() != total)
            return new(AssignmentOutcome.NotFound, 0, 0, total);

        await using var transaction = await store.Database.BeginTransactionAsync(ct);
        var ids = items.Select(x => x.WorkId).ToArray();
        var works = await store.WorkItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (works.Count != total) return new(AssignmentOutcome.NotFound, 0, 0, total);

        // Assignability is part of all-or-nothing: one item that cannot take the action refuses
        // the whole batch before anything is mutated.
        var needsOpen = command.Action is BulkWorkAction.Status or BulkWorkAction.Priority or BulkWorkAction.Schedule;
        if (needsOpen && works.Any(x => x.IsTerminal)) return new(AssignmentOutcome.NotAssignable, 0, 0, total);
        if (command.Action is BulkWorkAction.Reopen && works.Any(x => !x.IsTerminal)) return new(AssignmentOutcome.NotAssignable, 0, 0, total);
        if (command.Action is BulkWorkAction.Schedule && works.Any(x => x.VendorId is null && x.EmployeeId is null))
            return new(AssignmentOutcome.NotAssignable, 0, 0, total);

        var byId = items.ToDictionary(x => x.WorkId);

        // Explicit per-item concurrency check up front: the `note` action does not mutate the
        // work row, so it would not otherwise trip the store's optimistic-concurrency guard.
        if (works.Any(w => (uint)store.Entry(w).Property("Version").CurrentValue! != byId[w.Id].Version))
        {
            await transaction.RollbackAsync(ct);
            return new(AssignmentOutcome.Conflict, 0, 0, total);
        }

        var now = clock.GetUtcNow();
        var changed = 0;
        foreach (var work in works)
        {
            store.Entry(work).Property("Version").OriginalValue = byId[work.Id].Version;
            if (ApplyOne(work, command, now)) changed++;
        }

        try
        {
            await store.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(changed > 0 ? AssignmentOutcome.Updated : AssignmentOutcome.Unchanged, changed, total - changed, total);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return new(AssignmentOutcome.Conflict, 0, 0, total);
        }
    }

    // Applies one already-vetted action to one work item and records its timeline entry.
    // Returns whether the item actually changed (a no-op status/priority is "unchanged").
    private bool ApplyOne(WorkItem work, BulkWorkCommand c, DateTimeOffset now)
    {
        switch (c.Action)
        {
            case BulkWorkAction.Status:
                var target = c.Status!.Value;
                if (work.Status == target) return false;
                var fromStatus = work.Status;
                work.ChangeStatus(target, now);
                store.Timeline.Add(Event(work, c.ActorId, "StatusChanged", fromStatus.ToString(), work.Status.ToString(), now));
                return true;
            case BulkWorkAction.Priority:
                var priority = c.Priority!.Value;
                if (work.Priority == priority) return false;
                var fromPriority = work.Priority;
                work.SetPriority(priority);
                store.Timeline.Add(Event(work, c.ActorId, "PriorityChanged", fromPriority.ToString(), work.Priority.ToString(), now));
                return true;
            case BulkWorkAction.Schedule:
                var oldStart = work.ScheduledStart;
                // Stored timestamps are microsecond-precision (Postgres timestamptz); the request
                // value may carry finer ticks. A window that matches to the second is a no-op.
                static bool SameInstant(DateTimeOffset? a, DateTimeOffset? b) =>
                    (a is null) == (b is null) && (a is not { } x || b is not { } y || Math.Abs((x - y).TotalSeconds) < 1);
                if (work.Status == WorkStatus.Scheduled
                    && SameInstant(oldStart, c.ScheduledStart) && SameInstant(work.ScheduledEnd, c.ScheduledEnd))
                    return false;
                work.Schedule(c.ScheduledStart!.Value, c.ScheduledEnd);
                store.Timeline.Add(Event(work, c.ActorId, "Scheduled", oldStart?.ToString("O"), work.ScheduledStart?.ToString("O"), now));
                return true;
            case BulkWorkAction.Note:
                store.Timeline.Add(TimelineEntry.Record(work.OrganizationId, c.ActorId, now, "WorkNote", "WorkItem", work.Id,
                    null, c.Note, work.Id, JsonSerializer.Serialize(new { note = c.Note, visibility = c.NoteInternal ? "internal" : "resident" })));
                return true;
            case BulkWorkAction.Reopen:
                store.Timeline.Add(TimelineEntry.From(work.Reopen(c.ActorId, now)));
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(c), c.Action, "Unknown bulk action.");
        }
    }

    private TimelineEntry Event(WorkItem work, Guid actor, string type, string? oldValue, string? newValue, DateTimeOffset? at = null) => TimelineEntry.Record(work.OrganizationId, actor, at ?? clock.GetUtcNow(), type, "WorkItem", work.Id, oldValue, newValue, work.Id, JsonSerializer.Serialize(new { oldValue, newValue }));

    // The optional location/category refs on a work item carry no FK (they are nullable and
    // cross several tables), so a create/update could otherwise stash a dangling or foreign id.
    // Each lookup runs through the tenant query filter, so a foreign id reads as "not found".
    private async Task EnsureChildReferencesAsync(Guid? buildingId, Guid? spaceId, Guid? categoryId, Guid? residentId, CancellationToken ct)
    {
        if (buildingId is { } b && !await store.Buildings.AnyAsync(x => x.Id == b, ct)) throw new ArgumentException("Unknown building.", nameof(buildingId));
        if (spaceId is { } s && !await store.Spaces.AnyAsync(x => x.Id == s, ct)) throw new ArgumentException("Unknown space.", nameof(spaceId));
        if (categoryId is { } cat && !await store.Categories.AnyAsync(x => x.Id == cat, ct)) throw new ArgumentException("Unknown category.", nameof(categoryId));
        if (residentId is { } r && !await store.Residents.AnyAsync(x => x.Id == r, ct)) throw new ArgumentException("Unknown resident.", nameof(residentId));
    }
}
