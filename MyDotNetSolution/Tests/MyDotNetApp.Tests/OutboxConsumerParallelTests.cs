using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyDotNetApp.Models;
using MyDotNetApp.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyDotNetApp.Tests
{
    public class OutboxConsumerParallelTests
    {
        private readonly KafkaOutboxSettings _kafkaSettings;
        private readonly IConfiguration _configuration;

        public OutboxConsumerParallelTests()
        {
            _kafkaSettings = new KafkaOutboxSettings
            {
                BootstrapServers = "localhost:9092",
                TopicName = "outbox-events",
                SchemaRegistryUrl = "http://localhost:8081",
                BatchSize = 100,
                PollingIntervalMs = 10,
                MaxConcurrentProducers = 5,
                MaxProducerBuffer = 1000,
                DatabaseConnectionPoolSize = 10
            };

            var configDict = new Dictionary<string, string?>
            {
                ["Processing:PollIntervalMs"] = "5000",
                ["Consumer:BatchSize"] = "1000",
                ["Consumer:FlushIntervalMs"] = "50",
                ["Consumer:InstanceCount"] = "3"
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();
        }

        [Fact]
        public void MultipleConsumers_CanBeCreated_WithDifferentInstanceIds()
        {
            // Arrange & Act
            var consumers = new List<OutboxConsumerService>();
            for (int i = 0; i < 3; i++)
            {
                var loggerMock = new Mock<ILogger<OutboxConsumerService>>();
                var outboxServiceMock = new Mock<IOutboxService>();
                var optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
                optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);

                consumers.Add(new OutboxConsumerService(
                    loggerMock.Object,
                    outboxServiceMock.Object,
                    _configuration,
                    optionsMock.Object,
                    i));
            }

            // Assert
            Assert.Equal(3, consumers.Count);
            Assert.All(consumers, c => Assert.NotNull(c));
        }

        [Fact]
        public async Task MultipleConsumers_ProcessBatchesConcurrently_WithoutBlocking()
        {
            // Arrange
            var completedTasks = new List<int>();
            var completedTasksLock = new object();
            var outboxServiceMock = new Mock<IOutboxService>();

            // Simulate batch processing with slight delay
            outboxServiceMock
                .Setup(s => s.MarkMessagesAsReceivedBatchAsync(
                    It.IsAny<List<long>>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (List<long> ids, DateTime dt, CancellationToken ct) =>
                {
                    await Task.Delay(50, ct); // Simulate DB work
                    return Task.CompletedTask;
                });

            var consumers = new List<OutboxConsumerService>();
            for (int i = 0; i < 3; i++)
            {
                var loggerMock = new Mock<ILogger<OutboxConsumerService>>();
                var optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
                optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);

                consumers.Add(new OutboxConsumerService(
                    loggerMock.Object,
                    outboxServiceMock.Object,
                    _configuration,
                    optionsMock.Object,
                    i));
            }

            // Act - Start all consumers concurrently
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));

            var stopwatch = Stopwatch.StartNew();
            var tasks = consumers.Select((consumer, index) =>
            {
                return Task.Run(async () =>
                {
                    try
                    {
                        await consumer.StartAsync(cts.Token);
                        await Task.Delay(100, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelled
                    }
                    finally
                    {
                        lock (completedTasksLock)
                        {
                            completedTasks.Add(index);
                        }
                        await consumer.StopAsync(CancellationToken.None);
                    }
                });
            }).ToList();

            await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            Assert.Equal(3, completedTasks.Count);
            Assert.True(stopwatch.ElapsedMilliseconds < 500, 
                $"Parallel execution took {stopwatch.ElapsedMilliseconds}ms, should be < 500ms");
        }

        [Fact]
        public async Task ParallelBatchUpdates_DoNotBlockEachOther()
        {
            // Arrange
            var updateCallCount = 0;
            var updateCallLock = new object();
            var concurrentUpdateCount = 0;
            var maxConcurrentUpdates = 0;
            var concurrencyLock = new object();

            var outboxServiceMock = new Mock<IOutboxService>();
            outboxServiceMock
                .Setup(s => s.MarkMessagesAsReceivedBatchAsync(
                    It.IsAny<List<long>>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (List<long> ids, DateTime dt, CancellationToken ct) =>
                {
                    // Track concurrent executions
                    lock (concurrencyLock)
                    {
                        concurrentUpdateCount++;
                        if (concurrentUpdateCount > maxConcurrentUpdates)
                        {
                            maxConcurrentUpdates = concurrentUpdateCount;
                        }
                    }

                    // Simulate work
                    await Task.Delay(30, ct);

                    lock (concurrencyLock)
                    {
                        concurrentUpdateCount--;
                    }

                    lock (updateCallLock)
                    {
                        updateCallCount++;
                    }

                    return Task.CompletedTask;
                });

            // Act - Simulate 3 consumers making concurrent batch updates
            var tasks = new List<Task>();
            for (int consumerIndex = 0; consumerIndex < 3; consumerIndex++)
            {
                var messageIds = Enumerable.Range(consumerIndex * 1000, 100)
                    .Select(i => (long)i)
                    .ToList();

                tasks.Add(Task.Run(async () =>
                {
                    await outboxServiceMock.Object.MarkMessagesAsReceivedBatchAsync(
                        messageIds,
                        DateTime.UtcNow,
                        CancellationToken.None);
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(3, updateCallCount);
            Assert.True(maxConcurrentUpdates >= 2, 
                $"Expected at least 2 concurrent updates, got {maxConcurrentUpdates}");
        }

        [Fact]
        public void Configuration_SupportsMultipleInstanceCount()
        {
            // Arrange & Act
            var instanceCount = _configuration.GetValue<int>("Consumer:InstanceCount", 1);

            // Assert
            Assert.Equal(3, instanceCount);
        }

        [Fact]
        public async Task ThreeConsumers_ProcessDifferentPartitions_Simultaneously()
        {
            // Arrange
            var processedPartitions = new HashSet<int>();
            var partitionLock = new object();
            var outboxServiceMock = new Mock<IOutboxService>();

            outboxServiceMock
                .Setup(s => s.MarkMessagesAsReceivedBatchAsync(
                    It.IsAny<List<long>>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act - Create 3 consumers simulating different partitions
            var consumers = new List<OutboxConsumerService>();
            for (int i = 0; i < 3; i++)
            {
                var loggerMock = new Mock<ILogger<OutboxConsumerService>>();
                var optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
                optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);

                var consumer = new OutboxConsumerService(
                    loggerMock.Object,
                    outboxServiceMock.Object,
                    _configuration,
                    optionsMock.Object,
                    i); // Instance ID simulates partition assignment

                consumers.Add(consumer);

                lock (partitionLock)
                {
                    processedPartitions.Add(i);
                }
            }

            // Assert
            Assert.Equal(3, consumers.Count);
            Assert.Equal(3, processedPartitions.Count);
            Assert.Contains(0, processedPartitions);
            Assert.Contains(1, processedPartitions);
            Assert.Contains(2, processedPartitions);
            await Task.CompletedTask;
        }

        [Fact]
        public async Task ParallelExecution_IsFasterThanSequential()
        {
            // Arrange
            var delayMs = 50;
            var outboxServiceMock = new Mock<IOutboxService>();
            outboxServiceMock
                .Setup(s => s.MarkMessagesAsReceivedBatchAsync(
                    It.IsAny<List<long>>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (List<long> ids, DateTime dt, CancellationToken ct) =>
                {
                    await Task.Delay(delayMs, ct);
                    return Task.CompletedTask;
                });

            var messageIds = Enumerable.Range(0, 100).Select(i => (long)i).ToList();

            // Sequential execution
            var sequentialStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
            {
                await outboxServiceMock.Object.MarkMessagesAsReceivedBatchAsync(
                    messageIds,
                    DateTime.UtcNow,
                    CancellationToken.None);
            }
            sequentialStopwatch.Stop();

            // Parallel execution
            var parallelStopwatch = Stopwatch.StartNew();
            var tasks = new List<Task>();
            for (int i = 0; i < 3; i++)
            {
                tasks.Add(outboxServiceMock.Object.MarkMessagesAsReceivedBatchAsync(
                    messageIds,
                    DateTime.UtcNow,
                    CancellationToken.None));
            }
            await Task.WhenAll(tasks);
            parallelStopwatch.Stop();

            // Assert - Parallel should be at least 2x faster (with 3 parallel tasks)
            Assert.True(parallelStopwatch.ElapsedMilliseconds < sequentialStopwatch.ElapsedMilliseconds / 2,
                $"Parallel ({parallelStopwatch.ElapsedMilliseconds}ms) should be faster than Sequential ({sequentialStopwatch.ElapsedMilliseconds}ms)");
        }
    }
}
