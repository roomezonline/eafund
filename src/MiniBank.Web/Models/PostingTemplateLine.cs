namespace MiniBank.Web.Models
{
    public class PostingTemplateLine
    {
        public int PostingTemplateLineId { get; set; }
        public int PostingTemplateId { get; set; }
        public int LineNo { get; set; }
        public long AccountId { get; set; } // GL Account (حساب حسابداری)
        public long? OperationalAccountId { get; set; } // Operational Account (حساب عملیاتی - بانکی/صندوق)
        public byte Side { get; set; } // 1=Debit, 2=Credit
        public byte AmountSource { get; set; } // 1=Total, 2=Principal, 3=Interest, 4=Fee, 5=Tax, 6=Penalty, 7=Commission, 8=FixedAmount
        public decimal? FixedAmount { get; set; }
        public bool AllowMemberDimension { get; set; }

        // Convenience
        public string? AccountCode { get; set; }
        public string? AccountName { get; set; }
    }
}
