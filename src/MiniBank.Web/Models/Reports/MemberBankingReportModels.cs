using System.ComponentModel.DataAnnotations;

namespace MiniBank.Web.Models.Reports;

/// <summary>
/// گزارش موجودی بانکی عضو (نمایش مثبت)
/// </summary>
public class MemberBankingBalanceRow
{
    public long MemberId { get; set; }
    
    [Display(Name = "نام عضو")]
    public string MemberName { get; set; } = string.Empty;
    
    [Display(Name = "کد ملی")]
    public string NationalCode { get; set; } = string.Empty;
    
    [Display(Name = "کد خانوار")]
    public string HouseholdCode { get; set; } = string.Empty;
    
    [Display(Name = "نام خانوار")]
    public string HouseholdName { get; set; } = string.Empty;
    
    [Display(Name = "موجودی اصل سپرده")]
    public decimal PrincipalBalance { get; set; }
    
    [Display(Name = "موجودی سود")]
    public decimal InterestBalance { get; set; }
    
    [Display(Name = "کل موجودی")]
    public decimal TotalBalance { get; set; }
    
    [Display(Name = "تعداد تراکنش")]
    public int TransactionCount { get; set; }
    
    [Display(Name = "آخرین تراکنش")]
    public DateTime? LastTransactionDate { get; set; }
    
    [Display(Name = "تاریخ عضویت")]
    public DateTime JoinedAt { get; set; }
    
    // اطلاعات بانکی (در صورت وجود)
    [Display(Name = "شماره حساب بانکی")]
    public string? BankAccountNumber { get; set; }
    
    [Display(Name = "نام بانک")]
    public string? BankName { get; set; }
    
    [Display(Name = "شعبه")]
    public string? BankBranch { get; set; }
}

/// <summary>
/// جزئیات موجودی بانکی عضو به تفکیک حساب
/// </summary>
public class MemberBankingDetailRow
{
    public long MemberId { get; set; }
    
    [Display(Name = "نام عضو")]
    public string MemberName { get; set; } = string.Empty;
    
    [Display(Name = "کد ملی")]
    public string NationalCode { get; set; } = string.Empty;
    
    [Display(Name = "کد خانوار")]
    public string HouseholdCode { get; set; } = string.Empty;
    
    [Display(Name = "نام خانوار")]
    public string HouseholdName { get; set; } = string.Empty;
    
    [Display(Name = "کد حساب")]
    public string AccountCode { get; set; } = string.Empty;
    
    [Display(Name = "نام حساب")]
    public string AccountName { get; set; } = string.Empty;
    
    [Display(Name = "نوع حساب")]
    public string AccountType { get; set; } = string.Empty;
    
    [Display(Name = "موجودی")]
    public decimal Balance { get; set; }
    
    [Display(Name = "تعداد تراکنش")]
    public int TransactionCount { get; set; }
    
    [Display(Name = "اولین تراکنش")]
    public DateTime? FirstTransactionDate { get; set; }
    
    [Display(Name = "آخرین تراکنش")]
    public DateTime? LastTransactionDate { get; set; }
}

/// <summary>
/// خلاصه کل موجودی صندوق
/// </summary>
public class FundSummaryRow
{
    [Display(Name = "کل اعضا")]
    public int TotalMembers { get; set; }
    
    [Display(Name = "اعضای فعال")]
    public int ActiveMembers { get; set; }
    
    [Display(Name = "کل موجودی اصل")]
    public decimal TotalPrincipalBalance { get; set; }
    
    [Display(Name = "کل موجودی سود")]
    public decimal TotalInterestBalance { get; set; }
    
    [Display(Name = "کل موجودی صندوق")]
    public decimal TotalFundBalance { get; set; }
    
    [Display(Name = "تعداد کل تراکنش")]
    public int TotalTransactions { get; set; }
    
    [Display(Name = "آخرین بروزرسانی")]
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
