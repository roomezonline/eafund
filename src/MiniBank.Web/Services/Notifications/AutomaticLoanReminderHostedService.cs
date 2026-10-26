using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Services.Notifications;

public sealed class AutomaticLoanReminderHostedService : BackgroundService
{
    private const string FeatureKey = "loan_installment_reminder";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomaticLoanReminderHostedService> _logger;

    public AutomaticLoanReminderHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AutomaticLoanReminderHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DelayAsync(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Automatic loan reminder loop failed");
            }

            await DelayAsync(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<NotificationRepository>();
        var settings = await repo.GetAutoSettingsAsync();

        if (!settings.LoanReminderEnabled)
            return;

        var now = DateTime.Now;
        if (now.Hour < settings.RunHour)
            return;

        if (settings.LastRunDate.HasValue && settings.LastRunDate.Value.Date >= now.Date)
            return;

        var reminders = scope.ServiceProvider.GetRequiredService<LoanInstallmentReminderRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var rows = await reminders.SearchAsync(settings.DaysBeforeDue, settings.IncludeOverdue, null, 500);
        var queued = 0;

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested)
                return;

            if (row.MemberId <= 0)
                continue;

            var eventKey = $"loan-reminder:{row.InstallmentId}:{now:yyyyMMdd}";
            var dueDate = NotificationText.FormatJalaliDate(row.DueDate);
            var amount = row.EmiAmount.ToString("N0");
            var message = NotificationText.BuildLoanReminderMessage(
                row.MemberFullName,
                amount,
                dueDate);

            var result = await notifications.NotifyAsync(new NotificationRequest
            {
                FeatureKey = FeatureKey,
                MessageText = message,
                BaleMessageText = NotificationText.BuildLoanReminderBaleMessage(
                    row.MemberFullName, row.FatherName, amount, dueDate),
                MemberId = row.MemberId,
                HeadMemberId = row.MemberId,
                RecipientMobile = row.MobileNumber,
                RecipientName = row.MemberFullName,
                SmsTemplateId = 387302,
                SmsParams = new Dictionary<string, string>
                {
                    ["NAME"] = NotificationText.NormalizeName(row.MemberFullName),
                    ["PRICE"] = $"{row.EmiAmount:N0} ریال",
                    ["DATE"] = NotificationText.FormatJalaliDate(row.DueDate)
                },
                EventKey = eventKey,
                CreatedBy = "auto-reminder",
                DispatchNow = false
            }, ct);

            if (result.Ok)
                queued++;
        }

        await repo.TryStartAutoRunAsync(now.Date);

        if (queued > 0)
            _logger.LogInformation("Automatic loan reminders queued: {Count}", queued);
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
