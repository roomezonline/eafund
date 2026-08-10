using System.Collections.Concurrent;
using System.Security.Claims;

namespace MiniBank.Web.Services
{
    public sealed class OnlineUsersTracker
    {
        public sealed record OnlineUserItem(
            string Key,
            string Username,
            string? Role,
            long? UserId,
            long? MemberId,
            DateTime LastSeenUtc,
            string? LastPath);

        private sealed class Entry
        {
            public string Key { get; init; } = "";
            public string Username { get; init; } = "";
            public string? Role { get; set; }
            public long? UserId { get; set; }
            public long? MemberId { get; set; }
            public DateTime LastSeenUtc { get; set; }
            public string? LastPath { get; set; }
        }

        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public void Touch(ClaimsPrincipal user, string? lastPath)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return;

            var username = (user.Identity?.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
                return;

            var key = username;

            long? TryGetLong(string claimType)
            {
                var v = (user.FindFirst(claimType)?.Value ?? string.Empty).Trim();
                if (long.TryParse(v, out var x)) return x;
                return null;
            }

            var role = (user.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(role)) role = null;

            var userId = TryGetLong(ClaimTypes.NameIdentifier);
            var memberId = TryGetLong("MemberId");

            var nowUtc = DateTime.UtcNow;

            _entries.AddOrUpdate(
                key,
                _ => new Entry
                {
                    Key = key,
                    Username = username,
                    Role = role,
                    UserId = userId,
                    MemberId = memberId,
                    LastSeenUtc = nowUtc,
                    LastPath = NormalizePath(lastPath)
                },
                (_, existing) =>
                {
                    existing.Role = role;
                    existing.UserId = userId;
                    existing.MemberId = memberId;
                    existing.LastSeenUtc = nowUtc;
                    existing.LastPath = NormalizePath(lastPath);
                    return existing;
                });
        }

        public IReadOnlyList<OnlineUserItem> Snapshot()
        {
            var list = new List<OnlineUserItem>(_entries.Count);
            foreach (var kv in _entries)
            {
                var e = kv.Value;
                list.Add(new OnlineUserItem(e.Key, e.Username, e.Role, e.UserId, e.MemberId, e.LastSeenUtc, e.LastPath));
            }
            return list;
        }

        public void PurgeOlderThan(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var kv in _entries)
            {
                if (kv.Value.LastSeenUtc < cutoff)
                    _entries.TryRemove(kv.Key, out _);
            }
        }

        private static string? NormalizePath(string? uriOrPath)
        {
            var x = (uriOrPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(x)) return null;

            if (Uri.TryCreate(x, UriKind.Absolute, out var abs))
                return abs.PathAndQuery;

            if (Uri.TryCreate(x, UriKind.Relative, out var rel))
                return rel.OriginalString;

            return x;
        }
    }
}
