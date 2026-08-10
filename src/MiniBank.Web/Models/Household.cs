namespace MiniBank.Web.Models
{
    public class Household
    {
        public long HouseholdId { get; set; }
        public string HouseholdCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long? OwnerMemberId { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
