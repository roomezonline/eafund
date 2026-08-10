namespace MiniBank.Web.Models
{
    public static class DepositEnumDisplayExtensions
    {
        public static string ToFa(this AccrualBasis value) => value switch
        {
            AccrualBasis.Daily => "روزانه",
            AccrualBasis.Monthly => "ماهانه",
            AccrualBasis.Annual => "سالانه",
            _ => value.ToString()
        };

        public static string ToFa(this PostingFrequency value) => value switch
        {
            PostingFrequency.Daily => "روزانه",
            PostingFrequency.Monthly => "ماهانه",
            PostingFrequency.Quarterly => "سه‌ماهه",
            PostingFrequency.Annual => "سالانه",
            PostingFrequency.OnMaturity => "در سررسید",
            _ => value.ToString()
        };

        public static string ToFa(this Compounding value) => value switch
        {
            Compounding.None => "بدون مرکب",
            Compounding.Daily => "روزانه",
            Compounding.Monthly => "ماهانه",
            Compounding.Quarterly => "سه‌ماهه",
            Compounding.Annual => "سالانه",
            _ => value.ToString()
        };

        public static string ToFa(this PostingTarget value) => value switch
        {
            PostingTarget.SameAccount => "همان حساب",
            PostingTarget.SupportAccount => "حساب پشتیبان",
            _ => value.ToString()
        };

        public static string ToFa(this DayCountConvention value) => value switch
        {
            DayCountConvention.Actual360 => "Actual/360",
            DayCountConvention.Actual365 => "Actual/365",
            DayCountConvention.ActualActual => "Actual/Actual",
            DayCountConvention.ThirtyE360 => "30E/360",
            _ => value.ToString()
        };

        public static string ToFa(this RoundingRule value) => value switch
        {
            RoundingRule.None => "بدون گرد کردن",
            RoundingRule.HalfUp => "نزدیک‌ترین رو به بالا (HalfUp)",
            RoundingRule.HalfEven => "بانکی (HalfEven)",
            _ => value.ToString()
        };
    }
}
