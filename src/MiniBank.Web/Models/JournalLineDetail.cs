namespace MiniBank.Web.Models
{
    public class JournalLineDetail
    {
        public long JournalLineId { get; set; }
        public long JournalId { get; set; }
        public int? DocumentNumber { get; set; }
        public long AccountId { get; set; }
        public long? MemberId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public int LineNo { get; set; }
        public string? Description { get; set; }
        public long? LoanId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? MemberName { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public byte? DocumentType { get; set; }
        public string? TransactionTypeCode { get; set; }
        public byte? PayChannel { get; set; }
        public string? CounterpartyName { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
        public string? DepositTypeName { get; set; }
        public decimal? TransactionAmount { get; set; }
        public string? PostingTemplateName { get; set; }
        public string? CounterpartCode { get; set; }
        public string? CounterpartName { get; set; }
        
        // خصوصیات محاسباتی
        public decimal Amount => Debit > 0 ? Debit : Credit;
        public bool IsDebit => Debit > 0;
        public bool IsCredit => Credit > 0;
        
        // نمایش
        public string DisplayAmount => Amount.ToString("N0");
        public string DisplayDate => OccurredAt.ToString("yyyy/MM/dd HH:mm");
        public string DisplayMember => string.IsNullOrWhiteSpace(MemberName) ? "عمومی" : MemberName.Trim();
        public string DisplayDescription => !string.IsNullOrWhiteSpace(Description) ? Description : 
                                          (!string.IsNullOrWhiteSpace(Notes) ? Notes : "تراکنش عمومی");
        public string DisplayTitle => $"{TransactionType} {Amount:N0} ریال - {DisplayMember}";
        public string DisplayDetails => $"{DisplayDescription} - {DisplayDate}";
        public string DisplayPurpose => !string.IsNullOrWhiteSpace(DepositTypeName) ? DepositTypeName : "نوع سپرده نامشخص";
        public string DisplayTransactionInfo => $"{DisplayPurpose} - مبلغ کل: {TransactionAmount:N0} ریال";
        public string DisplayPostingTemplate => !string.IsNullOrWhiteSpace(PostingTemplateName) ? PostingTemplateName : "الگوی حسابداری نامشخص";
        public string DisplayCounterpart => !string.IsNullOrWhiteSpace(CounterpartName) ? $"طرف حساب: {CounterpartCode} - {CounterpartName}" : "طرف حساب نامشخص";
        public string DisplayFullTransaction => $"{DisplayPostingTemplate} | {DisplayCounterpart} | {DisplayTransactionInfo}";
        public string DisplaySummary => $"{TransactionType} {Amount:N0} ریال - {DisplayMember} - {DisplayDate}";
        
        // رنگ‌بندی
        public string AmountColor => IsDebit ? "text-success" : "text-danger";
        public string TypeBadgeClass => IsDebit ? "badge bg-success" : "badge bg-danger";
    }
}
