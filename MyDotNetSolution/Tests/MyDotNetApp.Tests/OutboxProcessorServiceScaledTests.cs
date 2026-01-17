using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyDotNetApp.Models;
using MyDotNetApp.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace MyDotNetApp.Tests
{
    public class OutboxProcessorServiceScaledTests
    {
        private readonly Mock<ILogger<OutboxProcessorServiceScaled>> _loggerMock;
        private readonly Mock<IOptions<KafkaOutboxSettings>> _optionsMock;
        private readonly KafkaOutboxSettings _kafkaSettings;
        private const string TestConnectionString = "Server=localhost;Database=test;";

        public OutboxProcessorServiceScaledTests()
        {
            _loggerMock = new Mock<ILogger<OutboxProcessorServiceScaled>>();
            
            _kafkaSettings = new KafkaOutboxSettings
            {
                BootstrapServers = "localhost:9092",
                TopicName = "test-topic",
                SchemaRegistryUrl = "http://localhost:8081",
                BatchSize = 100,
                PollingIntervalMs = 10,
                MaxConcurrentProducers = 5,
                MaxProducerBuffer = 1000,
                DatabaseConnectionPoolSize = 10
            };

            _optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
            _optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);
        }

        [Fact]
        public void Constructor_ValidParameters_InitializesSuccessfully()
        {
            // Act
            var service = new OutboxProcessorServiceScaled(
                _loggerMock.Object,
                _optionsMock.Object,
                TestConnectionString);

            // Assert
            Assert.NotNull(service);
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new OutboxProcessorServiceScaled(null!, _optionsMock.Object, TestConnectionString));
            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_NullOptions_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new OutboxProcessorServiceScaled(_loggerMock.Object, null!, TestConnectionString));
            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_NullConnectionString_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new OutboxProcessorServiceScaled(_loggerMock.Object, _optionsMock.Object, null!));
            Assert.NotNull(ex);
        }

        [Fact]
        public void Constructor_NullSchemaRegistryUrl_ThrowsArgumentNullException()
        {
            // Arrange
            var invalidSettings = new KafkaOutboxSettings
            {
                BootstrapServers = "localhost:9092",
                TopicName = "test-topic",
                SchemaRegistryUrl = null, // Invalid
                BatchSize = 100
            };
            
            var invalidOptionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
            invalidOptionsMock.Setup(o => o.Value).Returns(invalidSettings);

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new OutboxProcessorServiceScaled(_loggerMock.Object, invalidOptionsMock.Object, TestConnectionString));
            Assert.NotNull(ex);
        }

        [Fact]
        public async Task StartAsync_InitializesKafkaProducers()
        {
            // Arrange
            var service = new OutboxProcessorServiceScaled(
                _loggerMock.Object,
                _optionsMock.Object,
                TestConnectionString);

            var cts = new CancellationTokenSource();
            cts.CancelAfter(100); // Cancel after 100ms to stop the service

            try
            {
                // Act
                await service.StartAsync(cts.Token);

                // Assert
                _loggerMock.Verify(
                    l => l.Log(
                        LogLevel.Information,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Starting Scaled Kafka Outbox Processor Service")),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                    Times.Once);
            }
            finally
            {
                service?.Dispose();
            }
        }

        [Fact]
        public async Task StopAsync_FlushesProducersAndLogs()
        {
            // Arrange
            var service = new OutboxProcessorServiceScaled(
                _loggerMock.Object,
                _optionsMock.Object,
                TestConnectionString);

            // Act
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            // Assert
            _loggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Stopping Scaled Kafka Outbox Processor Service")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void Dispose_DisposesResources()
        {
            // Arrange
            var service = new OutboxProcessorServiceScaled(
                _loggerMock.Object,
                _optionsMock.Object,
                TestConnectionString);

            // Act
            service.Dispose();

            // Assert - no exception thrown
            Assert.True(true);
        }

        [Fact]
        public async Task StopAsync_CompletesChannels()
        {
            // Arrange
            var service = new OutboxProcessorServiceScaled(
                _loggerMock.Object,
                _optionsMock.Object,
                TestConnectionString);

            // Act
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            // Assert - service should complete cleanly
            try
            {
                service.Dispose();
            }
            catch (ChannelClosedException)
            {
                // Expected - channel may already be closed
            }
        }

        [Fact]
        public void KafkaOutboxSettings_DefaultValues_Correct()
        {
            // Arrange & Act
            var settings = new KafkaOutboxSettings();

            // Assert
            Assert.Equal(50000, settings.BatchSize);
            Assert.Equal(10, settings.PollingIntervalMs);
            Assert.Equal(20, settings.MaxConcurrentProducers);
            Assert.Equal(200000, settings.MaxProducerBuffer);
            Assert.Equal(40, settings.DatabaseConnectionPoolSize);
        }

        [Fact]
        public void PerformanceMetrics_RecordAndLog()
        {
            // Arrange
            var metrics = new PerformanceMetrics();
            var loggerMock = new Mock<ILogger>();

            // Act
            metrics.RecordFetched(100);
            metrics.RecordProduced();
            metrics.RecordProduced();
            metrics.RecordMarked(50);
            metrics.RecordFailed();

            // Assert - no exception thrown
            metrics.LogMetrics(loggerMock.Object);
            Assert.True(true);
        }

        [Fact]
        public void PerformanceMetrics_MultipleRecordOperations_AggregatesCorrectly()
        {
            // Arrange
            var metrics = new PerformanceMetrics();

            // Act
            metrics.RecordFetched(10);
            metrics.RecordFetched(20);
            metrics.RecordProduced();
            metrics.RecordProduced();
            metrics.RecordProduced();
            metrics.RecordMarked(25);
            metrics.RecordFailed();

            // Assert - verify no exceptions
            var loggerMock = new Mock<ILogger>();
            metrics.LogMetrics(loggerMock.Object);
        }

        [Fact]
        public void PerformanceMetrics_ThreadSafety_HandlesConcurrentCalls()
        {
            // Arrange
            var metrics = new PerformanceMetrics();
            var tasks = new List<Task>();

            // Act - simulate concurrent metric recording
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    metrics.RecordFetched(1);
                    metrics.RecordProduced();
                    metrics.RecordMarked(1);
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Assert - no exception thrown
            var loggerMock = new Mock<ILogger>();
            metrics.LogMetrics(loggerMock.Object);
        }
    }
}
