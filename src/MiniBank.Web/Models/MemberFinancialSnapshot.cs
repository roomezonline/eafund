namespace MiniBank.Web.Models
{
    public sealed class MemberFinancialSnapshot
    {
        public sealed class LockedItem
        {
            public long HoldId { get; set; }
            public long LoanId { get; set; }
            public CreditHoldRole Role { get; set; }
            public decimal LockedAmount { get; set; }
            public long BorrowerMemberId { get; set; }
            public string BorrowerName { get; set; } = string.Empty;
        }

        public sealed class ActiveLoanSnapshot
        {
            public long LoanId { get; set; }
            public DateTime DisbursedAt { get; set; }
            public decimal Principal { get; set; }
            public decimal TotalInterest { get; set; }
            public decimal TotalPayable { get; set; }

            public decimal RemainingPrincipal { get; set; }
            public decimal RemainingInterest { get; set; }
            public decimal RemainingTotal => RemainingPrincipal + RemainingInterest;

            public int TermMonths { get; set; }
            public decimal EmiAmount { get; set; }

            public int TotalInstallments { get; set; }
            public int PaidInstallments { get; set; }
            public int RemainingInstallments { get; set; }

            public DateTime? FirstDueDate { get; set; }
            public DateTime? LastDueDate { get; set; }
            public DateTime? FirstUnpaidDueDate { get; set; }

            public int? CurrentInstallmentNo { get; set; }
            public DateTime? CurrentInstallmentDueDate { get; set; }
            public decimal CurrentInstallmentPrincipal { get; set; }
            public decimal CurrentInstallmentInterest { get; set; }
            public decimal CurrentInstallmentTotal { get; set; }

            public decimal CurrentInstallmentRemainingPrincipal { get; set; }
            public decimal CurrentInstallmentRemainingInterest { get; set; }
            public decimal CurrentInstallmentRemainingTotal => CurrentInstallmentRemainingPrincipal + CurrentInstallmentRemainingInterest;

            public int CurrentInstallmentOverdueDays { get; set; }
            public decimal PenaltyDailyRate { get; set; }
            public decimal CurrentInstallmentPenaltyDailyAmount { get; set; }
            public decimal CurrentInstallmentPenaltyAmount { get; set; }

            public decimal CurrentInstallmentAchievedPrincipal { get; set; }
            public decimal CurrentInstallmentAchievedInterest { get; set; }
            public decimal CurrentInstallmentAchievedTotal => CurrentInstallmentAchievedPrincipal + CurrentInstallmentAchievedInterest;

            public DateTime? LastPaidInstallmentDueDate { get; set; }
        }

        public string MemberCode { get; set; } = string.Empty;

        public decimal NetPrincipal { get; set; }
        public decimal PrincipalWithdrawals { get; set; }

        public decimal NetInterest { get; set; }
        public decimal InterestWithdrawals { get; set; }

        public decimal LockedAmount { get; set; }

        public List<LockedItem> LockedItems { get; set; } = new();

        public ActiveLoanSnapshot? ActiveLoan { get; set; }

        public decimal TotalNet => NetPrincipal + NetInterest;
        public decimal AvailableCapital => TotalNet - LockedAmount;
    }
}
