using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Caching.Memory;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Services.Auth
{
    public class AuthPermissionService
    {
        private readonly AuthRepository _repo;
        private readonly IMemoryCache _cache;

        private const string RoleVersionPrefix = "auth:permver:";

        public AuthPermissionService(AuthRepository repo, IMemoryCache cache)
        {
            _repo = repo;
            _cache = cache;
        }

        private static string NormalizePath(string? absoluteOrRelative)
        {
            var p = (absoluteOrRelative ?? string.Empty).Trim();
            if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try { p = new Uri(p).AbsolutePath; } catch { }
            }
            if (!p.StartsWith('/')) p = "/" + p;
            if (p.Length > 1 && p.EndsWith('/')) p = p.TrimEnd('/');
            return p;
        }

        public bool IsPublicPath(string path)
        {
            path = NormalizePath(path);
            if (path.Equals("/login", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/_", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/icons", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/manifest", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/service-worker", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith("/pages/errors", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public async Task<bool> IsAllowedAsync(long roleId, string path)
        {
            path = NormalizePath(path);
            var version = _cache.GetOrCreate(RoleVersionPrefix + roleId, e => 1L);
            var cacheKey = $"auth:perm:{roleId}:{version}:{path}";
            if (_cache.TryGetValue(cacheKey, out bool cached))
                return cached;

            const string sql = @"
SELECT TOP 1 ISNULL(rp.Allowed, 0)
FROM dbo.AuthPages p
LEFT JOIN dbo.AuthRolePagePermissions rp ON rp.PageId = p.PageId AND rp.RoleId = @roleId
WHERE p.IsActive = 1 AND (p.Path = @path OR p.PageKey = @path)";

            // Dapper extension on IDbConnection via repo connection is not directly accessible here; use repo methods.
            // Use page lookup then permission lookup.
            var page = await _repo.GetPageByKeyAsync(path);
            if (page == null && path.Length > 1)
            {
                // Fallback for parameterized routes (/x/{id}) or deeper paths: try parent paths.
                // Example: /admin/reports/tafsili-moeen/123 => /admin/reports/tafsili-moeen
                var p = path;
                while (page == null && p.Length > 1)
                {
                    var lastSlash = p.LastIndexOf('/');
                    if (lastSlash <= 0) break;
                    p = p.Substring(0, lastSlash);
                    page = await _repo.GetPageByKeyAsync(p);
                }
            }
            if (page == null)
            {
                // If page isn't registered yet, default deny (more secure).
                _cache.Set(cacheKey, false, TimeSpan.FromMinutes(5));
                return false;
            }

            var perms = await _repo.GetRolePagePermissionsAsync(roleId);
            var allowed = perms.FirstOrDefault(x => x.PageId == page.PageId).Allowed;

            _cache.Set(cacheKey, allowed, TimeSpan.FromMinutes(5));
            return allowed;
        }

        public void InvalidateRole(long roleId)
        {
            var key = RoleVersionPrefix + roleId;
            var cur = _cache.GetOrCreate(key, e => 1L);
            _cache.Set(key, cur + 1L, TimeSpan.FromHours(12));
        }
    }
}
