using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain;
using PropFlow.Domain.Communications;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Infrastructure.Communications;

// Tenant-scoped store for the Communications module. Its own schema and migration history keep
// it independent of the Operations context. Enqueuing an outbox row atomically with an
// Operations change (a shared transaction) is wired when work events start producing messages.
public sealed class CommunicationsStore(DbContextOptions<CommunicationsStore> options, ITenantContext tenant) : DbContext(options)
{
    public Guid OrganizationId => tenant.OrganizationId;
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new TenantConnectionInterceptor(tenant));

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("communications");

        model.Entity<MessageTemplate>(entity =>
        {
            entity.ToTable("MessageTemplates");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200);
            entity.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        });

        model.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.Property(x => x.RecipientAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200);
            entity.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.ProviderReference).HasMaxLength(200);
            entity.Property(x => x.FailureReason).HasMaxLength(1000);
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Status });
        });

        // Same tenant convention as the Operations context: composite key, no store-generated
        // Id, and a query filter fixed to the current organization.
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
