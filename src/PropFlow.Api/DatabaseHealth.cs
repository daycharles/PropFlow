using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public sealed class RuntimeDatabaseGuard(IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Database"));
        await connection.OpenAsync(ct);
        if (!await DatabaseSafety.HasSafeRuntimeRoleAsync(connection, ct))
            throw new InvalidOperationException("The API requires a non-owner database role without RLS bypass or administration privileges.");
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class DatabaseReadiness(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Database"));
            await connection.OpenAsync(ct);
            if (!await DatabaseSafety.HasSafeRuntimeRoleAsync(connection, ct)) return HealthCheckResult.Unhealthy();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT to_regclass('identity."AspNetUsers"') IS NOT NULL
                  AND to_regclass('identity."Memberships"') IS NOT NULL
                  AND (SELECT count(*) >= 68
                         AND count(*) FILTER (WHERE NOT (c.relrowsecurity AND c.relforcerowsecurity)) = 0
                       FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid
                       WHERE n.nspname = 'operations' AND c.relkind = 'r' AND c.relname <> '__OperationsMigrations')
                  AND (SELECT count(*) >= 2
                         AND count(*) FILTER (WHERE NOT (c.relrowsecurity AND c.relforcerowsecurity)) = 0
                       FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid
                       WHERE n.nspname = 'communications' AND c.relkind = 'r' AND c.relname <> '__CommunicationsMigrations')
                  AND (SELECT count(*) >= 2
                         AND count(*) FILTER (WHERE NOT (c.relrowsecurity AND c.relforcerowsecurity)) = 0
                       FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid
                       WHERE n.nspname = 'integrations' AND c.relkind = 'r' AND c.relname <> '__IntegrationsMigrations')
                """;
            return await command.ExecuteScalarAsync(ct) is true ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
