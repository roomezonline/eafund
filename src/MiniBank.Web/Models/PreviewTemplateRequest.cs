namespace MiniBank.Web.Models
{
    public class PreviewTemplateRequest
    {
        public decimal AmountTotal { get; set; }
        public decimal? AmountPrincipal { get; set; }
        public decimal? AmountInterest { get; set; }
        public decimal? AmountFee { get; set; }
        public decimal? AmountTax { get; set; }
        public decimal? AmountPenalty { get; set; }
        public decimal? AmountCommission { get; set; }
        public long? MemberId { get; set; }
    }
}
