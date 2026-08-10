using System;
using System.Globalization;

namespace MiniBank.Web.Utilities
{
    public static class DateExtensions
    {
        private static CultureInfo? _persianCulture;

        private static CultureInfo PersianCulture
        {
            get
            {
                if (_persianCulture == null)
                {
                    var ci = new CultureInfo("fa-IR");
                    ci.DateTimeFormat.Calendar = new PersianCalendar();
                    // Ensure digits and AM/PM are Farsi localized
                    ci.DateTimeFormat.AMDesignator = "ق.ظ";
                    ci.DateTimeFormat.PMDesignator = "ب.ظ";
                    _persianCulture = ci;
                }
                return _persianCulture;
            }
        }

        public static string ToShamsiDateString(this DateTime dt)
        {
            if (dt == default) return string.Empty;
            // Use Persian calendar for formatting
            return dt.ToString("yyyy/MM/dd", PersianCulture);
        }

        public static string ToShamsiDateTimeString(this DateTime dt)
        {
            if (dt == default) return string.Empty;
            return dt.ToString("yyyy/MM/dd HH:mm", PersianCulture);
        }

        public static string ToShamsiLongDateString(this DateTime dt)
        {
            if (dt == default) return string.Empty;
            // Example: دوشنبه 20 شهریور 1404
            return dt.ToString("dddd d MMMM yyyy", PersianCulture);
        }

        public static string ToShamsiShortDateString(this DateTime dt)
        {
            if (dt == default) return string.Empty;
            // Example: 20/06/1404
            return dt.ToString("dd/MM/yyyy", PersianCulture);
        }

        public static string ToShamsiDateString(this DateTimeOffset dto)
        {
            if (dto == default) return string.Empty;
            return dto.ToLocalTime().DateTime.ToString("yyyy/MM/dd", PersianCulture);
        }

        public static string ToShamsiDateTimeString(this DateTimeOffset dto)
        {
            if (dto == default) return string.Empty;
            return dto.ToLocalTime().DateTime.ToString("yyyy/MM/dd HH:mm", PersianCulture);
        }

        public static string ToShamsiLongDateString(this DateTimeOffset dto)
        {
            if (dto == default) return string.Empty;
            return dto.ToLocalTime().DateTime.ToString("dddd d MMMM yyyy", PersianCulture);
        }

        public static string ToShamsiShortDateString(this DateTimeOffset dto)
        {
            if (dto == default) return string.Empty;
            return dto.ToLocalTime().DateTime.ToString("dd/MM/yyyy", PersianCulture);
        }

        public static CultureInfo GetPersianCulture()
        {
            // Expose the culture instance for components like MudDatePicker
            return PersianCulture;
        }
    }
}
