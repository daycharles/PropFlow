using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PropFlow.Application;

namespace PropFlow.Infrastructure.Persistence;

// Applied on every connection checkout, including pooled connections. Npgsql resets connections
// when returned to the pool; we still overwrite the session value on each open.
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var id = tenant.OrganizationId;
        if (id == Guid.Empty) throw new TenantAccessException();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.organization_id', @tenant, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = id.ToString();
        command.Parameters.Add(parameter);
        return command;
    }
}
