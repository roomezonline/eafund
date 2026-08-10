namespace MiniBank.Web.Models.Auth
{
    public class AuthRole
    {
        public long RoleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
