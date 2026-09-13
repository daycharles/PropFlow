using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Application;
using PropFlow.Infrastructure.Communications;
using PropFlow.Infrastructure.Identity;
using PropFlow.Infrastructure.Integrations;

namespace PropFlow.Infrastructure.Persistence;

// Explicit administrator operations. Never registered in the HTTP host.
public static class DatabaseProvisioner
{
    public static async Task MigrateAsync(string adminConnection)
    {
        await using var identity = CreateIdentityStore(adminConnection);
        await identity.Database.MigrateAsync();
        await using var operations = CreateOperationsStore(adminConnection, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await operations.Database.MigrateAsync();
        await using var communications = CreateCommunicationsStore(adminConnection, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await communications.Database.MigrateAsync();
        await using var integrations = CreateIntegrationStore(adminConnection, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await integrations.Database.MigrateAsync();
    }

    public static IdentityStore CreateIdentityStore(string connection) => new(new DbContextOptionsBuilder<IdentityStore>()
        .UseNpgsql(connection, options => options.MigrationsHistoryTable("__IdentityMigrations", "identity")).Options);

    public static OperationsStore CreateOperationsStore(string connection, Guid organizationId) => new(
        new DbContextOptionsBuilder<OperationsStore>().UseNpgsql(connection,
            options => options.MigrationsHistoryTable("__OperationsMigrations", "operations")).Options,
        new FixedTenantContext(organizationId));

    public static CommunicationsStore CreateCommunicationsStore(string connection, Guid organizationId) => new(
        new DbContextOptionsBuilder<CommunicationsStore>().UseNpgsql(connection,
            options => options.MigrationsHistoryTable("__CommunicationsMigrations", "communications")).Options,
        new FixedTenantContext(organizationId));

    public static IntegrationStore CreateIntegrationStore(string connection, Guid organizationId) => new(
        new DbContextOptionsBuilder<IntegrationStore>().UseNpgsql(connection,
            options => options.MigrationsHistoryTable("__IntegrationsMigrations", "integrations")).Options,
        new FixedTenantContext(organizationId));

    public static async Task ConfigureRuntimeAsync(string adminConnection, string password)
    {
        if (password.Length < 20) throw new ArgumentException("Runtime password must contain at least 20 characters.");
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'propflow_app')";
        var exists = (bool)(await command.ExecuteScalarAsync())!;
        command.CommandText = exists
            ? "SELECT format('ALTER ROLE propflow_app WITH LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION PASSWORD %L', @password)"
            : "SELECT format('CREATE ROLE propflow_app WITH LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION PASSWORD %L', @password)";
        command.Parameters.AddWithValue("password", password);
        var sql = (string)(await command.ExecuteScalarAsync())!;
        command.Parameters.Clear();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
        command.CommandText = """
            REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA identity, operations, communications, integrations TO propflow_app;
            GRANT SELECT ON ALL TABLES IN SCHEMA identity TO propflow_app;
            GRANT UPDATE ON identity."AspNetUsers" TO propflow_app;
            -- Account creation previously only happened out of band (seeding/admin tooling on the
            -- admin connection). InvitationService.AcceptAsync (PF-S01.03) now calls
            -- UserManager.CreateAsync at runtime for a brand-new invitee.
            GRANT INSERT ON identity."AspNetUsers" TO propflow_app;
            -- Previously read-only at runtime (MembershipAccess only reads); InvitationService's
            -- acceptance flow and MembershipManagementService (PF-S01.03/.07) now write here too.
            GRANT INSERT, UPDATE ON identity."Memberships" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON identity."Invitations" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON identity."RoleCapabilityOverrides" TO propflow_app;
            GRANT SELECT, INSERT ON identity."Teams" TO propflow_app;
            GRANT SELECT, INSERT, DELETE ON identity."TeamMemberships" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON identity."UserSessions" TO propflow_app;
            -- Append-only, matching operations."Timeline" below: no UPDATE/DELETE grant, so the
            -- audit trail cannot be altered or erased by the application role itself.
            GRANT SELECT, INSERT ON identity."AuditEntries" TO propflow_app;
            GRANT SELECT ON operations."Vendors" TO propflow_app;
            GRANT SELECT ON operations."Employees" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."Buildings", operations."Spaces" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."Portfolios", operations."Properties" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."Listings", operations."Inquiries", operations."Showings" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."Leases", operations."LeaseNotices" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."Announcements" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."LeaseParties" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."LeaseDocuments" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."ResidentPayments" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."LeaseCharges" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."RecurringCharges" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."Credits" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."LateFeeRules" TO propflow_app;
            -- Append-only, matching operations."Timeline": a refund is a fact about money that
            -- left, so the runtime role gets no UPDATE or DELETE on the refund trail.
            GRANT SELECT, INSERT ON operations."PaymentRefunds" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."PaymentMethods" TO propflow_app;
            GRANT SELECT, INSERT ON operations."PaymentReceipts" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."PaymentReconciliations" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."DelinquencyCases" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."PropertyContacts" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."PropertyDocuments" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."PropertyAmenities" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."Applicants" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."HouseholdMembers" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."WorkCategories", operations."Residents", operations."Occupancies", operations."Assets" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."PreventiveMaintenancePlans", operations."MeterReadings", operations."AssetLifecycleCosts" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."PreventiveMaintenanceOccurrences" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."ComplianceObligations", operations."Incidents", operations."Violations", operations."Remediations" TO propflow_app;
            GRANT SELECT, INSERT ON operations."IncidentAudit" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."ComplianceEvidence", operations."ComplianceOccurrences" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."SavedViews" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."AutomationRules" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."RepeatRepairPolicies" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."WorkItems" TO propflow_app;
            GRANT SELECT, INSERT ON operations."Timeline" TO propflow_app;
            -- FS-S09 ledger. Accounts and periods are editable state (rename, activate/deactivate,
            -- close), so they carry UPDATE. Journals do not: a posted entry is corrected by a
            -- reversing entry, so the runtime role gets no UPDATE and no DELETE on either journal
            -- table — the same control as operations."Timeline" above.
            GRANT SELECT, INSERT, UPDATE ON operations."ChartOfAccounts" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."FiscalPeriods" TO propflow_app;
            GRANT SELECT, INSERT ON operations."JournalEntries" TO propflow_app;
            GRANT SELECT, INSERT ON operations."JournalLines" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."PayableInvoices", operations."ReceivableInvoices" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."BankAccounts" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."BankTransactions" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."Budgets", operations."BudgetLines", operations."Owners", operations."PropertyOwnerships", operations."ManagementFeeRules", operations."Distributions" TO propflow_app;
            GRANT SELECT, INSERT ON operations."OwnerStatements" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."ReportSchedules" TO propflow_app;
            GRANT SELECT, INSERT ON operations."ReportDeliveries" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."Attachments" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."CustomFieldDefinitions" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."CustomFieldValues" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."NumberingSequences" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."ApprovalRequests" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."OrganizationSettings" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."NotificationPreferences" TO propflow_app;
            -- FS-S05 applications and screening. The application itself and its screening
            -- requests are working state (status transitions, retry counters), so they carry
            -- UPDATE; the applicant rows are editable and removable before submission.
            GRANT SELECT, INSERT, UPDATE ON operations."RentalApplications" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."ApplicationApplicants" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."ScreeningRequests" TO propflow_app;
            -- The append-only three, deliberately on their own line rather than appended to a
            -- multi-table GRANT above: consent, screening verdicts and decisions are adverse-
            -- action evidence, and on a shared line one careless verb would silently unlock the
            -- whole trail with nothing failing. A PostgreSQL trigger backs this up because a
            -- GRANT can be widened by accident and a trigger cannot.
            GRANT SELECT, INSERT ON operations."ApplicationConsents" TO propflow_app;
            GRANT SELECT, INSERT ON operations."ScreeningResults" TO propflow_app;
            GRANT SELECT, INSERT ON operations."ApplicationDecisions" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON communications."MessageTemplates" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON communications."OutboxMessages" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON communications."Conversations" TO propflow_app;
            GRANT SELECT, INSERT ON communications."ConversationMessages" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON communications."Campaigns" TO propflow_app;
            GRANT SELECT, INSERT ON communications."ChannelUnsubscribes" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON communications."DocumentTemplates" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON communications."DocumentPackets" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON communications."SignatureRequests" TO propflow_app;
            GRANT SELECT, INSERT ON communications."ProviderCallbackReceipts" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON integrations."Connections" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON integrations."RecordLinks" TO propflow_app;
            -- FS-S19 reconciliation. Mapping configuration is ordinary editable state: a profile is
            -- created, retargeted and promoted, and a rule that was a mistake is deleted outright
            -- rather than tombstoned.
            GRANT SELECT, INSERT, UPDATE, DELETE ON integrations."MappingProfiles" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON integrations."MappingRules" TO propflow_app;
            -- Append-and-amend, the same rung as operations."PaymentRefunds" above: a sync run is a
            -- history row saying what an automated process did to a tenant's data, so the runtime
            -- role can claim it, finish it and reclaim it, but never erase it. UPDATE without
            -- DELETE is the control, not an oversight.
            GRANT SELECT, INSERT, UPDATE ON integrations."SyncRuns" TO propflow_app;
            -- Same rung, and the reason the conflict queue can be trusted: resolving a conflict is
            -- a status transition (Conflict.Resolve / Conflict.Ignore), never a removal, so a
            -- resolved conflict survives as evidence that a human looked at a divergence and made a
            -- call. There is deliberately no DELETE.
            GRANT SELECT, INSERT, UPDATE ON integrations."Conflicts" TO propflow_app;
            """;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
