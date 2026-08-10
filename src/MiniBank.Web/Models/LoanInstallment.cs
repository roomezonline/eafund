namespace MiniBank.Web.Models
{
    public enum LoanInstallmentStatus : byte
    {
        Unpaid = 1,
        PartiallyPaid = 2,
        Paid = 3
    }

    public class LoanInstallment
    {
        public long InstallmentId { get; set; }
        public long LoanId { get; set; }
        public int InstallmentNo { get; set; }
        public DateTime DueDate { get; set; }

        public decimal OpeningBalance { get; set; }
        public decimal EmiAmount { get; set; }
        public decimal InterestAmount { get; set; }
        public decimal PrincipalAmount { get; set; }
        public decimal ClosingBalance { get; set; }

        public decimal PaidPrincipal { get; set; }
        public decimal PaidInterest { get; set; }
        public decimal PaidPenalty { get; set; }

        public LoanInstallmentStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
