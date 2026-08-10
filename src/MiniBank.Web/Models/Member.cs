namespace MiniBank.Web.Models
{
    public class Member
    {
        public long MemberId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string? FatherName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string NationalCode { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string? HouseholdCode { get; set; }
        public bool IsActive { get; set; }
        public DateTime JoinedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        
        // Navigation properties
        public Household? Household { get; set; }
        
        // Computed property for display
        public string FullName => $"{FirstName} {LastName}".Trim();
    }
}
