namespace MiniBank.Web.Models
{
    public class BankAccount
    {
        public long AccountId { get; set; }
        public string BankName { get; set; } = string.Empty; // نام بانک یا موسسه
        public string AccountNumber { get; set; } = string.Empty; // شماره حساب
        public string? CardNumber { get; set; } = string.Empty; // شماره کارت
        public string? Shaba { get; set; } = string.Empty; // شماره شبا
        public string AccountType { get; set; } = string.Empty; // نوع حساب (جاری/کوتاه مدت/بلندمدت/...)
        public decimal? InterestRate { get; set; } // سود حساب به درصد
        public long? GlAccountId { get; set; } // لینک به حسابداری (معین)
        public bool IsActive { get; set; } = true; // وضعیت فعال/غیرفعال
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow; // تاریخ افتتاح
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // نمایش ترکیبی
        public string DisplayTitle => string.IsNullOrWhiteSpace(BankName) ? AccountNumber : $"{BankName} - {AccountNumber}";
    }
}
