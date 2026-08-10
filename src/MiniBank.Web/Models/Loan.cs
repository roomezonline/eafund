namespace MiniBank.Web.Models
{
    public enum LoanStatus : byte
    {
        Draft = 1,
        Approved = 2,
        Disbursed = 3,
        Active = 4,
        Settled = 5,
        Cancelled = 6
    }

    public class Loan
    {
        public long LoanId { get; set; }
        public long MemberId { get; set; }
        public string? MemberCode { get; set; }
        public long LoanRatePlanId { get; set; }

        public decimal Principal { get; set; }
        public int TermMonths { get; set; }

        public DateTime DisbursedAt { get; set; }
        public DateTime FirstDueDate { get; set; }

        public decimal AnnualRatePercent { get; set; }
        public decimal PenaltyAnnualRatePercent { get; set; }

        public decimal EmiAmount { get; set; }
        public decimal OutstandingPrincipal { get; set; }

        public LoanStatus Status { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
