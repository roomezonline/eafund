using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using MiniBank.Web.Infrastructure.Repositories;

namespace MiniBank.Web.Services.Auth
{
    public class AuthActivityService
    {
        private readonly AuthenticationStateProvider _authState;
        private readonly AuthRepository _repo;
        private readonly IMemoryCache _cache;

        public AuthActivityService(AuthenticationStateProvider authState, AuthRepository repo, IMemoryCache cache)
        {
            _authState = authState;
            _repo = repo;
            _cache = cache;
        }

        public async Task TouchSeenThrottledAsync(TimeSpan throttle)
        {
            var state = await _authState.GetAuthenticationStateAsync();
            var user = state.User;

            if (user?.Identity?.IsAuthenticated != true)
                return;

            var idStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(idStr, out var userId) || userId <= 0)
                return;

            var cacheKey = $"auth:lastseen:{userId}";
            if (_cache.TryGetValue(cacheKey, out DateTime lastUtc))
            {
                if (DateTime.UtcNow - lastUtc < throttle)
                    return;
            }

            _cache.Set(cacheKey, DateTime.UtcNow, throttle);

            try
            {
                await _repo.TouchSeenAsync(userId);
            }
            catch
            {
            }
        }
    }
}
