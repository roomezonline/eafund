using MiniBank.Web.Components.Extensions;

namespace MiniBank.Web.Services
{
    /// <summary>
    /// بازه تقویمی «سال مالی فعال» (شمسی yyyy/MM/dd) که همه فیلترها و
    /// دیتاپیکرهای گزارش‌ها به آن محدود می‌شوند.
    ///
    /// سال مالی فعال = سالِ انتخابیِ کاربر (FiscalYearContext) و در نبودِ آن،
    /// سالِ بازِ تاریخ امروز. این سرویس هیچ کشِ مستقلی ندارد تا با عوض شدن
    /// انتخاب، بلافاصله بازه‌ی جدید اعمال شود.
    /// </summary>
    public class FiscalYearRangeProvider
    {
        private readonly FiscalYearContext _context;

        public FiscalYearRangeProvider(FiscalYearContext context)
        {
            _context = context;
        }

        public async Task<(string From, string To)> GetRangeAsync()
        {
            var fy = await _context.GetActiveAsync();
            if (fy != null)
                return (fy.StartDate.ToPersianDate(), fy.EndDate.ToPersianDate());

            return DateTimeExtensions.CurrentJalaliYearRange();
        }

        public async Task<(DateTime Start, DateTime End)> GetGregorianRangeAsync()
        {
            var fy = await _context.GetActiveAsync();
            if (fy != null)
                return (fy.StartDate, fy.EndDate);

            var (from, to) = DateTimeExtensions.CurrentJalaliYearRange();
            return (DateTimeExtensions.FromPersianDate(from), DateTimeExtensions.FromPersianDate(to));
        }

        /// <summary>تاریخ شمسی داده‌شده را داخل بازه سال مالی فعال فشار می‌دهد.</summary>
        public async Task<string> ClampAsync(string? jalaliDate)
        {
            var (from, to) = await GetRangeAsync();
            if (string.IsNullOrWhiteSpace(jalaliDate))
                return to;

            var value = DateTimeExtensions.FromPersianDate(jalaliDate);
            if (value < DateTimeExtensions.FromPersianDate(from)) return from;
            if (value > DateTimeExtensions.FromPersianDate(to)) return to;
            return jalaliDate;
        }

        public void Invalidate() => _context.Invalidate();
    }
}
