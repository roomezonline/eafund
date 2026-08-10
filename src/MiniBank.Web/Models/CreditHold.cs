namespace MiniBank.Web.Models
{
    public enum CreditHoldStatus : byte
    {
        Active = 1,
        Released = 2,
        Cancelled = 3
    }

    public enum CreditHoldRole : byte
    {
        Applicant = 1,
        Guarantor = 2
    }

    public class CreditHold
    {
        public long HoldId { get; set; }
        public long LoanId { get; set; }
        public long MemberId { get; set; }
        public string? MemberCode { get; set; }
        public CreditHoldRole Role { get; set; }
        public byte LockPercent { get; set; }
        public decimal BaseCapitalAmount { get; set; }
        public decimal LockedAmount { get; set; }
        public CreditHoldStatus Status { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReleasedAt { get; set; }
    }
}
