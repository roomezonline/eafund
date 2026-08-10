namespace MiniBank.Web.Services
{
    public sealed class DbBackupHostedService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<DbBackupHostedService> _logger;

        public DbBackupHostedService(IServiceProvider sp, ILogger<DbBackupHostedService> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Database Backup Service started");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime? nextDelayTargetLocal = null;
                try
                {
                    using var scope = _sp.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<DbBackupService>();
                    var repo = scope.ServiceProvider.GetRequiredService<MiniBank.Web.Infrastructure.Repositories.DbBackupRepository>();

                    // Distributed lock: ensure only one scheduler instance can evaluate/run at a time.
                    if (!await repo.TryAcquireSchedulerLockAsync(timeoutMs: 0))
                    {
                        nextDelayTargetLocal = DateTime.Now.AddSeconds(30);
                        continue;
                    }

                    try
                    {
                    var settings = await svc.GetSettingsAsync();

                    if (settings.IsEnabled)
                    {
                        var nowLocal = DateTime.Now;
                        var lastSuccessUtc = await repo.GetLastSuccessfulRunUtcAsync();
                        var lastSuccessLocal = lastSuccessUtc?.ToLocalTime();

                        var nextRunLocal = GetNextRunLocal(settings, nowLocal, lastSuccessLocal);
                        if (nextRunLocal <= nowLocal)
                        {
                            _logger.LogInformation("Running scheduled DB backup...");
                            await svc.RunBackupNowAsync("scheduler");

                            // Re-read last success after completion, then compute the next run.
                            lastSuccessUtc = await repo.GetLastSuccessfulRunUtcAsync();
                            lastSuccessLocal = lastSuccessUtc?.ToLocalTime();
                            nextRunLocal = GetNextRunLocal(settings, DateTime.Now, lastSuccessLocal);
                        }

                        nextDelayTargetLocal = nextRunLocal;
                        _logger.LogInformation($"Next backup scheduled for {nextRunLocal:yyyy-MM-dd HH:mm}");
                    }
                    }
                    finally
                    {
                        await repo.ReleaseSchedulerLockAsync();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in DbBackupHostedService");
                }

                // Delay until next scheduled time (precise), otherwise fall back to a short polling interval.
                try
                {
                    if (nextDelayTargetLocal.HasValue)
                    {
                        var delay = nextDelayTargetLocal.Value - DateTime.Now;
                        if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
                        if (delay > TimeSpan.FromDays(2)) delay = TimeSpan.FromHours(6);
                        await Task.Delay(delay, stoppingToken);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }
                }
                catch { }
            }
        }

        private static DateTime GetNextRunLocal(MiniBank.Web.Models.Admin.DbBackupSettings settings, DateTime nowLocal, DateTime? lastSuccessLocal)
        {
            var scheduledToday = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day)
                .AddMinutes(settings.RunAtTimeLocalMinutes % (24 * 60));

            switch (settings.Frequency)
            {
                case MiniBank.Web.Models.Admin.DbBackupFrequency.Daily:
                {
                    // If already ran for today's scheduled time, schedule tomorrow.
                    if (lastSuccessLocal.HasValue && lastSuccessLocal.Value >= scheduledToday && lastSuccessLocal.Value.Date == scheduledToday.Date)
                        return scheduledToday.AddDays(1);

                    // If time hasn't arrived yet, schedule today; else schedule now (due).
                    return nowLocal < scheduledToday ? scheduledToday : scheduledToday;
                }
                    
                case MiniBank.Web.Models.Admin.DbBackupFrequency.Weekly:
                {
                    var targetDay = settings.RunOnDayOfWeek ?? 0; // Default to Sunday if not set
                    var todayMatches = (int)nowLocal.DayOfWeek == targetDay;

                    if (todayMatches)
                    {
                        if (lastSuccessLocal.HasValue && lastSuccessLocal.Value >= scheduledToday && lastSuccessLocal.Value.Date == scheduledToday.Date)
                            return scheduledToday.AddDays(7);

                        return nowLocal < scheduledToday ? scheduledToday : scheduledToday;
                    }

                    // next target day
                    var daysToAdd = targetDay - (int)nowLocal.DayOfWeek;
                    if (daysToAdd <= 0) daysToAdd += 7;
                    return scheduledToday.AddDays(daysToAdd);
                }
                    
                case MiniBank.Web.Models.Admin.DbBackupFrequency.Monthly:
                {
                    var targetDay = Math.Clamp(settings.RunOnDayOfMonth ?? 1, 1, 28);
                    var scheduledThisMonth = new DateTime(nowLocal.Year, nowLocal.Month, targetDay)
                        .AddMinutes(settings.RunAtTimeLocalMinutes % (24 * 60));

                    if (nowLocal.Date == scheduledThisMonth.Date)
                    {
                        if (lastSuccessLocal.HasValue && lastSuccessLocal.Value >= scheduledThisMonth && lastSuccessLocal.Value.Date == scheduledThisMonth.Date)
                        {
                            var nextMonth = nowLocal.AddMonths(1);
                            return new DateTime(nextMonth.Year, nextMonth.Month, targetDay)
                                .AddMinutes(settings.RunAtTimeLocalMinutes % (24 * 60));
                        }

                        return nowLocal < scheduledThisMonth ? scheduledThisMonth : scheduledThisMonth;
                    }

                    if (nowLocal < scheduledThisMonth)
                        return scheduledThisMonth;

                    var nm = nowLocal.AddMonths(1);
                    return new DateTime(nm.Year, nm.Month, targetDay)
                        .AddMinutes(settings.RunAtTimeLocalMinutes % (24 * 60));
                }
                    
                default:
                    return scheduledToday.AddDays(1);
            }
        }
    }
}
