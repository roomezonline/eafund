namespace MiniBank.Web.Models
{
    // استانداردهای رفتاری سپرده‌ها (nullable در مدل استفاده می‌شوند)
    public enum AccrualBasis
    {
        Daily = 1,
        Monthly = 2,
        Annual = 3
    }

    public enum PostingFrequency
    {
        Daily = 1,
        Monthly = 2,
        Quarterly = 3,
        Annual = 4,
        OnMaturity = 5
    }

    public enum Compounding
    {
        None = 0,
        Daily = 1,
        Monthly = 2,
        Quarterly = 3,
        Annual = 4
    }

    public enum PostingTarget
    {
        SameAccount = 1,
        SupportAccount = 2
    }

    public enum DayCountConvention
    {
        Actual360 = 1,
        Actual365 = 2,
        ActualActual = 3,
        ThirtyE360 = 4
    }

    public enum RoundingRule
    {
        None = 0,
        HalfUp = 1,
        HalfEven = 2
    }
}
