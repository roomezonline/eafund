namespace MiniBank.Web.Models
{
    public class MemberDepositAccount
    {
        public long MemberDepositAccountId { get; set; }
        public long MemberId { get; set; }
        public long DepositTypeId { get; set; }
        public string? AccountNo { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public bool IsActive { get; set; }
        public string? Notes { get; set; }
        // Convenience
        public string? DepositTypeTitle { get; set; }
    }
}
