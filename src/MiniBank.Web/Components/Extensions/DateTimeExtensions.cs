using System.Globalization;
using System.Runtime.CompilerServices;

namespace MiniBank.Web.Components.Extensions
{
    public static class DateTimeExtensions
    {
        private static readonly PersianCalendar PersianCalendar = new PersianCalendar();
        private static string? _logConnectionString;

        public static (string From, string To) CurrentJalaliYearRange(DateTime? baseDate = null)
        {
            var today = (baseDate ?? DateTime.Today).Date;
            var y = PersianCalendar.GetYear(today);
            var endDay = PersianCalendar.GetDaysInMonth(y, 12);
            return ($"{y:0000}/01/01", $"{y:0000}/12/{endDay:00}");
        }

        public static string CurrentJalaliYearStart(DateTime? baseDate = null)
            => CurrentJalaliYearRange(baseDate).From;

        public static string CurrentJalaliYearEnd(DateTime? baseDate = null)
            => CurrentJalaliYearRange(baseDate).To;

        public static (string From, string To) FiscalYearRange(DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue)
                return (startDate.Value.ToPersianDate(), endDate.Value.ToPersianDate());
            return CurrentJalaliYearRange();
        }

        public static DateTime AddPersianMonths(this DateTime dateTime, int months)
        {
            if (dateTime == default) return default;
            return PersianCalendar.AddMonths(dateTime, months);
        }

        public static string ToPersianDate(this DateTime dateTime)
        {
            if (dateTime == default)
                return string.Empty;

            var year = PersianCalendar.GetYear(dateTime);
            var month = PersianCalendar.GetMonth(dateTime);
            var day = PersianCalendar.GetDayOfMonth(dateTime);

            return $"{year:0000}/{month:00}/{day:00}";
        }

        public static string ToPersianDateTime(this DateTime dateTime)
        {
            if (dateTime == default)
                return string.Empty;

            var persianDate = dateTime.ToPersianDate();
            var time = dateTime.ToString("HH:mm");

            return $"{persianDate} - {time}";
        }

        public static string NormalizeDigits(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
                .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
                .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        }

        public static void ConfigureLogging(string connectionString)
        {
            _logConnectionString = connectionString;
        }

        public static DateTime FromPersianDate(string persianDate, [CallerFilePath] string callerFile = "", [CallerMemberName] string callerMember = "")
        {
            if (string.IsNullOrWhiteSpace(persianDate))
                return DateTime.Now;

            var normalized = NormalizeDigits(persianDate.Trim());
            var parts = normalized.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 3)
                return DateTime.Now;

            if (int.TryParse(parts[0], out var year) &&
                int.TryParse(parts[1], out var month) &&
                int.TryParse(parts[2], out var day))
            {
                try
                {
                    var dt = PersianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);

                    // PersianCalendar has a known off-by-one bug on certain dates.
                    // Validate roundtrip and correct if needed.
                    if (PersianCalendar.GetYear(dt) != year ||
                        PersianCalendar.GetMonth(dt) != month ||
                        PersianCalendar.GetDayOfMonth(dt) != day)
                    {
                        dt = dt.AddDays(1);
                    }

                    LogConversion(persianDate, dt, callerFile, callerMember);

                    return dt;
                }
                catch
                {
                    return DateTime.Now;
                }
            }

            return DateTime.Now;
        }

        private static void LogConversion(string jalali, DateTime greg, string file, string member)
        {
            if (_logConnectionString == null) return;
            try
            {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_logConnectionString);
                conn.Open();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                    "INSERT INTO DateConversionLog (JalaliInput, GregorianOutput, CallerFile, CallerMember) VALUES (@j, @g, @f, @m)", conn);
                cmd.Parameters.AddWithValue("@j", jalali);
                cmd.Parameters.AddWithValue("@g", greg);
                cmd.Parameters.AddWithValue("@f", file);
                cmd.Parameters.AddWithValue("@m", member);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // logging failure must not affect the caller
            }
        }

        public static bool TryParseJalaliDate(string? input, out DateTime result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(input)) return false;
            try
            {
                result = FromPersianDate(input);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Returns a long Persian date like: یکشنبه 2 فروردین 1404
        public static string ToPersianDateFull(this DateTime dateTime)
        {
            if (dateTime == default)
                return string.Empty;

            var year = PersianCalendar.GetYear(dateTime);
            var month = PersianCalendar.GetMonth(dateTime);
            var day = PersianCalendar.GetDayOfMonth(dateTime);
            var dow = PersianCalendar.GetDayOfWeek(dateTime);

            var dayOfWeekFa = dow switch
            {
                DayOfWeek.Saturday => "شنبه",
                DayOfWeek.Sunday => "یکشنبه",
                DayOfWeek.Monday => "دوشنبه",
                DayOfWeek.Tuesday => "سه‌شنبه",
                DayOfWeek.Wednesday => "چهارشنبه",
                DayOfWeek.Thursday => "پنجشنبه",
                DayOfWeek.Friday => "جمعه",
                _ => string.Empty
            };

            var monthNameFa = month switch
            {
                1 => "فروردین",
                2 => "اردیبهشت",
                3 => "خرداد",
                4 => "تیر",
                5 => "مرداد",
                6 => "شهریور",
                7 => "مهر",
                8 => "آبان",
                9 => "آذر",
                10 => "دی",
                11 => "بهمن",
                12 => "اسفند",
                _ => string.Empty
            };

            return $"{dayOfWeekFa} {day} {monthNameFa} {year}";
        }
    }
}
