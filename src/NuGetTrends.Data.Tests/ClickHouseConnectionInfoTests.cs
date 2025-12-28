using FluentAssertions;
using NuGetTrends.Data.ClickHouse;
using Xunit;

namespace NuGetTrends.Data.Tests;

public class ClickHouseConnectionInfoTests
{
    public class ParseMethod
    {
        [Theory]
        [InlineData("http://localhost:8123/default", "localhost", "8123", "default")]
        [InlineData("http://clickhouse.example.com:8123/mydb", "clickhouse.example.com", "8123", "mydb")]
        [InlineData("https://secure.clickhouse.io:8443/production", "secure.clickhouse.io", "8443", "production")]
        [InlineData("http://192.168.1.100:8123/analytics", "192.168.1.100", "8123", "analytics")]
        public void HttpUri_ParsesCorrectly(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("http://localhost:8123", "localhost", "8123", null)]
        [InlineData("http://localhost:8123/", "localhost", "8123", null)]
        public void HttpUri_WithoutDatabase_ParsesHostAndPort(string connectionString, string expectedHost, string expectedPort, string? expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("Host=localhost;Port=8123;Database=default", "localhost", "8123", "default")]
        [InlineData("Host=clickhouse.example.com;Port=9000;Database=mydb", "clickhouse.example.com", "9000", "mydb")]
        [InlineData("host=localhost;port=8123;database=default", "localhost", "8123", "default")]
        [InlineData("HOST=LOCALHOST;PORT=8123;DATABASE=DEFAULT", "LOCALHOST", "8123", "DEFAULT")]
        public void KeyValue_ParsesCorrectly(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("Server=localhost;Port=8123;Database=default", "localhost", "8123", "default")]
        [InlineData("server=myserver;port=9000;database=test", "myserver", "9000", "test")]
        public void KeyValue_ServerAlias_ParsesCorrectly(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("Host=localhost;Port=8123", "localhost", "8123", null)]
        [InlineData("Host=localhost;Database=mydb", "localhost", null, "mydb")]
        [InlineData("Port=8123;Database=mydb", null, "8123", "mydb")]
        public void KeyValue_PartialInfo_ParsesAvailableFields(string connectionString, string? expectedHost, string? expectedPort, string? expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("Host=localhost;Port=8123;Database=default;Username=user;Password=pass", "localhost", "8123", "default")]
        [InlineData("Host=localhost;Port=8123;Database=default;Compression=true", "localhost", "8123", "default")]
        public void KeyValue_WithExtraParameters_IgnoresUnknownKeys(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("Host = localhost ; Port = 8123 ; Database = default", "localhost", "8123", "default")]
        [InlineData("  Host=localhost  ;  Port=8123  ;  Database=default  ", "localhost", "8123", "default")]
        public void KeyValue_WithWhitespace_TrimsValues(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("http://user:password@localhost:8123/default", "localhost", "8123", "default")]
        [InlineData("https://admin:secret@clickhouse.io:8443/prod", "clickhouse.io", "8443", "prod")]
        public void HttpUri_WithCredentials_ParsesHostPortDatabase(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Theory]
        [InlineData("http://localhost:8123/default?compress=true", "localhost", "8123", "default")]
        [InlineData("http://localhost:8123/mydb?timeout=30&compress=1", "localhost", "8123", "mydb")]
        public void HttpUri_WithQueryParameters_ParsesHostPortDatabase(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }

        [Fact]
        public void EmptyConnectionString_ReturnsEmptyInfo()
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse("");

            // Assert
            result.Host.Should().BeNull();
            result.Port.Should().BeNull();
            result.Database.Should().BeNull();
        }

        [Fact]
        public void InvalidKeyValueFormat_ReturnsEmptyInfo()
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse("not-a-valid-connection-string");

            // Assert
            result.Host.Should().BeNull();
            result.Port.Should().BeNull();
            result.Database.Should().BeNull();
        }

        [Theory]
        [InlineData("clickhouse://localhost:9000/default")]
        [InlineData("tcp://localhost:9000/default")]
        public void BinaryProtocolUri_ParsesCorrectly(string connectionString)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be("localhost");
            result.Port.Should().Be("9000");
            result.Database.Should().Be("default");
        }

        [Theory]
        [InlineData("Host=localhost;Port=9000;Database=default;Protocol=binary", "localhost", "9000", "default")]
        [InlineData("Host=localhost;Port=8123;Database=default;Protocol=http", "localhost", "8123", "default")]
        public void KeyValue_WithProtocol_ParsesCorrectly(string connectionString, string expectedHost, string expectedPort, string expectedDatabase)
        {
            // Act
            var result = ClickHouseConnectionInfo.Parse(connectionString);

            // Assert
            result.Host.Should().Be(expectedHost);
            result.Port.Should().Be(expectedPort);
            result.Database.Should().Be(expectedDatabase);
        }
    }
}
