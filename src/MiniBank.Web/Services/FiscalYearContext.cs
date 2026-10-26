using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models;

namespace MiniBank.Web.Services
{
    /// <summary>
    /// سال مالیِ «انتخاب‌شده» توسط کاربر در کل سامانه.
    ///
    /// - مقدار در کوکی mb.fy نگهداری می‌شود (بعد از F5 / تب جدید / ناوبری حفظ می‌شود).
    /// - اگر چیزی انتخاب نشده باشد، سالِ بازِ تاریخ امروز مبنا قرار می‌گیرد.
    /// - فقط برای «خواندن/گزارش» استفاده می‌شود؛ مسیرهای ثبت سند (مهر زدن FiscalYearId،
    ///   شماره‌گذاری، اختتامیه/افتتاحیه) نباید از این سرویس استفاده کنند.
    ///
    /// نکته: اپ با ServerPrerendered بالا می‌آید، پس در رندر اولیه JSInterop در دسترس نیست؛
    /// بنابراین خواندن کوکی اول از HttpContext انجام می‌شود و فقط در حالت تعاملی از JS.
    /// </summary>
    public class FiscalYearContext
    {
        public const string CookieName = "mb.fy";
        private const int CookieDays = 3650;

        private readonly FiscalYearRepository _repo;
        private readonly IJSRuntime _js;
        private readonly IHttpContextAccessor _http;

        private readonly object _gate = new();
        private Task? _loadTask;
        private FiscalYear? _selected;
        private FiscalYear? _openToday;
        private bool _loaded;

        public FiscalYearContext(FiscalYearRepository repo, IJSRuntime js, IHttpContextAccessor http)
        {
            _repo = repo;
            _js = js;
            _http = http;
        }

        /// <summary>سال انتخابی کاربر؛ اگر چیزی انتخاب نشده null است.</summary>
        public FiscalYear? Selected => _selected;

        /// <summary>آیا انتخابِ فعلی با سالِ جاری (سالِ بازِ امروز) یکی است؟</summary>
        public bool IsCurrent => (_selected == null && _openToday == null)
                                 || (_selected != null && _openToday != null && _selected.FiscalYearId == _openToday.FiscalYearId);

        public event Action? OnChange;

        /// <summary>بارگذاری تنبل و یک‌باره؛ همه‌ی مصرف‌کنندگان همین Task را await می‌کنند.</summary>
        public Task EnsureLoadedAsync()
        {
            // قفل لازم است: بدون آن Invalidate() می‌تواند بین ??= و return
            // مقدار _loadTask را null کند و caller با await null → NullReferenceException بشکند.
            lock (_gate)
            {
                _loadTask ??= LoadAsync();
                return _loadTask;
            }
        }

        private async Task LoadAsync()
        {
            try
            {
                var code = await ReadCookieAsync();
                if (int.TryParse(code, out var yearCode) && yearCode > 0)
                    _selected = await _repo.GetByYearCodeAsync(yearCode);

                _openToday = await _repo.GetOpenYearForDateAsync(DateTime.Today);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FiscalYearContext load failed: {ex.Message}");
            }
            finally
            {
                _loaded = true;
            }
        }

        private async Task<string> ReadCookieAsync()
        {
            // Prerender: HttpContext موجود است و JSInterop پشتیبانی نمی‌شود
            var http = _http.HttpContext;
            if (http != null)
                return http.Request.Cookies[CookieName] ?? string.Empty;

            try
            {
                return await _js.InvokeAsync<string>("mbGetCookie", CookieName) ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"mbGetCookie failed: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>سالی که باید داده‌ها بر اساس آن نمایش داده شوند: انتخابی، وگرنه سالِ بازِ امروز.</summary>
        public async Task<FiscalYear?> GetActiveAsync()
        {
            await EnsureLoadedAsync();
            return _selected ?? _openToday;
        }

        /// <summary>سالِ بازِ تاریخ امروز (سال جاری).</summary>
        public async Task<FiscalYear?> GetOpenTodayAsync()
        {
            await EnsureLoadedAsync();
            return _openToday;
        }

        public async Task SetAsync(int yearCode)
        {
            var fy = await _repo.GetByYearCodeAsync(yearCode)
                     ?? throw new InvalidOperationException($"سال مالی {yearCode} یافت نشد");

            await WriteCookieAsync(yearCode.ToString());
            _selected = fy;
            _loaded = true;
            OnChange?.Invoke();
        }

        public async Task ResetAsync()
        {
            await WriteCookieAsync(string.Empty);
            _selected = null;
            _loaded = true;
            OnChange?.Invoke();
        }

        private async Task WriteCookieAsync(string value)
        {
            var http = _http.HttpContext;
            if (http != null && http.Response.HasStarted == false)
            {
                if (string.IsNullOrEmpty(value))
                    http.Response.Cookies.Delete(CookieName);
                else
                    http.Response.Cookies.Append(CookieName, value, new CookieOptions
                    {
                        Path = "/",
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddDays(CookieDays)
                    });
            }

            try
            {
                await _js.InvokeVoidAsync("mbSetCookie", CookieName, value, CookieDays);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"mbSetCookie failed: {ex.Message}");
            }
        }

        /// <summary>وقتی سال مالی ساخته/ویرایش/حذف/بسته/باز شد صدا بزنید.</summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _loadTask = null;
                _selected = null;
                _openToday = null;
                _loaded = false;
            }

            OnChange?.Invoke();
        }

        public bool IsLoaded => _loaded;
    }
}
