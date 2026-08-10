namespace MiniBank.Web.Models
{
    public sealed class DepositInterestRun
    {
        public long RunId { get; set; }
        public DateTime PeriodStartDate { get; set; }
        public DateTime PostingOccurredAt { get; set; }

        public decimal AnnualRatePercent { get; set; }
        public int DaysInMonth { get; set; }

        public DateTime CreditsCutoffToExclusive { get; set; }
        public DateTime DebitsCutoffToExclusive { get; set; }

        public decimal SumPrincipal { get; set; }
        public decimal SumInterest { get; set; }
        public decimal SumNet { get; set; }
        public decimal SumProfit { get; set; }

        public int MemberCount { get; set; }
        public int NeutralCount { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }
}
