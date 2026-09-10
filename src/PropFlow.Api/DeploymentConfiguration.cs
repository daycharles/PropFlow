using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace PropFlow.Api;

public static class DeploymentConfiguration
{
    public static void ValidatePostgresTls(string connectionString, IHostEnvironment environment)
    {
        if (!environment.IsProduction() && !environment.IsStaging()) return;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.SslMode != SslMode.VerifyFull)
            throw new InvalidOperationException(
                "Production and staging require PostgreSQL Ssl Mode=VerifyFull with a trusted server certificate.");
    }

    public static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var enabled = configuration.GetValue("ForwardedHeaders:Enabled", false);
        var rawProxies = configuration["ForwardedHeaders:KnownProxies"];
        var proxies = (rawProxies ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => IPAddress.TryParse(value, out var address) ? address : throw new InvalidOperationException(
                $"ForwardedHeaders:KnownProxies contains an invalid IP address: '{value}'."))
            .ToArray();

        if ((environment.IsProduction() || environment.IsStaging()) && (!enabled || proxies.Length == 0))
            throw new InvalidOperationException(
                "Production and staging require ForwardedHeaders:Enabled=true and an explicit " +
                "ForwardedHeaders:KnownProxies allow-list.");

        if (!enabled) return;
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
        options.RequireHeaderSymmetry = true;
    }
}
