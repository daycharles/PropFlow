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
            GRANT SELECT ON operations."Vendors" TO propflow_app;
            GRANT SELECT ON operations."Employees", operations."Portfolios", operations."Properties", operations."Buildings", operations."Spaces" TO propflow_app;
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
