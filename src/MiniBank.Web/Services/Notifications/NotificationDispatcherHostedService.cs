using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Services.Notifications;

public sealed class NotificationDispatcherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatcherHostedService> _logger;

    public NotificationDispatcherHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DelayAsync(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<NotificationRepository>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();

                await repo.ResetStaleAsync();

                // در یک چرخه چند دسته پشت سر هم ارسال می‌شود تا ثبت‌های انبوه
                // (مثل سود سپرده) معطل نمانند و همه پیام‌ها به ترتیب ارسال شوند.
                while (!stoppingToken.IsCancellationRequested)
                {
                    var items = await repo.ClaimDueAsync(25);
                    if (items.Count == 0)
                        break;

                    foreach (var item in items)
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        try
                        {
                            await dispatcher.DispatchClaimedAsync(item, stoppingToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Notification dispatch failed for {NotificationId}", item.NotificationId);
                        }
                    }

                    processed += items.Count;

                    if (processed >= 500)
                        break;

                    await DelayAsync(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Notification dispatcher loop failed");
            }

            await DelayAsync(processed > 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (TaskCanceledException)
        {
            // Host is shutting down.
        }
    }
}
