using System.Globalization;

namespace MiniBank.Web.Utilities
{
    /// <summary>
    /// تنها مرجعِ «سند انتقال (افتتاحیه)».
    /// هر جایی که باید سند انتقالِ بین دو سال مالی را بسازد یا از محاسبات کنار بگذارد،
    /// باید از همین کلاس استفاده شود تا الگوی Notes و متنِ ساخته‌شده هرگز از هم فاصله نگیرند.
    /// (علت وجود این کلاس: کنار گذاشتن دستیِ سند افتتاحیه در چند جا با متنِ کپی‌شده)
    /// </summary>
    public static class OpeningJournal
    {
        /// <summary>قالبِ Notes سند انتقال. {0} = سال مبدأ، {1} = سال مقصد.</summary>
        public const string NotesFormat = "انتقال از سال {0} - افتتاحیه حساب در سال {1}";

        /// <summary>الگوی LIKE عمومی که همهٔ اسناد انتقال را پوشش می‌دهد (درصد = هر سال).</summary>
        public static readonly string NotesPattern =
            NotesFormat.Replace("{0}", "%").Replace("{1}", "%");

        /// <summary>متن Notes سند انتقال از سال <paramref name="sourceYearCode"/> به سال <paramref name="targetYearCode"/>.</summary>
        public static string Build(int sourceYearCode, int targetYearCode)
            => string.Format(
                CultureInfo.InvariantCulture,
                NotesFormat,
                sourceYearCode,
                targetYearCode);

        /// <summary>الگوی LIKE برای سندِ انتقالِ منتهی به سالِ مشخص.</summary>
        public static string PatternFor(int targetYearCode)
            => NotesFormat
                .Replace("{0}", "%")
                .Replace("{1}", targetYearCode.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// قطعهٔ SQL برای رد کردن سند انتقال در کوئری‌های مانده/گزارش.
        /// حتماً از ISNULL استفاده می‌شود چون NOT LIKE روی NULL نتیجه NULL می‌دهد و ردیف حذف می‌شود.
        /// </summary>
        public static string ExclusionSql(string alias = "j")
            => $"ISNULL({alias}.Notes, N'') NOT LIKE N'{NotesPattern}'";
    }
}
