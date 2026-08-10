namespace MiniBank.Web.Models
{
    public enum LoanGuaranteeType : byte
    {
        GuarantorMember = 1,
        DepositHold = 2,
        Cheque = 3,
        Promissory = 4
    }

    public class LoanGuarantee
    {
        public long GuaranteeId { get; set; }
        public long LoanId { get; set; }
        public LoanGuaranteeType Type { get; set; }
        public long? GuarantorMemberId { get; set; }
        public string? GuarantorMemberCode { get; set; }
        public long? DepositAccountId { get; set; }
        public decimal Amount { get; set; }
        public bool IsActive { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
