namespace MiniBank.Web.Models
{
    public class TrialBalanceNode
    {
        public long AccountId { get; set; }
        public long? JournalLineId { get; set; } // برای تراکنش‌ها
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public byte Level { get; set; } // 1=گروه, 2=کل, 3=معین, 4=تفضیلی
        public byte AccountType { get; set; }
        public long? ParentAccountId { get; set; }
        
        // مانده‌ها
        public decimal DebitBalance { get; set; }
        public decimal CreditBalance { get; set; }
        public decimal NetBalance => DebitBalance - CreditBalance;
        
        // ویژگی‌های UI
        public bool HasChildren { get; set; }
        public bool IsExpanded { get; set; }
        public List<TrialBalanceNode> Children { get; set; } = new();
        
        // نمایش
        public string AccountTypeName => AccountType switch
        {
            1 => "دارایی",
            2 => "بدهی", 
            3 => "حقوق صاحبان",
            4 => "درآمد",
            5 => "هزینه",
            _ => "نامشخص"
        };
        
        public string LevelName => Level switch
        {
            1 => "گروه",
            2 => "کل", 
            3 => "معین",
            4 => "تفضیلی",
            5 => "تراکنش",
            _ => "نامشخص"
        };
        
        public string DisplayTitle => $"{Code} - {Name}";
        public string FullDisplayTitle => $"{Code} - {Name} ({LevelName})";
        
        // رنگ‌بندی بر اساس نوع حساب
        public string AccountTypeColor => AccountType switch
        {
            1 => "text-success", // دارایی - سبز
            2 => "text-danger",  // بدهی - قرمز
            3 => "text-primary", // حقوق صاحبان - آبی
            4 => "text-info",    // درآمد - آبی روشن
            5 => "text-warning", // هزینه - نارنجی
            _ => "text-muted"
        };
        
        // آیکون بر اساس سطح
        public string LevelIcon => Level switch
        {
            1 => "fas fa-layer-group",
            2 => "fas fa-list",
            3 => "fas fa-file-alt",
            4 => "fas fa-search",
            5 => "fas fa-exchange-alt",
            _ => "fas fa-question"
        };
    }
}
