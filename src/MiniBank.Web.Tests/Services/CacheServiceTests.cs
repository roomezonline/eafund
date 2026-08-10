using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MiniBank.Web.Services;
using Moq;
using Xunit;

namespace MiniBank.Web.Tests.Services
{
    public class CacheServiceTests : IDisposable
    {
        private readonly IMemoryCache _memoryCache;
        private readonly Mock<ILogger<CacheService>> _loggerMock;
        private readonly CacheService _cacheService;

        public CacheServiceTests()
        {
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _loggerMock = new Mock<ILogger<CacheService>>();
            _cacheService = new CacheService(_memoryCache, _loggerMock.Object);
        }

        [Fact]
        public async Task GetAsync_Should_Return_Cached_Value_When_Exists()
        {
            // Arrange
            const string key = "test-key";
            const string expectedValue = "test-value";
            await _cacheService.SetAsync(key, expectedValue);

            // Act
            var result = await _cacheService.GetAsync<string>(key);

            // Assert
            result.Should().Be(expectedValue);
        }

        [Fact]
        public async Task GetAsync_Should_Return_Default_When_Key_Not_Exists()
        {
            // Arrange
            const string key = "non-existent-key";

            // Act
            var result = await _cacheService.GetAsync<string>(key);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_Should_Store_Value_With_Default_Expiration()
        {
            // Arrange
            const string key = "test-key";
            const string value = "test-value";

            // Act
            await _cacheService.SetAsync(key, value);
            var result = await _cacheService.GetAsync<string>(key);

            // Assert
            result.Should().Be(value);
        }

        [Fact]
        public async Task SetAsync_Should_Store_Value_With_Custom_Expiration()
        {
            // Arrange
            const string key = "test-key";
            const string value = "test-value";
            var expiration = TimeSpan.FromSeconds(1);

            // Act
            await _cacheService.SetAsync(key, value, expiration);
            var result = await _cacheService.GetAsync<string>(key);

            // Assert
            result.Should().Be(value);

            // Wait for expiration
            await Task.Delay(1100);
            var expiredResult = await _cacheService.GetAsync<string>(key);
            expiredResult.Should().BeNull();
        }

        [Fact]
        public async Task RemoveAsync_Should_Remove_Cached_Value()
        {
            // Arrange
            const string key = "test-key";
            const string value = "test-value";
            await _cacheService.SetAsync(key, value);

            // Act
            await _cacheService.RemoveAsync(key);
            var result = await _cacheService.GetAsync<string>(key);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task RemoveByPatternAsync_Should_Remove_Matching_Keys()
        {
            // Arrange
            await _cacheService.SetAsync("user:1", "value1");
            await _cacheService.SetAsync("user:2", "value2");
            await _cacheService.SetAsync("product:1", "value3");

            // Act
            await _cacheService.RemoveByPatternAsync("user");

            // Assert
            var user1 = await _cacheService.GetAsync<string>("user:1");
            var user2 = await _cacheService.GetAsync<string>("user:2");
            var product1 = await _cacheService.GetAsync<string>("product:1");

            user1.Should().BeNull();
            user2.Should().BeNull();
            product1.Should().Be("value3"); // Should remain
        }

        [Fact]
        public async Task Should_Handle_Complex_Objects()
        {
            // Arrange
            const string key = "complex-object";
            var complexObject = new { Id = 1, Name = "Test", Items = new[] { 1, 2, 3 } };

            // Act
            await _cacheService.SetAsync(key, complexObject);
            var result = await _cacheService.GetAsync<object>(key);

            // Assert
            result.Should().BeEquivalentTo(complexObject);
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
        }
    }
}
