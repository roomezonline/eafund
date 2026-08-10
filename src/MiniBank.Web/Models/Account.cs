namespace MiniBank.Web.Models
{
    public class Account
    {
        public long AccountId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public byte AccountType { get; set; } // 1=Asset, 2=Liability, 3=Equity, 4=Income, 5=Expense
        public long? ParentAccountId { get; set; }
        public byte? Level { get; set; } // 1=گروه, 2=کل, 3=معین, 4=تفضیلی
        public long? MemberId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation
        public Account? Parent { get; set; }
        public List<Account> Children { get; set; } = new();
        
        // Display helpers
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
            _ => "نامشخص"
        };
        
        public string DisplayTitle => $"{Code} - {Name}";
        public string FullDisplayTitle => $"{Code} - {Name} ({LevelName})";
    }
}
