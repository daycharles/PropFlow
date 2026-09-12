using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain;
using PropFlow.Domain.Integrations;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Integrations;

// Tenant-scoped store for the Integrations module. Its own schema and migration history keep it
// independent of Operations and Communications. Same five-layer tenant defence as the other
// contexts: composite key, central query filter, connection GUC, forced RLS in the migration,
// least-privilege grants in DatabaseProvisioner.
public sealed class IntegrationStore(DbContextOptions<IntegrationStore> options, ITenantContext tenant) : DbContext(options)
{
    public Guid OrganizationId => tenant.OrganizationId;
    public DbSet<IntegrationConnection> Connections => Set<IntegrationConnection>();
    public DbSet<ExternalRecordLink> RecordLinks => Set<ExternalRecordLink>();
    public DbSet<MappingProfile> MappingProfiles => Set<MappingProfile>();
    public DbSet<MappingRule> MappingRules => Set<MappingRule>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<Conflict> Conflicts => Set<Conflict>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new TenantConnectionInterceptor(tenant));

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("integrations");

        model.Entity<IntegrationConnection>(entity =>
        {
            entity.ToTable("Connections");
            entity.Property(x => x.SourceSystem).HasMaxLength(IntegrationConnection.SourceSystemMaxLength).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(IntegrationConnection.DisplayNameMaxLength).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(IntegrationConnection.ErrorMaxLength);
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasIndex(x => new { x.OrganizationId, x.SourceSystem }).IsUnique();
        });

        model.Entity<ExternalRecordLink>(entity =>
        {
            entity.ToTable("RecordLinks");
            entity.Property(x => x.ExternalId).HasMaxLength(ExternalRecordLink.ExternalIdMaxLength).IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(ExternalRecordLink.ContentHashMaxLength);
            entity.Property(x => x.ReconciledHash).HasMaxLength(ExternalRecordLink.ContentHashMaxLength);
            entity.Property(x => x.LastError).HasMaxLength(ExternalRecordLink.ErrorMaxLength);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.SyncState).HasConversion<string>().HasMaxLength(32).IsRequired();
            // ContentHash vs ReconciledHash is the replay-safety mechanism, not duplication — the
            // header comment on ExternalRecordLink says why. NeedsReconciliation compares them in
            // memory and is not a column.
            entity.Ignore(x => x.NeedsReconciliation);
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.Kind, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.SyncState });
            // Reverse lookup: "is this PropFlow row already bound to an external record?" — the
            // ambiguous-match check in the reconciler (PF-S19.05).
            entity.HasIndex(x => new { x.OrganizationId, x.Kind, x.InternalId });
            // The retirement sweep asks for a connection's links that the current run did not touch.
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.LastRunId });
            entity.HasOne<IntegrationConnection>()
                .WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ConnectionId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // One mapping configuration per (connection, entity kind). Holds the facts the canonical
        // records cannot supply plus the value rules; Mode gates whether the reconciler writes.
        model.Entity<MappingProfile>(entity =>
        {
            entity.ToTable("MappingProfiles");
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.DefaultTimeZoneId).HasMaxLength(MappingProfile.TimeZoneMaxLength);
            entity.Property<uint>("Version").IsRowVersion();
            // Computed from Mode and from Validate(); neither is stored, so a rule change can never
            // leave a stale "valid" flag behind in the database.
            entity.Ignore(x => x.AppliesChanges);
            entity.Ignore(x => x.HasErrors);
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.Kind }).IsUnique();
            entity.HasOne<IntegrationConnection>()
                .WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ConnectionId })
                .OnDelete(DeleteBehavior.Cascade);
            // Validate() reads the rules, so the collection has to materialise with the profile.
            // It is exposed as IReadOnlyList over the private `rules` field, hence field access.
            entity.HasMany(x => x.Rules)
                .WithOne()
                .HasForeignKey(x => new { x.OrganizationId, x.ProfileId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        model.Entity<MappingRule>(entity =>
        {
            entity.ToTable("MappingRules");
            entity.Property(x => x.SourceField).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.SourceValue).HasMaxLength(MappingRule.SourceValueMaxLength).IsRequired();
            entity.Property(x => x.TargetValue).HasMaxLength(MappingRule.TargetValueMaxLength).IsRequired();
            // Deliberately NOT unique on (ProfileId, SourceField, SourceValue): rule matching is
            // case-insensitive in the domain, so a database unique index would be both the wrong
            // comparison and a 500 where MappingProfile.Validate() already reports "ambiguous" as
            // an error the operator can see and fix.
            entity.HasIndex(x => new { x.OrganizationId, x.ProfileId, x.SourceField });
        });

        // One row per sync attempt. The row version is load-bearing: SyncRun.Begin inserts this
        // Running row and saves it BEFORE the adapter is contacted, exactly as
        // OutboxProcessor.cs:38-48 claims an outbox message, and the loser of that race is
        // identified by the DbUpdateConcurrencyException the token produces.
        model.Entity<SyncRun>(entity =>
        {
            entity.ToTable("SyncRuns");
            entity.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.Error).HasMaxLength(SyncRun.ErrorMaxLength);
            entity.Property(x => x.SnapshotHash).HasMaxLength(SyncRun.SnapshotHashMaxLength);
            entity.Property<uint>("Version").IsRowVersion();
            // SyncCounts is a transport record over the five int columns, not an owned type.
            entity.Ignore(x => x.Counts);
            entity.Ignore(x => x.IsFinished);
            // Run history, newest first, per connection.
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.StartedAt })
                .IsDescending(false, false, true);
            // The stale-run reclaim sweep (PF-S19.06) scans Running rows by heartbeat.
            entity.HasIndex(x => new { x.OrganizationId, x.Status, x.HeartbeatAt });
            // THE CLAIM. Without this partial unique index two dispatchers each insert a Running
            // row, neither collides, and Begin's claim silently stops being a claim.
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId })
                .IsUnique()
                .HasFilter("\"Status\" = 'Running'")
                .HasDatabaseName("IX_SyncRuns_ActiveClaim");
            entity.HasOne<IntegrationConnection>()
                .WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ConnectionId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // The conflict queue. FirstSeenInRunId / LastSeenInRunId / ResolvedByUserId are deliberately
        // plain Guids with no foreign key: a conflict outlives the run that raised it (a reclaimed
        // run is still a row, but nothing should cascade queue history away), and the resolver is a
        // user in the identity context, which this store never joins to.
        model.Entity<Conflict>(entity =>
        {
            entity.ToTable("Conflicts");
            entity.Property(x => x.ExternalId).HasMaxLength(ExternalRecordLink.ExternalIdMaxLength).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Reason).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.Field).HasMaxLength(Conflict.FieldMaxLength).IsRequired();
            entity.Property(x => x.ObservedValue).HasMaxLength(Conflict.ValueMaxLength);
            entity.Property(x => x.CurrentValue).HasMaxLength(Conflict.ValueMaxLength);
            entity.Property(x => x.Detail).HasMaxLength(Conflict.DetailMaxLength);
            entity.Property(x => x.ResolutionNote).HasMaxLength(Conflict.NoteMaxLength);
            entity.Property<uint>("Version").IsRowVersion();
            entity.Ignore(x => x.IsOpen);
            entity.Ignore(x => x.Key);
            // THE INDEX THE STORY TURNS ON. A re-run that re-detects the same divergence must
            // update LastSeenInRunId on the existing open row; without this it can insert a second
            // one and the conflict queue grows once per run per divergence. The filter is on the
            // string 'Open' because Status is stored with HasConversion<string>(). Resolved and
            // Ignored rows fall outside the filter on purpose: a recurrence after resolution is a
            // new row, and the resolved one stays as audit history.
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.Kind, x.ExternalId, x.Reason, x.Field })
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'")
                .HasDatabaseName("IX_Conflicts_OpenDivergence");
            // The queue view: a connection's open conflicts, most recently seen first.
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.Status, x.LastSeenAt })
                .IsDescending(false, false, false, true);
            // "What did run X raise?"
            entity.HasIndex(x => new { x.OrganizationId, x.LastSeenInRunId });
            entity.HasOne<IntegrationConnection>()
                .WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ConnectionId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Same tenant convention as the other contexts: composite key, no store-generated Id,
        // query filter fixed to the current organization.
        foreach (var entity in model.Model.GetEntityTypes().Where(x => typeof(TenantEntity).IsAssignableFrom(x.ClrType)))
        {
            model.Entity(entity.ClrType).HasKey(nameof(TenantEntity.OrganizationId), nameof(TenantEntity.Id));
            model.Entity(entity.ClrType).Property(nameof(TenantEntity.Id)).ValueGeneratedNever();
            var parameter = Expression.Parameter(entity.ClrType, "entity");
            var body = Expression.Equal(Expression.Property(parameter, nameof(TenantEntity.OrganizationId)),
                Expression.Property(Expression.Constant(this), nameof(OrganizationId)));
            entity.SetQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardWrites()
    {
        var id = OrganizationId;
        if (id == Guid.Empty) throw new TenantAccessException();
        foreach (var entry in ChangeTracker.Entries<TenantEntity>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.OrganizationId != id ||
                (entry.State != EntityState.Added && entry.Property(x => x.OrganizationId).OriginalValue != id))
                throw new TenantAccessException();
        }
    }
}
