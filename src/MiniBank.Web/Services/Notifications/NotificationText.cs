using System.Globalization;
using System.Text.RegularExpressions;

namespace MiniBank.Web.Services.Notifications;

public static class NotificationText
{
    public static string NormalizeName(string? fullName)
    {
        var name = (fullName ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;

        var starIndex = name.IndexOf('☆');
        if (starIndex < 0)
            starIndex = name.IndexOf('★');
        if (starIndex < 0)
            starIndex = name.IndexOf('*');

        if (starIndex >= 0)
            name = name[..starIndex].Trim();

        return Regex.Replace(name, @"\s+", " ");
    }

    public static string FormatJalaliDate(DateTime value)
    {
        var local = value.ToLocalTime();
        var pc = new PersianCalendar();
        var y = pc.GetYear(local);
        var m = pc.GetMonth(local);
        var d = pc.GetDayOfMonth(local);
        return $"{y:0000}/{m:00}/{d:00}";
    }

    public static string BuildLoanReminderMessage(string memberName, string amount, string dueDate)
    {
        var name = NormalizeName(memberName);
        return $"عضومحترم {name}\n" +
               $"لطفا نسبت به پرداخت قسط خود به مبلغ {amount} ریال\n" +
               $"در سررسید {dueDate} اقدام نمایید.\n" +
               "صندوق همیاری امام علی (ع)";
    }

    public static string BuildLoanReminderBaleMessage(string memberName, string? fatherName, string amount, string dueDate)
    {
        var name = NormalizeName(memberName);
        if (!string.IsNullOrWhiteSpace(fatherName) && !name.Contains('☆'))
            name = $"{name}☆{fatherName.Trim()}";

        var person = string.IsNullOrWhiteSpace(name) ? "عضو محترم" : $"آقای {name}";
        return "عضو محترم صندوق امام علی (ع)\n" +
               $"{person}\n" +
               "با سلام\n" +
               $"لطفا نسبت به پرداخت قسط وام خود به مبلغ {amount} ریال در تاریخ سررسید {dueDate} اقدام نمایید.\n" +
               "صندوق امام علی (ع)";
    }
}
