namespace MockBucket.Core;

/// <summary>Idle-bucket expiry: buckets untouched past MB_RETENTION_IDLE_HOURS are dropped.
/// Ring overflow handles per-bucket entry caps; this handles forgotten buckets. Runs in the same process,
/// one timer, no external scheduler.</summary>
public sealed class RetentionService(BucketRegistry registry, CaptureOptions options, EventHub hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.IdleExpiryHours <= 0) return; // 0/negative = never expire
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(options.IdleExpiryHours);
                foreach (var b in registry.All())
                    if (b.LastSeen < cutoff && registry.Remove(b.Id))
                        hub.Publish(b.Id, "gone", "{}");
            }
        }
        catch (OperationCanceledException) { }
    }
}
