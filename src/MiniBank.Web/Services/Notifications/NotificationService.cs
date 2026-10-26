using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models.Notifications;
using System.Text.Json;

namespace MiniBank.Web.Services.Notifications;

public sealed record NotificationRequest
{
    public string FeatureKey { get; init; } = string.Empty;
    public string MessageText { get; init; } = string.Empty;
    public string? BaleMessageText { get; init; }
    public long? MemberId { get; init; }
    public long? HouseholdId { get; init; }
    public long? HeadMemberId { get; init; }
    public string? MemberIdsJson { get; init; }
    public string? RecipientMobile { get; init; }
    public string? RecipientName { get; init; }
    public int? SmsTemplateId { get; init; }
    public IReadOnlyDictionary<string, string>? SmsParams { get; init; }
    public string? EventKey { get; init; }
    public string? CreatedBy { get; init; }
    public bool DispatchNow { get; init; } = true;
}

public sealed record NotificationAvailability(
    bool Enabled,
    byte ChannelMask,
    byte EffectiveMask,
    bool SmsReady,
    bool BaleReady,
    string? Reason)
{
    public bool HasAnyChannel => EffectiveMask != 0;

    public string EffectiveLabel => NotificationChannels.Label(EffectiveMask);
}

public sealed record NotificationSendResult(
    bool Ok,
    bool PendingApproval,
    bool SmsSent,
    bool BaleSent,
    string Message,
    long? NotificationId,
    byte Status,
    string? Detail);

public class NotificationService
{
    private readonly NotificationRepository _repo;
    private readonly BaleBotRepository _baleRepo;
    private readonly SmsPanelRepository _smsPanelRepo;
    private readonly NotificationDispatcher _dispatcher;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        NotificationRepository repo,
        BaleBotRepository baleRepo,
        SmsPanelRepository smsPanelRepo,
        NotificationDispatcher dispatcher,
        ILogger<NotificationService> logger)
    {
        _repo = repo;
        _baleRepo = baleRepo;
        _smsPanelRepo = smsPanelRepo;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<NotificationAvailability> GetAvailabilityAsync(string featureKey, string? mobile, long? memberId)
    {
        var feature = await _repo.GetFeatureAsync(featureKey);
        if (feature == null)
            return new NotificationAvailability(false, NotificationChannels.Sms, 0, false, false, "نوع اطلاع‌رسانی تعریف نشده است.");

        var mask = feature.ChannelMask;
        if (!feature.IsEnabled)
            return new NotificationAvailability(false, mask, 0, false, false, "این نوع اطلاع‌رسانی در تنظیمات غیرفعال است.");

        var smsReady = false;
        if (NotificationChannels.HasSms(mask) && !string.IsNullOrWhiteSpace(mobile))
        {
            var smsSettings = await _smsPanelRepo.GetSettingsAsync();
            smsReady = smsSettings.IsEnabled;
        }

        var baleReady = false;
        if (NotificationChannels.HasBale(mask) && memberId.HasValue && memberId.Value > 0)
        {
            var baleSettings = await _baleRepo.GetSettingsAsync();
            if (baleSettings.IsEnabled && !string.IsNullOrWhiteSpace(baleSettings.BotToken))
            {
                var subscriber = await _baleRepo.GetActiveByMemberAsync(memberId.Value);
                baleReady = subscriber != null;
            }
        }

        var effective = (byte)((smsReady ? NotificationChannels.Sms : 0) | (baleReady ? NotificationChannels.Bale : 0));

        string? reason = null;
        if (effective == 0)
        {
            var reasons = new List<string>();
            if (NotificationChannels.HasSms(mask) && !smsReady)
                reasons.Add("پیامک آماده نیست (غیرفعال، بدون شماره موبایل یا قابلیت پیامک خاموش است)");
            if (NotificationChannels.HasBale(mask) && !baleReady)
                reasons.Add("عضو به پیام‌رسان بله متصل نیست یا ارسال بله غیرفعال است");
            if (reasons.Count == 0)
                reasons.Add("کانال ارسال مشخص نشده است");
            reason = string.Join("؛ ", reasons);
        }

        return new NotificationAvailability(true, mask, effective, smsReady, baleReady, reason);
    }

    public async Task<NotificationSendResult> NotifyAsync(NotificationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FeatureKey))
            return new NotificationSendResult(false, false, false, false, "نوع اطلاع‌رسانی مشخص نیست.", null, 0, null);

        if (string.IsNullOrWhiteSpace(request.MessageText))
            return new NotificationSendResult(false, false, false, false, "متن اطلاع‌رسانی خالی است.", null, 0, null);

        var availability = await GetAvailabilityAsync(request.FeatureKey, request.RecipientMobile, request.MemberId);
        if (!availability.Enabled)
            return new NotificationSendResult(false, false, false, false, availability.Reason ?? "اطلاع‌رسانی غیرفعال است.", null, 0, availability.Reason);

        if (!availability.HasAnyChannel)
            return new NotificationSendResult(false, false, false, false, availability.Reason ?? "کانال ارسال در دسترس نیست.", null, NotificationStatuses.Skipped, availability.Reason);

        var feature = await _repo.GetFeatureAsync(request.FeatureKey);
        var pendingApproval = feature?.ApprovalMode == 1;
        var status = pendingApproval ? NotificationStatuses.PendingApproval : NotificationStatuses.Queued;

        var item = new NotificationQueueItem
        {
            EventKey = string.IsNullOrWhiteSpace(request.EventKey) ? null : request.EventKey.Trim(),
            FeatureKey = request.FeatureKey.Trim(),
            MemberId = request.MemberId,
            HouseholdId = request.HouseholdId,
            HeadMemberId = request.HeadMemberId ?? request.MemberId,
            MemberIdsJson = request.MemberIdsJson,
            RecipientMobile = string.IsNullOrWhiteSpace(request.RecipientMobile) ? null : request.RecipientMobile.Trim(),
            RecipientName = request.RecipientName,
            SmsTemplateId = request.SmsTemplateId,
            SmsParamsJson = request.SmsParams == null ? null : JsonSerializer.Serialize(request.SmsParams),
            MessageText = request.MessageText,
            BaleMessageText = string.IsNullOrWhiteSpace(request.BaleMessageText) ? null : request.BaleMessageText,
            ChannelMask = availability.EffectiveMask,
            Status = status,
            CreatedBy = request.CreatedBy,
            NextAttemptAt = pendingApproval ? null : DateTime.UtcNow
        };

        var notificationId = await _repo.EnqueueAsync(item);
        if (!notificationId.HasValue)
        {
            return new NotificationSendResult(true, false, false, false,
                "این اطلاع‌رسانی قبلاً ثبت شده است و دوباره ارسال نمی‌شود.", null, status, null);
        }

        if (pendingApproval)
        {
            return new NotificationSendResult(true, true, false, false,
                "اطلاع‌رسانی برای تایید مدیر در صف قرار گرفت.", notificationId, status, null);
        }

        if (!request.DispatchNow)
        {
            return new NotificationSendResult(true, false, false, false,
                "اطلاع‌رسانی در صف ارسال قرار گرفت.", notificationId, status, null);
        }

        var outcome = await _dispatcher.DispatchAsync(notificationId.Value, ct);
        return new NotificationSendResult(
            outcome.Ok,
            false,
            outcome.SmsSent,
            outcome.BaleSent,
            outcome.Message,
            notificationId,
            outcome.Status,
            outcome.Detail);
    }

    public Task<NotificationDispatchOutcome> DispatchNowAsync(long notificationId, CancellationToken ct = default)
        => _dispatcher.DispatchAsync(notificationId, ct);
}
