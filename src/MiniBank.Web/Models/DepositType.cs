namespace MiniBank.Web.Models
{
    public class DepositType
    {
        public long DepositTypeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Category { get; set; } // short-term, long-term, qard-al-hasan, ...
        public decimal? BreakRate { get; set; } // نرخ شکست
        public string? InterestCalcPeriod { get; set; } // Deprecated: free-text period (kept for backward compatibility)

        // Standardized behavior fields (nullable for backward compatibility)
        public AccrualBasis? AccrualBasis { get; set; }
        public PostingFrequency? PostingFrequency { get; set; }
        public Compounding? Compounding { get; set; }
        public PostingTarget? PostingTarget { get; set; }
        public DayCountConvention? DayCountConvention { get; set; }
        public RoundingRule? RoundingRule { get; set; }

        // Numeric policies
        public decimal? MinBalanceForInterest { get; set; }
        public bool EligibleForLoan { get; set; } = false; // مشمول دریافت وام
        public bool IsActive { get; set; } = true;
        public bool HasInterest { get; set; } = true; // حساب سودده یا بدون سود
        // Permissions and visibility
        public bool CanGuaranteeLoan { get; set; } = true; // مجوز ضمانت وام
        public bool ShowInTransactions { get; set; } = true; // نمایش تراکنش‌های این حساب در لیست تراکنش‌ها
        public bool IncludeInTotalBalance { get; set; } = true; // لحاظ در جمع موجودی کل صندوق
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
