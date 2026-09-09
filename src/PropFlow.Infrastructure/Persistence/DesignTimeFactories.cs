using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PropFlow.Application;
using PropFlow.Infrastructure.Communications;
using PropFlow.Infrastructure.Identity;

namespace PropFlow.Infrastructure.Persistence;

public sealed class IdentityDesignFactory : IDesignTimeDbContextFactory<IdentityStore>
{
    public IdentityStore CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<IdentityStore>()
        .UseNpgsql(DesignConnection.Value, options => options.MigrationsHistoryTable("__IdentityMigrations", "identity")).Options);
}
public sealed class OperationsDesignFactory : IDesignTimeDbContextFactory<OperationsStore>
{
    public OperationsStore CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OperationsStore>()
        .UseNpgsql(DesignConnection.Value, options => options.MigrationsHistoryTable("__OperationsMigrations", "operations")).Options,
        new FixedTenantContext(Guid.Parse("00000000-0000-0000-0000-000000000001")));
}
public sealed class CommunicationsDesignFactory : IDesignTimeDbContextFactory<CommunicationsStore>
{
    public CommunicationsStore CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CommunicationsStore>()
        .UseNpgsql(DesignConnection.Value, options => options.MigrationsHistoryTable("__CommunicationsMigrations", "communications")).Options,
        new FixedTenantContext(Guid.Parse("00000000-0000-0000-0000-000000000001")));
}
internal static class DesignConnection
{
    internal static string Value => Environment.GetEnvironmentVariable("ConnectionStrings__Admin")
        ?? "Host=localhost;Database=propflow;Username=propflow";
}
