using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models.Notifications;
using System.Text.Json;

namespace MiniBank.Web.Services.Notifications;

public sealed record NotificationDispatchOutcome(
    bool Ok,
    byte Status,
    bool SmsSent,
    bool BaleSent,
    string Message,
    string? Detail);

public class NotificationDispatcher
{
    private const int MaxAttempts = 3;

    private readonly NotificationRepository _repo;
    private readonly BaleBotRepository _baleRepo;
    private readonly BaleApiClient _baleClient;
    private readonly SmsPanelRepository _smsPanelRepo;
    private readonly SmsSender _smsSender;
    private readonly SmsMessageLogRepository _smsLogRepo;
    private readonly SmsSendService _smsSendService;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        NotificationRepository repo,
        BaleBotRepository baleRepo,
        BaleApiClient baleClient,
        SmsPanelRepository smsPanelRepo,
        SmsSender smsSender,
        SmsMessageLogRepository smsLogRepo,
        SmsSendService smsSendService,
        ILogger<NotificationDispatcher> logger)
    {
        _repo = repo;
        _baleRepo = baleRepo;
        _baleClient = baleClient;
        _smsPanelRepo = smsPanelRepo;
        _smsSender = smsSender;
        _smsLogRepo = smsLogRepo;
        _smsSendService = smsSendService;
        _logger = logger;
    }

    public async Task<NotificationDispatchOutcome> DispatchAsync(long notificationId, CancellationToken ct = default)
    {
        var item = await _repo.GetAsync(notificationId);
        if (item == null)
            return new NotificationDispatchOutcome(false, 0, false, false, "اطلاع‌رسانی یافت نشد.", null);

        if (item.Status is NotificationStatuses.Sent or NotificationStatuses.Cancelled)
            return new NotificationDispatchOutcome(true, item.Status, item.SmsSentAt.HasValue, item.BaleSentAt.HasValue, "این اطلاع‌رسانی قبلاً پردازش شده است.", null);

        if (item.Status == NotificationStatuses.PendingApproval)
            return new NotificationDispatchOutcome(false, item.Status, false, false, "این اطلاع‌رسانی در انتظار تایید مدیر است.", null);

        if (item.Status == NotificationStatuses.Queued && !await _repo.TryStartAsync(notificationId))
            return new NotificationDispatchOutcome(false, item.Status, false, false, "امکان شروع ارسال وجود ندارد.", null);

        item = await _repo.GetAsync(notificationId) ?? item;
        return await DispatchClaimedAsync(item, ct);
    }

    public async Task<NotificationDispatchOutcome> DispatchClaimedAsync(NotificationQueueItem item, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var notes = new List<string>();
        var warnings = new List<string>();
        var attempted = false;

        var smsSent = false;
        long? smsLogId = null;
        var baleSent = false;
        long? baleLogId = null;

        if (NotificationChannels.HasSms(item.ChannelMask) && !item.SmsSentAt.HasValue)
        {
            var smsResult = await TrySendSmsAsync(item, ct);
            attempted |= smsResult.Attempted;
            smsSent = smsResult.Success;
            smsLogId = smsResult.LogId;
            if (!string.IsNullOrWhiteSpace(smsResult.Warning))
                warnings.Add(smsResult.Warning);
            if (!string.IsNullOrWhiteSpace(smsResult.Note))
            {
                if (smsResult.Attempted) errors.Add(smsResult.Note);
                else notes.Add(smsResult.Note);
            }
        }

        if (NotificationChannels.HasBale(item.ChannelMask) && !item.BaleSentAt.HasValue)
        {
            var baleResult = await TrySendBaleAsync(item, ct);
            attempted |= baleResult.Attempted;
            baleSent = baleResult.Success;
            baleLogId = baleResult.LogId;
            if (!string.IsNullOrWhiteSpace(baleResult.Note))
            {
                if (baleResult.Attempted) errors.Add(baleResult.Note);
                else notes.Add(baleResult.Note);
            }
        }

        var errorSummary = errors.Count == 0 ? null : string.Join(" | ", errors);
        if (errorSummary is { Length: > 500 })
            errorSummary = errorSummary[..500];

        if (smsSent || baleSent)
        {
            await _repo.MarkSentAsync(item.NotificationId, smsSent, smsLogId, baleSent, baleLogId, errorSummary);

            var channels = new List<string>();
            if (smsSent) channels.Add("پیامک");
            if (baleSent) channels.Add("بله");
            var message = $"اطلاع‌رسانی از طریق {string.Join(" + ", channels)} ارسال شد.";
            if (warnings.Count > 0)
                message += " " + string.Join(" ", warnings);

            return new NotificationDispatchOutcome(true, NotificationStatuses.Sent, smsSent, baleSent, message, errorSummary);
        }

        if (!attempted)
        {
            var reason = notes.Count == 0 ? "کانال ارسال در دسترس نیست." : string.Join(" | ", notes);
            if (reason.Length > 500) reason = reason[..500];
            await _repo.MarkSkippedAsync(item.NotificationId, reason);
            return new NotificationDispatchOutcome(false, NotificationStatuses.Skipped, false, false, reason, reason);
        }

        if (item.Attempts >= MaxAttempts)
        {
            await _repo.MarkFailedAsync(item.NotificationId, errorSummary, smsSent, smsLogId, baleSent, baleLogId);
            return new NotificationDispatchOutcome(false, NotificationStatuses.Failed, smsSent, baleSent,
                "ارسال اطلاع‌رسانی ناموفق بود.", errorSummary);
        }

        var delayMinutes = item.Attempts switch
        {
            <= 1 => 1,
            2 => 5,
            _ => 15
        };

        await _repo.ScheduleRetryAsync(item.NotificationId, delayMinutes, errorSummary, smsSent, smsLogId, baleSent, baleLogId);

        return new NotificationDispatchOutcome(false, NotificationStatuses.Queued, smsSent, baleSent,
            "ارسال ناموفق بود؛ برای تلاش مجدد در صف قرار گرفت.", errorSummary);
    }

    private async Task<(bool Attempted, bool Success, long? LogId, string? Note, string? Warning)> TrySendSmsAsync(
        NotificationQueueItem item,
        CancellationToken ct)
    {
        var mobile = (item.RecipientMobile ?? string.Empty).Trim();
        var templateId = item.SmsTemplateId ?? 0;

        if (string.IsNullOrWhiteSpace(mobile))
            return (false, false, null, "شماره موبایل گیرنده موجود نیست.", null);

        if (templateId <= 0)
            return (false, false, null, "شناسه قالب پیامک تنظیم نشده است.", null);

        try
        {
            var settings = await _smsPanelRepo.GetSettingsAsync();
            if (!settings.IsEnabled)
                return (false, false, null, "ارسال پیامک غیرفعال است.", null);

            var parameters = ParseParams(item.SmsParamsJson);

            var logId = await _smsLogRepo.CreateAsync(
                featureKey: item.FeatureKey,
                householdId: item.HouseholdId,
                headMemberId: item.HeadMemberId ?? item.MemberId,
                memberIdsJson: item.MemberIdsJson,
                recipientMobile: mobile,
                templateId: templateId,
                paramsJson: item.SmsParamsJson,
                messageText: item.MessageText);

            string? rawResponse = null;
            long? providerMessageId = null;
            int? apiStatus = null;
            string? apiMessage = null;

            try
            {
                rawResponse = await _smsSender.SendVerifyWithParametersAsync(mobile, templateId, parameters, ct);

                try
                {
                    using var doc = JsonDocument.Parse(rawResponse);
                    if (doc.RootElement.TryGetProperty("status", out var s) && s.TryGetInt32(out var st))
                        apiStatus = st;
                    if (doc.RootElement.TryGetProperty("message", out var m))
                        apiMessage = m.GetString();
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                        data.TryGetProperty("messageId", out var mid) && mid.TryGetInt64(out var midVal))
                    {
                        providerMessageId = midVal;
                    }
                }
                catch
                {
                    // Response shape is provider-specific; the HTTP call already succeeded.
                }

                var isSuccess = !apiStatus.HasValue || apiStatus.Value == 1;
                await _smsLogRepo.MarkResultAsync(logId, isSuccess, rawResponse, providerMessageId, apiStatus, apiMessage, errorMessage: isSuccess ? null : apiMessage);

                if (!isSuccess)
                    return (true, false, logId, apiMessage ?? "سرویس پیامک ارسال را ناموفق اعلام کرد.", null);

                var parts = EstimateParts(item.MessageText);
                var creditResult = await _smsSendService.ConsumeCreditAfterSuccessfulSendAsync(parts);
                var warning = creditResult.lowCredit
                    ? $"هشدار: اعتبار پیامک کمتر از {creditResult.thresholdRial:N0} ریال است."
                    : null;

                return (true, true, logId, null, warning);
            }
            catch (Exception exSend)
            {
                await _smsLogRepo.MarkResultAsync(logId, false, rawResponse, providerMessageId, apiStatus, apiMessage, exSend.Message);
                _logger.LogWarning(exSend, "SMS dispatch failed for notification {NotificationId}", item.NotificationId);
                return (true, false, logId, exSend.Message, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMS dispatch preparation failed for notification {NotificationId}", item.NotificationId);
            return (true, false, null, ex.Message, null);
        }
    }

    private async Task<(bool Attempted, bool Success, long? LogId, string? Note)> TrySendBaleAsync(
        NotificationQueueItem item,
        CancellationToken ct)
    {
        if (!item.MemberId.HasValue || item.MemberId.Value <= 0)
            return (false, false, null, "عضو گیرنده مشخص نیست.");

        var subscriber = await _baleRepo.GetActiveByMemberAsync(item.MemberId.Value);
        if (subscriber == null)
            return (false, false, null, "عضو به پیام‌رسان بله متصل نیست.");

        var settings = await _baleRepo.GetSettingsAsync();
        if (!settings.IsEnabled)
            return (false, false, null, "ارسال بله غیرفعال است.");

        var baleText = string.IsNullOrWhiteSpace(item.BaleMessageText)
            ? item.MessageText
            : item.BaleMessageText;

        long? logId = null;
        try
        {
            logId = await _baleRepo.CreateMessageLogAsync(
                item.NotificationId,
                item.MemberId,
                subscriber.ChatId,
                item.FeatureKey,
                baleText);

            var result = await _baleClient.SendMessageAsync(subscriber.ChatId, baleText, ct);
            await _baleRepo.MarkMessageResultAsync(
                logId.Value,
                result.Ok,
                result.RawResponse,
                result.MessageId,
                result.ApiStatus,
                result.ApiMessage,
                result.Error);

            if (!result.Ok)
            {
                _logger.LogWarning("Bale dispatch failed for notification {NotificationId}: {Error}", item.NotificationId, result.Error);
                return (true, false, logId, result.Error ?? "ارسال بله ناموفق بود.");
            }

            return (true, true, logId, null);
        }
        catch (Exception ex)
        {
            if (logId.HasValue)
            {
                try { await _baleRepo.MarkMessageResultAsync(logId.Value, false, null, null, null, null, ex.Message); } catch { }
            }

            _logger.LogWarning(ex, "Bale dispatch failed for notification {NotificationId}", item.NotificationId);
            return (true, false, logId, ex.Message);
        }
    }

    private static Dictionary<string, string> ParseParams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return values == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static int EstimateParts(string? text)
    {
        var length = (text ?? string.Empty).Length;
        if (length <= 0)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(length / 70.0));
    }
}
