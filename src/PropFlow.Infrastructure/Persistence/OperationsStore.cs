using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain;
using PropFlow.Domain.Accounting;
using PropFlow.Domain.Assets;
using PropFlow.Domain.Automation;
using PropFlow.Domain.People;
using PropFlow.Domain.Properties;
using PropFlow.Domain.Marketing;
using PropFlow.Domain.Leasing;
using PropFlow.Domain.Timeline;
using PropFlow.Domain.Work;
using PropFlow.Domain.Communications;

namespace PropFlow.Infrastructure.Persistence;

public sealed class OperationsStore(DbContextOptions<OperationsStore> options, ITenantContext tenant) : DbContext(options)
{
    public Guid OrganizationId => tenant.OrganizationId;
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyContact> PropertyContacts => Set<PropertyContact>();
    public DbSet<PropertyDocument> PropertyDocuments => Set<PropertyDocument>();
    public DbSet<PropertyAmenity> PropertyAmenities => Set<PropertyAmenity>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<Occupancy> Occupancies => Set<Occupancy>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<WorkCategory> Categories => Set<WorkCategory>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<RepeatRepairPolicy> RepeatRepairPolicies => Set<RepeatRepairPolicy>();
    public DbSet<TimelineEntry> Timeline => Set<TimelineEntry>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<Showing> Showings => Set<Showing>();
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<LeaseNotice> LeaseNotices => Set<LeaseNotice>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
    public DbSet<LeaseParty> LeaseParties => Set<LeaseParty>();
    public DbSet<LeaseDocument> LeaseDocuments => Set<LeaseDocument>();
    public DbSet<ResidentPayment> ResidentPayments => Set<ResidentPayment>();
    public DbSet<LeaseCharge> LeaseCharges => Set<LeaseCharge>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

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
            entity.Property(x => x.InternalNotes).HasMaxLength(4000);
            entity.Property(x => x.ResidentVisibleNotes).HasMaxLength(4000);
            entity.Property(x => x.Cost).HasPrecision(18, 2);
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasOne<Vendor>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VendorId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.EmployeeId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Building>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BuildingId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Space>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SpaceId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkCategory>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CategoryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AssetId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            // Repeat-repair detection (PF-6.04) counts a property's work per asset.
            entity.HasIndex(x => new { x.OrganizationId, x.AssetId });
        });
        model.Entity<Vendor>(entity =>
        {
            entity.ToTable("Vendors");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.Property(x => x.Trade).HasMaxLength(100);
            entity.Property(x => x.Category).HasMaxLength(100);
        });
        model.Entity<Employee>(entity => { entity.ToTable("Employees"); entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); entity.Property(x => x.Email).HasMaxLength(254); entity.Property(x => x.Phone).HasMaxLength(40); });
        model.Entity<Portfolio>(entity => { entity.ToTable("Portfolios"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); });
        model.Entity<Property>(entity => { entity.ToTable("Properties"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired(); entity.HasOne<Portfolio>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PortfolioId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<PropertyContact>(entity => { entity.ToTable("PropertyContacts"); entity.Property(x => x.FullName).HasMaxLength(200).IsRequired(); entity.Property(x => x.Role).HasMaxLength(100).IsRequired(); entity.Property(x => x.Email).HasMaxLength(254); entity.Property(x => x.Phone).HasMaxLength(40); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.PropertyId }); });
        model.Entity<PropertyDocument>(entity => { entity.ToTable("PropertyDocuments"); entity.Property(x => x.Title).HasMaxLength(200).IsRequired(); entity.Property(x => x.DocumentUrl).HasMaxLength(1000).IsRequired(); entity.Property(x => x.DocumentType).HasMaxLength(100); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.PropertyId, x.CreatedAt }); });
        model.Entity<PropertyAmenity>(entity => { entity.ToTable("PropertyAmenities"); entity.Property(x => x.Name).HasMaxLength(120).IsRequired(); entity.Property(x => x.Details).HasMaxLength(500); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.PropertyId, x.Name }).IsUnique(); });
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
        model.Entity<SavedView>(entity =>
        {
            entity.ToTable("SavedViews");
            entity.Property(x => x.UserId).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Filters).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Columns).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.UserId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.UserId, x.IsDefault });
        });
        model.Entity<AutomationRule>(entity =>
        {
            entity.ToTable("AutomationRules");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(x => x.Conditions).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Actions).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.IsEnabled, x.Trigger });
        });
        model.Entity<RepeatRepairPolicy>(entity =>
        {
            entity.ToTable("RepeatRepairPolicies");
            // One policy per organization — the upsert in EfRepeatRepairDetector relies on it.
            entity.HasIndex(x => x.OrganizationId).IsUnique();
        });
        model.Entity<TimelineEntry>(entity =>
        {
            entity.ToTable("Timeline");
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Changes).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.RelatedObjectType).HasMaxLength(100);
            entity.Property(x => x.OldValue).HasMaxLength(4000);
            entity.Property(x => x.NewValue).HasMaxLength(4000);
            entity.Property(x => x.ResidentVisible).HasDefaultValue(false);
            entity.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WorkId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.WorkId, x.OccurredAt });
        });
        model.Entity<Attachment>(entity =>
        {
            entity.ToTable("Attachments");
            entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
            entity.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WorkId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.WorkId, x.CreatedAt });
            entity.HasIndex(x => new { x.OrganizationId, x.RetainUntil });
        });
        model.Entity<Listing>(entity =>
        {
            entity.ToTable("Listings");
            entity.Property(x => x.Headline).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.MonthlyRent).HasPrecision(12, 2);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Space>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SpaceId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.Status, x.AvailableOn });
        });
        model.Entity<Inquiry>(entity =>
        {
            entity.ToTable("Inquiries"); entity.Property(x => x.ProspectName).HasMaxLength(200).IsRequired(); entity.Property(x => x.Email).HasMaxLength(254).IsRequired(); entity.Property(x => x.Phone).HasMaxLength(40); entity.Property(x => x.Message).HasMaxLength(2000); entity.Property(x => x.LeadSource).HasMaxLength(100); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Listing>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ListingId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.ListingId, x.Email, x.Status });
        });
        model.Entity<Showing>(entity =>
        {
            entity.ToTable("Showings"); entity.Property(x => x.ProspectName).HasMaxLength(200).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Listing>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ListingId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.ListingId, x.ScheduledAt });
        });
        model.Entity<Applicant>(entity => { entity.ToTable("Applicants"); entity.Property(x => x.ProspectName).HasMaxLength(200).IsRequired(); entity.Property(x => x.Email).HasMaxLength(254).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.HasOne<Listing>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ListingId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasOne<Inquiry>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.InquiryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.ListingId, x.Status }); entity.HasIndex(x => new { x.OrganizationId, x.InquiryId }).IsUnique(); });
        model.Entity<Lease>(entity =>
        {
            entity.ToTable("Leases"); entity.Property(x => x.MonthlyRent).HasPrecision(12, 2).IsRequired(); entity.Property(x => x.SecurityDeposit).HasPrecision(12, 2); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Resident>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResidentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Space>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SpaceId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.ResidentId, x.Status });
            entity.HasIndex(x => new { x.OrganizationId, x.SpaceId, x.Status });
        });
        model.Entity<LeaseNotice>(entity =>
        {
            entity.ToTable("LeaseNotices"); entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.Status, x.DueOn });
        });
        model.Entity<Announcement>(entity =>
        {
            entity.ToTable("Announcements");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Status, x.ExpiresAt });
        });
        model.Entity<HouseholdMember>(entity =>
        {
            entity.ToTable("HouseholdMembers");
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Relationship).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.HasOne<Resident>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResidentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.ResidentId });
        });
        model.Entity<LeaseParty>(entity =>
        {
            entity.ToTable("LeaseParties");
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.LeaseId });
        });
        model.Entity<LeaseDocument>(entity =>
        {
            entity.ToTable("LeaseDocuments");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DocumentUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.SignedBy).HasMaxLength(200);
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.LeaseId, x.Status });
        });
        model.Entity<ResidentPayment>(entity =>
        {
            entity.ToTable("ResidentPayments");
            entity.Property(x => x.Amount).HasPrecision(12, 2).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(200);
            entity.HasOne<LeaseCharge>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ChargeId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Resident>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResidentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.ResidentId, x.Status, x.DueOn });
        });
        model.Entity<LeaseCharge>(entity => { entity.ToTable("LeaseCharges"); entity.Property(x => x.Description).HasMaxLength(200).IsRequired(); entity.Property(x => x.Amount).HasPrecision(12, 2).IsRequired(); entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.LeaseId, x.Status, x.DueOn }); });

        model.Entity<ChartOfAccount>(entity =>
        {
            entity.ToTable("ChartOfAccounts");
            entity.Property(x => x.Code).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
        });
        model.Entity<FiscalPeriod>(entity =>
        {
            entity.ToTable("FiscalPeriods");
            entity.Property(x => x.Name).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.StartsOn, x.EndsOn });
        });
        model.Entity<JournalEntry>(entity =>
        {
            entity.ToTable("JournalEntries");
            entity.Property(x => x.Reference).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Memo).HasMaxLength(1000);
            // (18,2) matches WorkItem.Cost — the ledger carries balances, not a single rent figure.
            entity.Property(x => x.Total).HasPrecision(18, 2).IsRequired();
            entity.HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PeriodId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ReversalOfId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.OrganizationId, x.EntryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            // The entry is immutable, so "already reversed" cannot be a flag on the original.
            // PostgreSQL keeps NULLs distinct in a unique index, so ordinary entries are
            // unconstrained while any one entry can be reversed at most once.
            entity.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(x => new { x.OrganizationId, x.ReversalOfId }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.PeriodId, x.EntryDate });
        });
        model.Entity<JournalLine>(entity =>
        {
            entity.ToTable("JournalLines");
            entity.Property(x => x.Debit).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Credit).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Memo).HasMaxLength(500);
            entity.HasOne<ChartOfAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.AccountId });
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
            // FS-S09: a posted journal is corrected by a reversing entry, never by an edit. This
            // is the EF rung of the same three-level control the Timeline uses — the runtime role
            // gets only GRANT SELECT, INSERT and a PostgreSQL trigger rejects UPDATE/DELETE.
            if (entry.Entity is JournalEntry or JournalLine && entry.State != EntityState.Added)
                throw new InvalidOperationException("Journal entries are append-only; post a reversing entry instead.");
        }
    }
}
