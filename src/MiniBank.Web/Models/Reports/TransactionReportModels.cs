namespace MiniBank.Web.Models.Reports
{
    /// <summary>
    /// مدل جزئیات تراز حساب‌های اعضا
    /// </summary>
    public class MemberBalanceDetailRow
    {
        public long MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public long HouseholdId { get; set; }
        public string HouseholdCode { get; set; } = string.Empty;
        public string HouseholdName { get; set; } = string.Empty;
        public long AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public int AccountLevel { get; set; }
        public long JournalLineId { get; set; }
        public long JournalId { get; set; }
        public long TransactionId { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal TransactionAmount { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal NetAmount { get; set; }
        public string PostingTemplate { get; set; } = string.Empty;
        public string TransactionType { get; set; } = string.Empty;
        public string TransactionNotes { get; set; } = string.Empty;
        public decimal RunningBalance { get; set; }
    }

    /// <summary>
    /// مدل خلاصه موجودی حساب‌های اعضا
    /// </summary>
    public class MemberBalanceSummaryRow
    {
        public long MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public long HouseholdId { get; set; }
        public string HouseholdCode { get; set; } = string.Empty;
        public string HouseholdName { get; set; } = string.Empty;
        public long AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public int AccountLevel { get; set; }
        public string AccountLevelName { get; set; } = string.Empty;
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal Balance { get; set; }
        public int TransactionCount { get; set; }
        public DateTime? FirstTransactionDate { get; set; }
        public DateTime? LastTransactionDate { get; set; }
    }

    /// <summary>
    /// مدل گزارش ردیابی مسیر تراکنش
    /// </summary>
    public class TransactionTrailReport
    {
        public long TransactionId { get; set; }
        public TransactionTrailInfo? Transaction { get; set; }
        public List<TransactionJournalLineInfo> JournalLines { get; set; } = new();
        public List<ProcessLogInfo> ProcessLogs { get; set; } = new();
    }

    /// <summary>
    /// اطلاعات اصلی تراکنش برای ردیابی
    /// </summary>
    public class TransactionTrailInfo
    {
        public long TransactionId { get; set; }
        public int TypeId { get; set; }
        public long? HouseholdId { get; set; }
        public long? MemberId { get; set; }
        public decimal AmountTotal { get; set; }
        public decimal? AmountPrincipal { get; set; }
        public decimal? AmountInterest { get; set; }
        public decimal? AmountFee { get; set; }
        public decimal? AmountTax { get; set; }
        public decimal? AmountPenalty { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public int Status { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string? ProcessId { get; set; }
        public int? PostingTemplateId { get; set; }
        public long? ParentReceiptId { get; set; }
        public long? AllocationId { get; set; }
        public string HouseholdCode { get; set; } = string.Empty;
        public string HouseholdName { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public string PostingTemplateName { get; set; } = string.Empty;
        public long? HouseholdReceiptId { get; set; }
        public decimal? ReceiptAmount { get; set; }
        public string TransactionTypeName { get; set; } = string.Empty;
    }

    /// <summary>
    /// اطلاعات ثبت‌های حسابداری برای ردیابی تراکنش
    /// </summary>
    public class TransactionJournalLineInfo
    {
        public long JournalLineId { get; set; }
        public long JournalId { get; set; }
        public int LineNo { get; set; }
        public long AccountId { get; set; }
        public long? MemberId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public int AccountLevel { get; set; }
        public string AccountLevelName { get; set; } = string.Empty;
        public string? MemberName { get; set; }
        public string? MemberNationalCode { get; set; }
    }

    /// <summary>
    /// اطلاعات لاگ پردازش
    /// </summary>
    public class ProcessLogInfo
    {
        public long LogId { get; set; }
        public string ProcessId { get; set; } = string.Empty;
        public string Step { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// مدل تأثیر تراکنش‌ها بر حساب‌های مختلف
    /// </summary>
    public class TransactionImpactRow
    {
        public long AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public int AccountLevel { get; set; }
        public int TransactionCount { get; set; }
        public int JournalLineCount { get; set; }
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal NetImpact { get; set; }
        public int AffectedMemberCount { get; set; }
        public DateTime? FirstTransactionDate { get; set; }
        public DateTime? LastTransactionDate { get; set; }
        public decimal DepositImpact { get; set; }
        public decimal LoanPaymentImpact { get; set; }
        public decimal InterestImpact { get; set; }
        public decimal PenaltyImpact { get; set; }
        public decimal FeeImpact { get; set; }
    }

    public class MemberTransactionRow
    {
        public long TransactionId { get; set; }
        public DateTime OccurredAt { get; set; }
        public long TypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public decimal AmountTotal { get; set; }
        public decimal? AmountPrincipal { get; set; }
        public decimal? AmountInterest { get; set; }
        public decimal? AmountPenalty { get; set; }
        public decimal? AmountFee { get; set; }
        public decimal? AmountTax { get; set; }
        public string? Notes { get; set; }
    }

    public class MemberSubsidiaryJournalRow
    {
        public long JournalId { get; set; }
        public DateTime OccurredAt { get; set; }
        public string SubsidiaryCode { get; set; } = string.Empty;
        public string SubsidiaryName { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Net { get; set; }
        public string? Notes { get; set; }
        public int? DocumentType { get; set; }
        public string? TransactionType { get; set; }
    }

    public class MemberSimpleTransactionRow
    {
        public long? TransactionId { get; set; }
        public long? JournalId { get; set; }
        public DateTime OccurredAt { get; set; }
        public long TypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Net { get; set; }
        public string? Operation { get; set; }
        public string MemberCode { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Notes { get; set; }
    }
}
