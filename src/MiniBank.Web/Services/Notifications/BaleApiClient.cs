using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models.Notifications;
using System.Net.Http.Json;
using System.Text.Json;

namespace MiniBank.Web.Services.Notifications;

public class BaleApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BaleBotRepository _repo;
    private readonly ILogger<BaleApiClient> _logger;

    public BaleApiClient(
        IHttpClientFactory httpClientFactory,
        BaleBotRepository repo,
        ILogger<BaleApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repo = repo;
        _logger = logger;
    }

    public async Task<BaleSendResult> SendMessageAsync(long chatId, string text, CancellationToken ct = default)
        => await SendMessageCoreAsync(chatId, text, null, ct);

    public async Task<BaleSendResult> SendContactRequestAsync(long chatId, string text, CancellationToken ct = default)
        => await SendMessageCoreAsync(chatId, text, new
        {
            keyboard = new[]
            {
                new[] { new { text = "ارسال شماره موبایل", request_contact = true } }
            },
            resize_keyboard = true,
            one_time_keyboard = true
        }, ct);

    public async Task<BaleSendResult> SendRemoveKeyboardAsync(long chatId, string text, CancellationToken ct = default)
        => await SendMessageCoreAsync(chatId, text, new { remove_keyboard = true }, ct);

    public async Task<BaleSendResult> SendMenuAsync(long chatId, string text, CancellationToken ct = default)
        => await SendMessageCoreAsync(chatId, text, new
        {
            keyboard = new[]
            {
                new[] { new { text = BaleMenuButtons.Household }, new { text = BaleMenuButtons.Balance } },
                new[] { new { text = BaleMenuButtons.Loan }, new { text = BaleMenuButtons.Cards } },
                new[] { new { text = BaleMenuButtons.Help } }
            },
            resize_keyboard = true
        }, ct);

    private async Task<BaleSendResult> SendMessageCoreAsync(long chatId, string text, object? replyMarkup, CancellationToken ct)
    {
        var settings = await _repo.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BotToken))
            return new BaleSendResult(false, null, null, null, null, "توکن ربات بله تنظیم نشده است.");

        if (chatId == 0)
            return new BaleSendResult(false, null, null, null, null, "شناسه گفتگو (ChatId) نامعتبر است.");

        var limited = Limit(text, 4000);
        var body = replyMarkup == null
            ? JsonSerializer.Serialize(new { chat_id = chatId, text = limited })
            : JsonSerializer.Serialize(new { chat_id = chatId, text = limited, reply_markup = replyMarkup });

        var raw = await PostAsync(settings, "sendMessage", body, ct);
        return ParseSendResult(raw);
    }

    public async Task<BaleBotInfo> GetMeAsync(CancellationToken ct = default)
    {
        var settings = await _repo.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BotToken))
            return new BaleBotInfo(false, null, null, "توکن ربات بله تنظیم نشده است.");

        var raw = await GetAsync(settings, "getMe", ct);
        if (raw == null)
            return new BaleBotInfo(false, null, null, "پاسخی از سرور بله دریافت نشد.");

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                var username = result.TryGetProperty("username", out var u) ? u.GetString() : null;
                var firstName = result.TryGetProperty("first_name", out var f) ? f.GetString() : null;
                return new BaleBotInfo(true, username, firstName, null);
            }

            var error = root.TryGetProperty("description", out var d) ? d.GetString() : raw;
            return new BaleBotInfo(false, null, null, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bale getMe response parse failed");
            return new BaleBotInfo(false, null, null, raw);
        }
    }

    public async Task<(bool ok, string? error)> SetWebhookAsync(string url, CancellationToken ct = default)
    {
        var settings = await _repo.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BotToken))
            return (false, "توکن ربات بله تنظیم نشده است.");

        if (string.IsNullOrWhiteSpace(url))
            return (false, "آدرس وبهوک نامعتبر است.");

        var body = JsonSerializer.Serialize(new { url });
        var raw = await PostAsync(settings, "setWebhook", body, ct);
        if (raw == null)
            return (false, "پاسخی از سرور بله دریافت نشد.");

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return (true, null);

            var error = root.TryGetProperty("description", out var d) ? d.GetString() : raw;
            return (false, error);
        }
        catch
        {
            return (false, raw);
        }
    }

    public async Task<(bool ok, string? error)> DeleteWebhookAsync(CancellationToken ct = default)
    {
        var settings = await _repo.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BotToken))
            return (false, "توکن ربات بله تنظیم نشده است.");

        var raw = await PostAsync(settings, "deleteWebhook", "{}", ct);
        if (raw == null)
            return (false, "پاسخی از سرور بله دریافت نشد.");

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return (true, null);

            var error = root.TryGetProperty("description", out var d) ? d.GetString() : raw;
            return (false, error);
        }
        catch
        {
            return (false, raw);
        }
    }

    public async Task<string?> GetWebhookInfoAsync(CancellationToken ct = default)
    {
        var settings = await _repo.GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.BotToken))
            return null;

        return await GetAsync(settings, "getWebhookInfo", ct);
    }

    private async Task<string?> PostAsync(BaleBotSettings settings, string method, string jsonBody, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("bale");
            var url = BuildUrl(settings, method);

            using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, ct);

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bale API call failed: {Method}", method);
            return null;
        }
    }

    private async Task<string?> GetAsync(BaleBotSettings settings, string method, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("bale");
            var url = BuildUrl(settings, method);

            using var response = await client.GetAsync(url, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bale API call failed: {Method}", method);
            return null;
        }
    }

    private static string BuildUrl(BaleBotSettings settings, string method)
    {
        var baseUrl = string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            ? "https://tapi.bale.ai"
            : settings.ApiBaseUrl.Trim().TrimEnd('/');

        return $"{baseUrl}/bot{settings.BotToken}/{method}";
    }

    private static BaleSendResult ParseSendResult(string? raw)
    {
        if (raw == null)
            return new BaleSendResult(false, null, null, null, null, "پاسخی از سرور بله دریافت نشد.");

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                long? messageId = null;
                int? status = 1;

                if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("message_id", out var mid) && mid.TryGetInt64(out var midVal))
                {
                    messageId = midVal;
                }

                return new BaleSendResult(true, messageId, status, null, raw, null);
            }

            int? apiStatus = null;
            if (root.TryGetProperty("error_code", out var ec) && ec.TryGetInt32(out var ecVal))
                apiStatus = ecVal;

            var description = root.TryGetProperty("description", out var d) ? d.GetString() : raw;
            return new BaleSendResult(false, null, apiStatus, description, raw, description);
        }
        catch
        {
            return new BaleSendResult(false, null, null, null, raw, raw);
        }
    }

    private static string Limit(string? text, int max)
    {
        var value = text ?? string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}

public static class BaleMenuButtons
{
    public const string Household = "👥 اعضای خانوار";
    public const string Balance = "💰 موجودی من";
    public const string Loan = "🏦 وضعیت وام";
    public const string Cards = "💳 کارت‌های صندوق";
    public const string Help = "ℹ️ راهنما";
}
