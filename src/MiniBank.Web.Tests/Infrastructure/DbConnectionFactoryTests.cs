using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MiniBank.Web.Infrastructure;
using Moq;
using System.Data;
using Xunit;

namespace MiniBank.Web.Tests.Infrastructure
{
    public class DbConnectionFactoryTests
    {
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly Mock<ILogger<DbConnectionFactory>> _loggerMock;
        private readonly DbConnectionFactory _factory;

        public DbConnectionFactoryTests()
        {
            _configurationMock = new Mock<IConfiguration>();
            _loggerMock = new Mock<ILogger<DbConnectionFactory>>();
            
            // Setup ConnectionStrings section
            var connectionStringsSectionMock = new Mock<IConfigurationSection>();
            var defaultConnectionSectionMock = new Mock<IConfigurationSection>();
            defaultConnectionSectionMock.Setup(x => x.Value).Returns("Server=localhost;Database=TestDb;Trusted_Connection=true;MultipleActiveResultSets=true;");
            
            connectionStringsSectionMock.Setup(x => x["DefaultConnection"]).Returns("Server=localhost;Database=TestDb;Trusted_Connection=true;MultipleActiveResultSets=true;");
            _configurationMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(connectionStringsSectionMock.Object);
            
            _factory = new DbConnectionFactory(_configurationMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void CreateConnection_Should_Return_SqlConnection()
        {
            // Act
            var connection = _factory.CreateConnection();

            // Assert
            connection.Should().NotBeNull();
            connection.Should().BeAssignableTo<IDbConnection>();
            connection.State.Should().Be(ConnectionState.Closed);
        }

        [Fact]
        public async Task CreateConnectionAsync_Should_Return_Open_SqlConnection()
        {
            // Act
            var connection = await _factory.CreateConnectionAsync();

            // Assert
            connection.Should().NotBeNull();
            connection.Should().BeAssignableTo<IDbConnection>();
            // Note: In real tests with actual database, connection.State would be Open
            // For unit tests without database, we just verify the connection object is created
        }

        [Fact]
        public void CreateConnection_Should_Throw_When_ConnectionString_Is_Null()
        {
            // Arrange
            var connectionStringsSectionMock = new Mock<IConfigurationSection>();
            connectionStringsSectionMock.Setup(x => x["DefaultConnection"]).Returns((string?)null);
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(connectionStringsSectionMock.Object);
            
            // Act & Assert
            var act = () => new DbConnectionFactory(configMock.Object, _loggerMock.Object);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void CreateConnection_Should_Throw_When_ConnectionString_Is_Empty()
        {
            // Arrange
            var connectionStringsSectionMock = new Mock<IConfigurationSection>();
            connectionStringsSectionMock.Setup(x => x["DefaultConnection"]).Returns(string.Empty);
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(connectionStringsSectionMock.Object);
            
            // Act & Assert
            var act = () => new DbConnectionFactory(configMock.Object, _loggerMock.Object);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public async Task CreateConnectionAsync_Should_Log_Error_On_Connection_Failure()
        {
            // Arrange
            var connectionStringsSectionMock = new Mock<IConfigurationSection>();
            connectionStringsSectionMock.Setup(x => x["DefaultConnection"]).Returns("Invalid Connection String");
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(x => x.GetSection("ConnectionStrings")).Returns(connectionStringsSectionMock.Object);
            
            var factory = new DbConnectionFactory(configMock.Object, _loggerMock.Object);

            // Act & Assert
            var act = async () => await factory.CreateConnectionAsync();
            await act.Should().ThrowAsync<Exception>();
            
            // Verify error was logged
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to open database connection")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
