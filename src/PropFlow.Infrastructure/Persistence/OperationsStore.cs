using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain;
using PropFlow.Domain.Assets;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;

namespace PropFlow.Infrastructure.Persistence;

public sealed class OperationsStore(DbContextOptions<OperationsStore> options, ITenantContext tenant) : DbContext(options)
{
    public Guid OrganizationId => tenant.OrganizationId;
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<Occupancy> Occupancies => Set<Occupancy>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<WorkCategory> Categories => Set<WorkCategory>();
    public DbSet<TimelineEntry> Timeline => Set<TimelineEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new TenantConnectionInterceptor(tenant));

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("operations");
        model.Entity<WorkItem>(entity =>
        {
            entity.ToTable("WorkItems");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasOne<Vendor>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VendorId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.EmployeeId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Building>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BuildingId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Space>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SpaceId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkCategory>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CategoryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Vendor>(entity =>
        {
            entity.ToTable("Vendors");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.Phone).HasMaxLength(40);
        });
        model.Entity<Employee>(entity => { entity.ToTable("Employees"); entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); entity.Property(x => x.Email).HasMaxLength(254); entity.Property(x => x.Phone).HasMaxLength(40); });
        model.Entity<Portfolio>(entity => { entity.ToTable("Portfolios"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); });
        model.Entity<Property>(entity => { entity.ToTable("Properties"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired(); entity.HasOne<Portfolio>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PortfolioId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<Building>(entity => { entity.ToTable("Buildings"); entity.Property(x => x.Name).HasMaxLength(100).IsRequired(); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<Space>(entity => { entity.ToTable("Spaces"); entity.Property(x => x.Code).HasMaxLength(50).IsRequired(); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasOne<Building>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BuildingId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<Resident>(entity =>
        {
            entity.ToTable("Residents");
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.Property(x => x.SmsConsent).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.EmailConsent).HasConversion<string>().HasMaxLength(20).IsRequired();
        });
        model.Entity<Occupancy>(entity =>
        {
            entity.ToTable("Occupancies");
            entity.HasOne<Resident>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResidentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Space>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SpaceId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.SpaceId });
            entity.HasIndex(x => new { x.OrganizationId, x.ResidentId });
        });
        model.Entity<Asset>(entity =>
        {
            entity.ToTable("Assets");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Manufacturer).HasMaxLength(200);
            entity.Property(x => x.Model).HasMaxLength(200);
            entity.Property(x => x.SerialNumber).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Condition).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.ReplacementCostEstimate).HasPrecision(12, 2);
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Space>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SpaceId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.PropertyId });
        });
        model.Entity<WorkCategory>(entity => { entity.ToTable("WorkCategories"); entity.Property(x => x.Name).HasMaxLength(100).IsRequired(); entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique(); });
        model.Entity<TimelineEntry>(entity =>
        {
            entity.ToTable("Timeline");
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Changes).HasColumnType("jsonb").IsRequired();
            entity.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WorkId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.WorkId, x.OccurredAt });
        });

        // One convention for every business entity, including future modules.
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
            if (entry.Entity is TimelineEntry && entry.State != EntityState.Added)
                throw new InvalidOperationException("Timeline entries are append-only.");
        }
    }
}
