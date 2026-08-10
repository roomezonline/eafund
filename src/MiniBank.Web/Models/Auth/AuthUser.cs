namespace MiniBank.Web.Models.Auth
{
    public class AuthUser
    {
        public long UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordEnc { get; set; } = string.Empty;
        public string? PasswordViewEnc { get; set; }
        public long RoleId { get; set; }
        public long? MemberId { get; set; }
        public string? MemberFirstName { get; set; }
        public string? MemberLastName { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? LastSeenAt { get; set; }

        public AuthRole? Role { get; set; }
    }
}
