namespace MiniBank.Web.Models.Auth
{
    public class AuthRolePagePermission
    {
        public long RoleId { get; set; }
        public long PageId { get; set; }
        public bool Allowed { get; set; }
        public DateTime UpdatedAt { get; set; }

        public AuthRole? Role { get; set; }
        public AuthPage? Page { get; set; }
    }
}
