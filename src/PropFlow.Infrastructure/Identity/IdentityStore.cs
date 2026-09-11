using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PropFlow.Infrastructure.Identity;

// This store is intentionally separate from tenant-scoped business data. Only identity/provisioning
// services may query it. Organization membership queries always bind both user and organization.
public sealed class IdentityStore(DbContextOptions<IdentityStore> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> Memberships => Set<OrganizationMembership>();
    public DbSet<MembershipPropertyBinding> MembershipPropertyBindings => Set<MembershipPropertyBinding>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<RoleCapabilityOverride> RoleCapabilityOverrides => Set<RoleCapabilityOverride>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.HasDefaultSchema("identity");
        model.Entity<Organization>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(63);
            entity.HasIndex(x => x.Slug).IsUnique().HasFilter("\"Slug\" IS NOT NULL");
        });
        model.Entity<OrganizationMembership>(entity =>
        {
            entity.HasKey(x => new { x.OrganizationId, x.UserId });
            entity.Property(x => x.Role).HasMaxLength(80).IsRequired();
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MembershipPropertyBinding>(entity =>
        {
            entity.HasKey(x => new { x.OrganizationId, x.UserId, x.PropertyId });
            entity.HasOne<OrganizationMembership>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Invitation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Token).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Token).IsUnique();
            // A re-invite of the same still-pending address replaces rather than duplicates:
            // enforced by the service layer (PF-S01.03) reading this index before insert, not by
            // a partial-unique constraint here, since "pending" depends on both AcceptedAt and
            // ExpiresAt and Npgsql's HasFilter takes a literal predicate, not an expression.
            entity.HasIndex(x => new { x.OrganizationId, x.Email });
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AcceptedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RoleCapabilityOverride>(entity =>
        {
            entity.HasKey(x => new { x.OrganizationId, x.Role, x.Capability });
            entity.Property(x => x.Role).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Capability).HasMaxLength(120).IsRequired();
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Team>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<TeamMembership>(entity =>
        {
            entity.HasKey(x => new { x.TeamId, x.UserId });
            entity.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<OrganizationMembership>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<UserSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.HasIndex(x => new { x.UserId, x.OrganizationId });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
