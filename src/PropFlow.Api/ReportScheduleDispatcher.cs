using Microsoft.EntityFrameworkCore;
using PropFlow.Domain.Reporting;
using PropFlow.Infrastructure.Persistence;

namespace PropFlow.Api;

public sealed class ReportScheduleDispatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReportScheduleDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<OperationsStore>();
                var due = await store.ReportSchedules.Where(x => x.IsActive && x.NextRunAt <= clock.GetUtcNow()).Take(100).ToListAsync(stoppingToken);
                foreach (var schedule in due)
                {
                    var now = clock.GetUtcNow(); var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{schedule.Kind}|{schedule.FilterJson}|{now:yyyy-MM-dd}")));
                    store.ReportDeliveries.Add(new ReportDelivery(store.OrganizationId, Guid.NewGuid(), schedule.Id, now, hash, 0)); schedule.MarkDelivered(now);
                }
                if (due.Count > 0) await store.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Report schedule dispatch failed"); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
