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
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."PropertyContacts" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."PropertyDocuments" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."Applicants" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."HouseholdMembers" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."WorkCategories", operations."Residents", operations."Occupancies", operations."Assets" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."SavedViews" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."AutomationRules" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON operations."RepeatRepairPolicies" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."WorkItems" TO propflow_app;
            GRANT SELECT, INSERT ON operations."Timeline" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON operations."Attachments" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON communications."MessageTemplates" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON communications."OutboxMessages" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON integrations."Connections" TO propflow_app;
            GRANT SELECT, INSERT, UPDATE ON integrations."RecordLinks" TO propflow_app;
            """;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
