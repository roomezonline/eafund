namespace MiniBank.Web.Models
{
    public class CashFlowBudgetItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public decimal Percentage { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
