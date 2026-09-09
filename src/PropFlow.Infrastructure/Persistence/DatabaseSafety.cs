using Npgsql;

namespace PropFlow.Infrastructure.Persistence;

public static class DatabaseSafety
{
    public static async Task<bool> HasSafeRuntimeRoleAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreaterole AND NOT r.rolcreatedb
              AND NOT EXISTS (
                SELECT 1 FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid
                WHERE n.nspname IN ('identity', 'operations') AND c.relowner = r.oid)
              AND NOT EXISTS (
                SELECT 1 FROM pg_namespace n WHERE n.nspname IN ('identity', 'operations') AND n.nspowner = r.oid)
            FROM pg_roles r WHERE r.rolname = current_user
            """;
        return await command.ExecuteScalarAsync(ct) is true;
    }
}
