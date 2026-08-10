using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Services
{
    /// <summary>
    /// کلاس پایه برای ارسال پیامک. بعداً کد ارسال پیامک واقعی را اینجا جایگزین کنید.
    /// از هرجای پروژه می‌توانید این کلاس را صدا بزنید.
    /// </summary>
    public class SmsSender
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly SmsPanelRepository _smsPanelRepository;

        public SmsSender(IHttpClientFactory httpClientFactory, IConfiguration configuration, SmsPanelRepository smsPanelRepository)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _smsPanelRepository = smsPanelRepository;
        }

        /// <summary>
        /// ارسال پیامک به شماره مقصد.
        /// بعداً بدنهٔ این متد را با کد سرویس پیامک خودتان جایگزین کنید.
        /// </summary>
        /// <param name="phoneNumber">شماره موبایل مقصد (به‌صورت استاندارد)</param>
        /// <param name="message">متن پیامک</param>
        /// <param name="ct">توکن لغو عملیات</param>
        public Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
        {
            // TODO: بدنهٔ واقعی ارسال پیامک را اینجا پیاده‌سازی کنید (کال کردن API سرویس‌دهنده‌ پیامک)
            // فعلاً به‌صورت Placeholder خالی است تا شما قالب اصلی را بدهید.
            return Task.CompletedTask;
        }

        public async Task<string> SendVerifyAsync(
            string mobile,
            int templateId,
            string PRICE,
            string BABAT,
            string MANBAB,
            string MANPRICE,
            string NAME = "",
            CancellationToken ct = default)
        {
            return await SendVerifyWithParametersAsync(
                mobile,
                templateId,
                new Dictionary<string, string>
                {
                    ["PRICE"] = PRICE,
                    ["BABAT"] = BABAT,
                    ["MANBAB"] = MANBAB,
                    ["MANPRICE"] = MANPRICE,
                    ["NAME"] = NAME
                },
                ct);
        }

        public async Task<string> SendVerifyWithParametersAsync(
            string mobile,
            int templateId,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken ct = default)
        {
            await EnsureSmsEnabledAsync(featureKey: null);

            var apiKey = GetApiKey();

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Remove("x-api-key");
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

            var payloadObj = new
            {
                mobile,
                templateId,
                parameters = (parameters ?? new Dictionary<string, string>())
                    .Select(kv => new { name = kv.Key, value = kv.Value ?? string.Empty })
                    .ToArray()
            };

            var payload = JsonSerializer.Serialize(payloadObj);
            HttpContent content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://api.sms.ir/v1/send/verify", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"SMS.ir verify failed ({(int)response.StatusCode}): {body}");
            return body;
        }

        public async Task<string> SendVerifyForFeatureAsync(
            string featureKey,
            string mobile,
            int templateId,
            string PRICE,
            string BABAT,
            string MANBAB,
            string MANPRICE,
            string NAME = "",
            CancellationToken ct = default)
        {
            await EnsureSmsEnabledAsync(featureKey);

            return await SendVerifyWithParametersAsync(
                mobile,
                templateId,
                new Dictionary<string, string>
                {
                    ["PRICE"] = PRICE,
                    ["BABAT"] = BABAT,
                    ["MANBAB"] = MANBAB,
                    ["MANPRICE"] = MANPRICE,
                    ["NAME"] = NAME
                },
                ct);
        }

        public async Task<string> SendVerifyWithParametersForFeatureAsync(
            string featureKey,
            string mobile,
            int templateId,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken ct = default)
        {
            await EnsureSmsEnabledAsync(featureKey);
            return await SendVerifyWithParametersAsync(mobile, templateId, parameters, ct);
        }

        private async Task EnsureSmsEnabledAsync(string? featureKey)
        {
            var settings = await _smsPanelRepository.GetSettingsAsync();
            if (!settings.IsEnabled)
                throw new InvalidOperationException("ارسال پیامک در پنل پیامک غیرفعال است.");

            if (string.IsNullOrWhiteSpace(featureKey))
                return;

            var features = await _smsPanelRepository.GetFeaturesAsync();
            var f = features.FirstOrDefault(x => string.Equals(x.FeatureKey, featureKey, StringComparison.OrdinalIgnoreCase));
            if (f == null || !f.IsEnabled)
                throw new InvalidOperationException("این قابلیت ارسال پیامک در پنل پیامک غیرفعال است.");
        }

        public async Task<SmsIrApiResponse<SmsIrMessageStatusData>?> GetMessageStatusAsync(long messageId, CancellationToken ct = default)
        {
            var apiKey = GetApiKey();
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Remove("x-api-key");
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

            var url = $"https://api.sms.ir/v1/send/{messageId}";
            var response = await httpClient.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"SMS.ir status failed ({(int)response.StatusCode}): {body}");

            return JsonSerializer.Deserialize<SmsIrApiResponse<SmsIrMessageStatusData>>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private string GetApiKey()
        {
            var apiKey = _configuration["SmsIr:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("SmsIr:ApiKey is not configured.");
            return apiKey;
        }

        public static string GetApiStatusMessage(int statusCode)
        {
            return statusCode switch
            {
                0 => "درخواست شما با خطا مواجه شده‌است.",
                1 => "عملیات با موفقیت انجام شد",
                10 => "کلید وب سرویس نامعتبر است",
                11 => "کلید وب سرویس غیرفعال است",
                12 => "کلید وب سرویس محدود به آی‌پی‌های تعریف شده می‌باشد.",
                13 => "حساب کاربری غیرفعال است",
                14 => "حساب کاربری در حالت تعلیق قرار دارد",
                15 => "به منظور استفاده از وب سرویس پلن خود را ارتقا دهید",
                16 => "مقدار ارسالی پارامتر نادرست می‌باشد",
                20 => "تعداد درخواست بیشتر از حد مجاز است",
                101 => "شماره خط نامعتبر میباشد",
                102 => "اعتبار کافی نمیباشد",
                103 => "درخواست شما دارای متن (های) خالی است",
                104 => "درخواست شما دارای موبایل (های) نادرست است",
                105 => "تعداد موبایل ها بیشتر از حد مجاز (100 عدد) میباشد",
                106 => "تعداد متن ها بیشتر از حد مجاز (100 عدد) میباشد",
                107 => "لیست موبایل ها خالی میباشد",
                108 => "لیست متن ها خالی میباشد",
                109 => "زمان ارسال نامعتبر میباشد",
                110 => "تعداد شماره موبایل ها و تعداد متن ها برابر نیستند",
                111 => "با این شناسه ارسالی ثبت نشده است",
                112 => "رکوردی برای حذف یافت نشد",
                113 => "قالب یافت نشد",
                114 => "طول رشته مقدار پارامتر، بیش از حد مجاز (25 کاراکتر) میباشد",
                115 => "شماره موبایل(ها) در لیست سیاه سامانه می‌باشند",
                116 => "نام یک یا چند پارامتر مقداردهی نشده‌است. لطفا به بخش مستندات ارسال وریفای مراجعه فرمایید",
                117 => "متن ارسال شده مورد تایید نمی‌باشد",
                118 => "تعداد پیام ها بیشتر از حد مجاز میباشد",
                119 => "به منظور استفاده از قالب‌ شخصی سازی شده پلن خود را ارتقا دهید",
                123 => "خط ارسال‌کننده نیاز به فعال‌سازی دارد.",
                124 => "درحال حاضر، فقط امکان ارسال پیامک OTP وجود دارد و قالب شما OTP شناسایی نشده است!",
                _ => $"وضعیت نامشخص ({statusCode})"
            };
        }

        public static string GetDeliveryStateMessage(byte? deliveryState)
        {
            if (deliveryState is null)
                return "نامشخص";

            return deliveryState.Value switch
            {
                1 => "رسیده",
                2 => "نرسیده به گوشی",
                3 => "رسیده به مخابرات",
                4 => "نرسیده به مخابرات",
                5 => "رسیده به اپراتور",
                6 => "ناموفق",
                7 => "لیست سیاه",
                8 => "نامشخص",
                _ => $"نامشخص ({deliveryState})"
            };
        }
    }

    public sealed class SmsIrApiResponse<T>
    {
        public int Status { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
    }

    public sealed class SmsIrMessageStatusData
    {
        public long MessageId { get; set; }
        public long Mobile { get; set; }
        public string? MessageText { get; set; }
        public long SendDateTime { get; set; }
        public long LineNumber { get; set; }
        public decimal Cost { get; set; }
        public byte? DeliveryState { get; set; }
        public long? DeliveryDateTime { get; set; }
    }
}

