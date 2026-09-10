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
            entity.Property(x => x.LastError).HasMaxLength(ExternalRecordLink.ErrorMaxLength);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.SyncState).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.Kind, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.ConnectionId, x.SyncState });
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
