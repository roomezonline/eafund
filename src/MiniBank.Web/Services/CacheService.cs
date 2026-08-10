using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace MiniBank.Web.Services
{
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
        Task RemoveAsync(string key);
        Task RemoveByPatternAsync(string pattern);
    }

    public class CacheService : ICacheService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<CacheService> _logger;
        private readonly HashSet<string> _cacheKeys = new();
        private readonly object _lockObject = new();

        public CacheService(IMemoryCache memoryCache, ILogger<CacheService> logger)
        {
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public Task<T?> GetAsync<T>(string key)
        {
            try
            {
                if (_memoryCache.TryGetValue(key, out var value))
                {
                    _logger.LogDebug("Cache hit for key: {Key}", key);
                    return Task.FromResult((T?)value);
                }

                _logger.LogDebug("Cache miss for key: {Key}", key);
                return Task.FromResult(default(T));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache value for key: {Key}", key);
                return Task.FromResult(default(T));
            }
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            try
            {
                var options = new MemoryCacheEntryOptions();
                
                if (expiration.HasValue)
                {
                    options.AbsoluteExpirationRelativeToNow = expiration;
                }
                else
                {
                    // Default expiration: 30 minutes
                    options.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                }

                options.RegisterPostEvictionCallback((key, value, reason, state) =>
                {
                    lock (_lockObject)
                    {
                        _cacheKeys.Remove(key.ToString() ?? string.Empty);
                    }
                    _logger.LogDebug("Cache entry evicted: {Key}, Reason: {Reason}", key, reason);
                });

                _memoryCache.Set(key, value, options);

                lock (_lockObject)
                {
                    _cacheKeys.Add(key);
                }

                _logger.LogDebug("Cache set for key: {Key}, Expiration: {Expiration}", key, expiration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cache value for key: {Key}", key);
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            try
            {
                _memoryCache.Remove(key);
                
                lock (_lockObject)
                {
                    _cacheKeys.Remove(key);
                }

                _logger.LogDebug("Cache removed for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache value for key: {Key}", key);
            }

            return Task.CompletedTask;
        }

        public Task RemoveByPatternAsync(string pattern)
        {
            try
            {
                List<string> keysToRemove;
                
                lock (_lockObject)
                {
                    keysToRemove = _cacheKeys
                        .Where(key => key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                foreach (var key in keysToRemove)
                {
                    _memoryCache.Remove(key);
                    lock (_lockObject)
                    {
                        _cacheKeys.Remove(key);
                    }
                }

                _logger.LogDebug("Cache removed for pattern: {Pattern}, Count: {Count}", pattern, keysToRemove.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache values for pattern: {Pattern}", pattern);
            }

            return Task.CompletedTask;
        }
    }
}
