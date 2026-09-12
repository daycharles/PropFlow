using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PropFlow.Application;
using PropFlow.Domain;
using PropFlow.Domain.Accounting;
using PropFlow.Domain.Approvals;
using PropFlow.Domain.Assets;
using PropFlow.Domain.Automation;
using PropFlow.Domain.Configuration;
using PropFlow.Domain.Billing;
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
    public DbSet<PreventiveMaintenancePlan> PreventiveMaintenancePlans => Set<PreventiveMaintenancePlan>();
    public DbSet<PreventiveMaintenanceOccurrence> PreventiveMaintenanceOccurrences => Set<PreventiveMaintenanceOccurrence>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<AssetLifecycleCost> AssetLifecycleCosts => Set<AssetLifecycleCost>();
    public DbSet<ComplianceObligation> ComplianceObligations => Set<ComplianceObligation>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Violation> Violations => Set<Violation>();
    public DbSet<Remediation> Remediations => Set<Remediation>();
    public DbSet<ComplianceEvidence> ComplianceEvidence => Set<ComplianceEvidence>();
    public DbSet<ComplianceOccurrence> ComplianceOccurrences => Set<ComplianceOccurrence>();
    public DbSet<WorkCategory> Categories => Set<WorkCategory>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<NumberingSequence> NumberingSequences => Set<NumberingSequence>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
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
    public DbSet<RecurringCharge> RecurringCharges => Set<RecurringCharge>();
    public DbSet<Credit> Credits => Set<Credit>();
    public DbSet<LateFeeRule> LateFeeRules => Set<LateFeeRule>();
    public DbSet<PaymentRefund> PaymentRefunds => Set<PaymentRefund>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();
    public DbSet<PaymentReconciliation> PaymentReconciliations => Set<PaymentReconciliation>();
    public DbSet<DelinquencyCase> DelinquencyCases => Set<DelinquencyCase>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<PayableInvoice> PayableInvoices => Set<PayableInvoice>();
    public DbSet<ReceivableInvoice> ReceivableInvoices => Set<ReceivableInvoice>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<PropertyOwnership> PropertyOwnerships => Set<PropertyOwnership>();
    public DbSet<ManagementFeeRule> ManagementFeeRules => Set<ManagementFeeRule>();
    public DbSet<Distribution> Distributions => Set<Distribution>();
    public DbSet<OwnerStatement> OwnerStatements => Set<OwnerStatement>();

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
            entity.Property(x => x.DisplayNumber).HasMaxLength(50);
            entity.Property<uint>("Version").IsRowVersion();
            // Unique when set (a partial index - most work predates numbering and has none),
            // so two organizations' work never collides and neither can one organization's.
            entity.HasIndex(x => new { x.OrganizationId, x.DisplayNumber })
                .IsUnique().HasFilter("\"DisplayNumber\" IS NOT NULL");
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
        model.Entity<PreventiveMaintenancePlan>(entity =>
        {
            entity.ToTable("PreventiveMaintenancePlans");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Recurrence).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Asset>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AssetId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.AssetId, x.IsActive });
        });
        model.Entity<PreventiveMaintenanceOccurrence>(entity =>
        {
            entity.ToTable("PreventiveMaintenanceOccurrences");
            entity.Property(x => x.OccurrenceKey).HasMaxLength(120).IsRequired();
            entity.Property(x => x.GeneratedAt).IsRequired();
            entity.HasOne<PreventiveMaintenancePlan>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.PlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Asset>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.AssetId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WorkItem>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.WorkItemId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.OccurrenceKey }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.AssetId, x.DueOn });
        });
        model.Entity<MeterReading>(entity =>
        {
            entity.ToTable("MeterReadings");
            entity.Property(x => x.MeterName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Unit).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Reading).HasPrecision(18, 3).IsRequired();
            entity.HasOne<Asset>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AssetId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.AssetId, x.MeterName, x.ReadOn });
        });
        model.Entity<AssetLifecycleCost>(entity =>
        {
            entity.ToTable("AssetLifecycleCosts");
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasOne<Asset>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AssetId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WorkItemId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => new { x.OrganizationId, x.AssetId, x.IncurredOn });
        });
        model.Entity<ComplianceObligation>(entity =>
        {
            entity.ToTable("ComplianceObligations");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Recurrence).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.PropertyId, x.Status, x.DueOn });
        });
        model.Entity<Incident>(entity =>
        {
            entity.ToTable("Incidents");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.OwnsMany(x => x.Audit, audit =>
            {
                audit.ToTable("IncidentAudit");
                audit.Property<Guid>("OrganizationId");
                audit.Property<Guid>("IncidentId");
                audit.Property(x => x.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
                audit.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
                audit.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
                audit.Property(x => x.Note).HasMaxLength(2000);
                audit.HasKey("OrganizationId", "IncidentId", nameof(IncidentAuditEntry.OccurredAt), nameof(IncidentAuditEntry.Action));
            });
            entity.HasIndex(x => new { x.OrganizationId, x.PropertyId, x.Status, x.OccurredAt });
        });
        model.Entity<Violation>(entity =>
        {
            entity.ToTable("Violations");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.PropertyId, x.Status });
        });
        model.Entity<Remediation>(entity =>
        {
            entity.ToTable("Remediations");
            entity.Property(x => x.Action).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.PropertyId, x.Status, x.DueOn });
        });
        model.Entity<WorkCategory>(entity =>
        {
            entity.ToTable("WorkCategories");
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AppliesTo).HasConversion<string>().HasMaxLength(20).IsRequired().HasDefaultValue(ConfigurationEntityType.WorkItem);
            entity.HasIndex(x => new { x.OrganizationId, x.AppliesTo, x.Name }).IsUnique();
        });
        model.Entity<CustomFieldDefinition>(entity =>
        {
            entity.ToTable("CustomFieldDefinitions");
            entity.Property(x => x.Key).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AppliesTo).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.FieldType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Options).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.AppliesTo, x.Key }).IsUnique();
        });
        model.Entity<CustomFieldValue>(entity =>
        {
            entity.ToTable("CustomFieldValues");
            entity.Property(x => x.Value).HasMaxLength(2000).IsRequired();
            entity.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WorkId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CustomFieldDefinition>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CustomFieldDefinitionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.WorkId, x.CustomFieldDefinitionId }).IsUnique();
        });
        model.Entity<NumberingSequence>(entity =>
        {
            entity.ToTable("NumberingSequences");
            entity.Property(x => x.AppliesTo).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.AppliesTo }).IsUnique();
        });
        model.Entity<ApprovalRequest>(entity =>
        {
            entity.ToTable("ApprovalRequests");
            entity.Property(x => x.SubjectType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.DecisionReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.OrganizationId, x.SubjectType, x.SubjectId });
            entity.HasIndex(x => new { x.OrganizationId, x.Status });
        });
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
            // FS-S08 widens money to (18, 2) so it does not change precision crossing the
            // charge -> ledger boundary. Widening is safe; narrowing is not.
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.RefundedAmount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(200);
            entity.Property(x => x.ProviderReference).HasMaxLength(200);
            entity.Property(x => x.FailureReason).HasMaxLength(500);
            entity.HasOne<LeaseCharge>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ChargeId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Resident>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResidentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.ResidentId, x.Status, x.DueOn });
            // The idempotency key for provider callbacks. PostgreSQL treats NULLs as distinct,
            // so payments that never reached a provider are unconstrained.
            entity.HasIndex(x => new { x.OrganizationId, x.ProviderReference }).IsUnique();
        });
        model.Entity<LeaseCharge>(entity =>
        {
            entity.ToTable("LeaseCharges");
            entity.Property(x => x.Description).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.AmountApplied).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RecurringCharge>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.RecurringChargeId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.LeaseId, x.Status, x.DueOn });
            // One generated charge per schedule per due date — this is what makes a repeated
            // generation run a no-op at the database, not just in the application.
            entity.HasIndex(x => new { x.OrganizationId, x.RecurringChargeId, x.DueOn }).IsUnique();
        });
        model.Entity<RecurringCharge>(entity =>
        {
            entity.ToTable("RecurringCharges");
            entity.Property(x => x.Description).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.LeaseId, x.Status });
        });
        model.Entity<Credit>(entity =>
        {
            entity.ToTable("Credits");
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.AppliedAmount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.LeaseId, x.Status });
        });
        model.Entity<LateFeeRule>(entity =>
        {
            entity.ToTable("LateFeeRules");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FlatAmount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.PercentOfOutstanding).HasPrecision(5, 2).IsRequired();
            entity.Property(x => x.MaximumAmount).HasPrecision(18, 2);
            entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            // One rule per scope: at most one organization-wide default (PropertyId NULL is
            // distinct in PostgreSQL, so the default is guarded in the endpoint instead) and
            // at most one per property.
            entity.HasIndex(x => new { x.OrganizationId, x.PropertyId }).IsUnique();
        });
        model.Entity<PaymentRefund>(entity =>
        {
            entity.ToTable("PaymentRefunds");
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.ProviderReference).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasOne<ResidentPayment>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PaymentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.PaymentId });
            // Same idempotency contract as a payment: a replayed refund callback finds this row.
            entity.HasIndex(x => new { x.OrganizationId, x.ProviderReference }).IsUnique();
        });
        model.Entity<PaymentMethod>(entity =>
        {
            entity.ToTable("PaymentMethods"); entity.Property(x => x.Label).HasMaxLength(100).IsRequired(); entity.Property(x => x.ProviderToken).HasMaxLength(200); entity.Property(x => x.LastFour).HasMaxLength(4); entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Resident>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResidentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.ResidentId, x.Status });
        });
        model.Entity<PaymentReceipt>(entity =>
        {
            entity.ToTable("PaymentReceipts"); entity.Property(x => x.ReceiptNumber).HasMaxLength(100).IsRequired();
            entity.HasOne<ResidentPayment>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PaymentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.PaymentId }).IsUnique(); entity.HasIndex(x => new { x.OrganizationId, x.ReceiptNumber }).IsUnique();
        });
        model.Entity<PaymentReconciliation>(entity =>
        {
            entity.ToTable("PaymentReconciliations"); entity.Property(x => x.ProviderReference).HasMaxLength(200).IsRequired(); entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.Note).HasMaxLength(500); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<ResidentPayment>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PaymentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.ProviderReference }).IsUnique();
        });
        model.Entity<DelinquencyCase>(entity =>
        {
            entity.ToTable("DelinquencyCases"); entity.Property(x => x.Balance).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.LeaseId, x.Status });
        });

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
            entity.Property(x => x.PropertyId);
            entity.Property(x => x.Debit).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Credit).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Memo).HasMaxLength(500);
            entity.HasOne<ChartOfAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.AccountId });
        });
        model.Entity<Budget>(entity => { entity.ToTable("Budgets"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired(); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.PropertyId, x.Year }).IsUnique(); });
        model.Entity<BudgetLine>(entity => { entity.ToTable("BudgetLines"); entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired(); entity.HasOne<Budget>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BudgetId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasOne<ChartOfAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.BudgetId, x.AccountId, x.Month }).IsUnique(); });
        model.Entity<Owner>(entity => { entity.ToTable("Owners"); entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); entity.Property(x => x.Email).HasMaxLength(254).IsRequired(); entity.HasIndex(x => new { x.OrganizationId, x.Email }).IsUnique(); });
        model.Entity<PropertyOwnership>(entity => { entity.ToTable("PropertyOwnerships"); entity.Property(x => x.Percentage).HasPrecision(5, 2).IsRequired(); entity.HasOne<Owner>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.OwnerId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.OwnerId, x.PropertyId }).IsUnique(); });
        model.Entity<ManagementFeeRule>(entity => { entity.ToTable("ManagementFeeRules"); entity.Property(x => x.Percentage).HasPrecision(5, 2).IsRequired(); entity.Property(x => x.Minimum).HasPrecision(18, 2).IsRequired(); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.OrganizationId, x.PropertyId }).IsUnique(); });
        model.Entity<Distribution>(entity => { entity.ToTable("Distributions"); entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired(); entity.HasOne<Owner>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.OwnerId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<OwnerStatement>(entity => { entity.ToTable("OwnerStatements"); entity.Property(x => x.Income).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.Expenses).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.ManagementFee).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.Distributions).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.SourceHash).HasMaxLength(64).IsRequired(); entity.HasOne<Owner>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.OwnerId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasOne<Property>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.OwnerId, x.PropertyId, x.StartsOn, x.EndsOn }).IsUnique(); });
        model.Entity<PayableInvoice>(entity => { entity.ToTable("PayableInvoices"); entity.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired(); entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.AmountPaid).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.HasOne<Vendor>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VendorId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.VendorId, x.Status }); entity.HasIndex(x => new { x.OrganizationId, x.InvoiceNumber }).IsUnique(); });
        model.Entity<ReceivableInvoice>(entity => { entity.ToTable("ReceivableInvoices"); entity.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired(); entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.AmountPaid).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.HasOne<Resident>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResidentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.ResidentId, x.Status }); entity.HasIndex(x => new { x.OrganizationId, x.InvoiceNumber }).IsUnique(); });
        model.Entity<BankAccount>(entity => { entity.ToTable("BankAccounts"); entity.Property(x => x.Name).HasMaxLength(100).IsRequired(); entity.Property(x => x.Institution).HasMaxLength(100).IsRequired(); entity.Property(x => x.LastFour).HasMaxLength(4).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.HasOne<ChartOfAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AssetAccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique(); });
        model.Entity<BankTransaction>(entity => { entity.ToTable("BankTransactions"); entity.Property(x => x.ExternalId).HasMaxLength(200).IsRequired(); entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired(); entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired(); entity.HasOne<BankAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BankAccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade); entity.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.JournalEntryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict); entity.HasIndex(x => new { x.OrganizationId, x.BankAccountId, x.ExternalId }).IsUnique(); });

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
