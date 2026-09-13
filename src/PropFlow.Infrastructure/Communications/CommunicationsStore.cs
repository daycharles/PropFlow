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
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<ChannelUnsubscribe> ChannelUnsubscribes => Set<ChannelUnsubscribe>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<DocumentPacket> DocumentPackets => Set<DocumentPacket>();
    public DbSet<SignatureRequest> SignatureRequests => Set<SignatureRequest>();
    public DbSet<ProviderCallbackReceipt> ProviderCallbackReceipts => Set<ProviderCallbackReceipt>();

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
            entity.Property(x => x.ProviderDeliveryStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.Status });
            // The work timeline reads a work item's messages newest first.
            entity.HasIndex(x => new { x.OrganizationId, x.WorkId, x.CreatedAt });
        });

        model.Entity<Conversation>(entity =>
        {
            entity.ToTable("Conversations"); entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ParticipantAddress).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.LastMessageAt });
        });
        model.Entity<ConversationMessage>(entity =>
        {
            entity.ToTable("ConversationMessages"); entity.Property(x => x.Body).HasMaxLength(10000).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(200); entity.Property(x => x.IntegrityHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasOne<Conversation>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ConversationId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.ConversationId, x.OccurredAt });
        });
        model.Entity<Campaign>(entity =>
        {
            entity.ToTable("Campaigns"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200); entity.Property(x => x.Body).HasMaxLength(10000).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        });
        model.Entity<ChannelUnsubscribe>(entity =>
        {
            entity.ToTable("ChannelUnsubscribes"); entity.Property(x => x.Address).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Channel, x.Address }).IsUnique();
        });
        model.Entity<ProviderCallbackReceipt>(entity =>
        { entity.ToTable("ProviderCallbackReceipts"); entity.Property(x => x.BodyDigest).HasMaxLength(64).IsRequired(); entity.HasIndex(x => new { x.OrganizationId, x.BodyDigest }).IsUnique(); });
        model.Entity<DocumentTemplate>(entity =>
        { entity.ToTable("DocumentTemplates"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.Property(x => x.Content).HasMaxLength(100000).IsRequired(); entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique(); });
        model.Entity<DocumentPacket>(entity =>
        { entity.ToTable("DocumentPackets"); entity.Property(x => x.Title).HasMaxLength(200).IsRequired(); entity.Property(x => x.RenderedContent).HasMaxLength(100000).IsRequired(); entity.Property(x => x.IntegrityHash).HasMaxLength(64).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); entity.HasOne<DocumentTemplate>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.TemplateId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.Status, x.ExpiresAt }); });
        model.Entity<SignatureRequest>(entity =>
        { entity.ToTable("SignatureRequests"); entity.Property(x => x.SignerId).HasMaxLength(200).IsRequired(); entity.Property(x => x.SignerName).HasMaxLength(200).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired(); entity.Property(x => x.EvidenceHash).HasMaxLength(128); entity.Property(x => x.FailureReason).HasMaxLength(1000); entity.HasOne<DocumentPacket>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PacketId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.PacketId, x.SignerId }).IsUnique(); });

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
