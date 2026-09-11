using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropFlow.Infrastructure.Attachments;

// Runs the attachment retention sweep on a fixed interval. The per-tenant deletion logic lives in
// AttachmentRetentionSweep; this type only owns the schedule and failure isolation. The sweep
// (and, through it, IAttachmentStorage) is resolved lazily on the first tick so a host whose
// attachment storage is not yet configured still starts — the same "fails on use, not on boot"
// behaviour the upload path already has.
public sealed class AttachmentRetentionService(IServiceProvider services, ILogger<AttachmentRetentionService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await services.GetRequiredService<AttachmentRetentionSweep>().SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Attachment retention sweep cycle failed.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
