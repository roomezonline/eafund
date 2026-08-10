using MiniBank.Web.Infrastructure.Repositories;
using MiniBank.Web.Models.Auth;

namespace MiniBank.Web.Services.Auth
{
    public class AuthSyncService
    {
        private readonly AuthRepository _authRepo;
        private readonly MemberRepository _memberRepo;
        private readonly PasswordHashService _hasher;

        public sealed record MemberUserSyncItem(long MemberId, string FirstName, string LastName, string Action);

        public sealed class MemberUserSyncReport
        {
            public int CreatedCount { get; set; }
            public int ExistingCount { get; set; }
            public int ErrorCount { get; set; }
            public List<MemberUserSyncItem> Items { get; } = new();
        }

        public AuthSyncService(AuthRepository authRepo, MemberRepository memberRepo, PasswordHashService hasher)
        {
            _authRepo = authRepo;
            _memberRepo = memberRepo;
            _hasher = hasher;
        }

        private static string Last4(string s)
        {
            s = (s ?? string.Empty).Trim();
            if (s.Length <= 4) return s;
            return s.Substring(s.Length - 4);
        }

        public async Task<MemberUserSyncReport> EnsureAllMembersHaveUsersAsync()
        {
            var memberRole = await _authRepo.GetRoleByNameAsync("Member");
            if (memberRole == null)
                throw new InvalidOperationException("Role 'Member' not found in AuthRoles");

            var members = await _memberRepo.GetAllAsync(null, activeOnly: false, householdId: null, limit: 500000);
            var report = new MemberUserSyncReport();

            foreach (var m in members)
            {
                var username = (m.NationalCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(username))
                    continue;

                var existing = await _authRepo.GetUserByUsernameAsync(username);
                var pwd = Last4(username);

                if (existing == null)
                {
                    var user = new AuthUser
                    {
                        Username = username,
                        PasswordEnc = _hasher.HashPassword(pwd),
                        RoleId = memberRole.RoleId,
                        MemberId = m.MemberId,
                        IsActive = m.IsActive
                    };

                    try
                    {
                        await _authRepo.CreateUserAsync(user);
                        report.CreatedCount++;
                        report.Items.Add(new MemberUserSyncItem(m.MemberId, m.FirstName ?? string.Empty, m.LastName ?? string.Empty, "ایجاد شد"));
                    }
                    catch
                    {
                        // ignore per-user conflicts
                        report.ErrorCount++;
                        report.Items.Add(new MemberUserSyncItem(m.MemberId, m.FirstName ?? string.Empty, m.LastName ?? string.Empty, "خطا/تداخل"));
                    }

                    continue;
                }

                // Existing user: do not modify (preserve Role/IsActive/Password changes done by admin).
                report.ExistingCount++;
                continue;
            }

            return report;
        }

        public async Task EnsureUserForMemberAsync(long memberId)
        {
            var member = await _memberRepo.GetByIdAsync(memberId);
            if (member == null)
                return;

            var username = (member.NationalCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
                return;

            var existing = await _authRepo.GetUserByUsernameAsync(username);
            if (existing != null)
                return;

            var memberRole = await _authRepo.GetRoleByNameAsync("Member");
            if (memberRole == null)
                return;

            var pwd = Last4(username);
            var user = new AuthUser
            {
                Username = username,
                PasswordEnc = _hasher.HashPassword(pwd),
                RoleId = memberRole.RoleId,
                MemberId = member.MemberId,
                IsActive = member.IsActive
            };

            await _authRepo.CreateUserAsync(user);
        }
    }
}
