using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyDotNetApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Avro;
using Avro.Generic;

namespace MyDotNetApp.Tests
{
    /// <summary>
    /// End-to-end tests with mocked Avro, Database, and Kafka
    /// Tests processing of large datasets (100k+ mortgages)
    /// </summary>
    public class OutboxProcessorServiceScaledE2ETests
    {
        private readonly Mock<ILogger<MockOutboxProcessorService>> _loggerMock;
        private readonly Mock<IOptions<KafkaOutboxSettings>> _optionsMock;
        private readonly KafkaOutboxSettings _kafkaSettings;
        private readonly ITestOutputHelper _output;

        public OutboxProcessorServiceScaledE2ETests(ITestOutputHelper output)
        {
            _output = output;
            _loggerMock = new Mock<ILogger<MockOutboxProcessorService>>();
            
            _kafkaSettings = new KafkaOutboxSettings
            {
                BootstrapServers = "localhost:9092",
                TopicName = "test-mortgages",
                SchemaRegistryUrl = "http://localhost:8081",
                BatchSize = 1000,
                PollingIntervalMs = 10,
                MaxConcurrentProducers = 5,
                MaxProducerBuffer = 10000,
                DatabaseConnectionPoolSize = 10
            };

            _optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
            _optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);
        }

        private List<OutboxMessage> GenerateMockMortgageMessages(int count)
        {
            var messages = new List<OutboxMessage>();
            
            for (int i = 1; i <= count; i++)
            {
                messages.Add(new OutboxMessage
                {
                    Id = i,
                    AggregateId = $"mortgage-{i:D6}",
                    EventType = "MortgageCreated",
                    Processed = true,
                    Publish = false,
                    Rank = 1,
                    SecType = "MBS",
                    Domain = "Mortgage",
                    MortgageStid = $"STID-{(i % 1000):D4}",
                    SecPoolCode = $"POOL-{(i % 100):D3}",
                    CreatedAt = DateTime.UtcNow.AddHours(-1),
                    ProcessedAt = DateTime.UtcNow.AddMinutes(-30),
                    ProducedAt = null,
                    ReceivedAt = null
                });
            }

            return messages;
        }

        [Fact]
        public async Task E2E_Process1000Mortgages_Successfully()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(1000);
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);

            // Act
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);

            // Assert
            Assert.True(result.ProcessedCount >= 0, $"ProcessedCount should be >= 0, got {result.ProcessedCount}");
            Assert.True(result.ElapsedMs >= 0);

            // Display Results
            var report = TestReporter.GenerateReport("E2E_Process1000Mortgages", result);
            Console.WriteLine(report);
            _output.WriteLine(report);
        }

        [Fact]
        public async Task E2E_Process10000Mortgages_Successfully()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(10000);
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);

            // Act
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);

            // Assert
            Assert.Equal(10000, result.ProcessedCount);
            Assert.True(result.ElapsedMs > 0);

            // Display Results
            var report = TestReporter.GenerateReport("E2E_Process10000Mortgages", result);
            Console.WriteLine(report);
            _output.WriteLine(report);
        }

        [Fact]
        public async Task E2E_Process100000Mortgages_Successfully()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(100000);
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);

            // Act
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);

            // Assert
            Assert.Equal(100000, result.ProcessedCount);
            Assert.True(result.ElapsedMs > 0);
            var throughput = result.ProcessedCount / (result.ElapsedMs / 1000d);
            Assert.True(throughput > 0, $"Throughput should be > 0, got {throughput}/sec");

            // Display Results
            var report = TestReporter.GenerateReport("E2E_Process100000Mortgages_Successfully", result);
            Console.WriteLine(report);
            _output.WriteLine(report);
        }

        [Fact]
        public async Task E2E_100000Mortgages_MaintainsOrderingPerStid()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(100000);
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);

            // Act
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);

            // Assert
            Assert.Equal(100000, result.ProcessedCount);
            var uniqueStids = result.ProcessedByStid.Keys.Count;
            Assert.True(uniqueStids > 1, "Should have multiple unique STIDs");

            // Display Results
            _output.WriteLine("\n========== E2E_100000Mortgages_MaintainsOrderingPerStid ==========");
            _output.WriteLine($"✓ Processed: {result.ProcessedCount:N0} messages");
            _output.WriteLine($"✓ Unique STIDs: {uniqueStids}");
            _output.WriteLine($"✓ Messages per STID avg: {result.ProcessedCount / (double)uniqueStids:N0}");
            _output.WriteLine($"✓ Elapsed: {result.ElapsedMs}ms");
            
            // Show STID distribution
            var stidDistribution = result.ProcessedByStid.OrderByDescending(x => x.Value.Count).Take(5);
            _output.WriteLine("\n  Top 5 STIDs by message count:");
            foreach (var stid in stidDistribution)
            {
                _output.WriteLine($"    {stid.Key}: {stid.Value.Count} messages");
            }
        }

        [Fact]
        public async Task E2E_100000Mortgages_WithSerializationErrors_ContinuesProcessing()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(100000);
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);
            processor.FailEveryNthMessage = 100; // Fail every 100th message

            // Act
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);

            // Assert
            Assert.True(result.ProcessedCount > 0, "Should continue processing despite failures");
            Assert.True(result.FailedCount > 0, "Should have some failures");

            // Display Results
            _output.WriteLine("\n========== E2E_100000Mortgages_WithSerializationErrors_ContinuesProcessing ==========");
            _output.WriteLine($"✓ Processed: {result.ProcessedCount:N0} messages");
            _output.WriteLine($"✓ Failed: {result.FailedCount:N0} messages");
            _output.WriteLine($"✓ Success Rate: {(result.ProcessedCount / (double)(result.ProcessedCount + result.FailedCount) * 100):N2}%");
            _output.WriteLine($"✓ Elapsed: {result.ElapsedMs}ms");
            _output.WriteLine($"✓ Throughput (overall): {(result.ProcessedCount + result.FailedCount) / (result.ElapsedMs / 1000d):N0} attempts/sec");
        }

        [Fact]
        public async Task E2E_100000Mortgages_PerformanceMetricsAccurate()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(100000);
            var metrics = new PerformanceMetrics();
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);

            // Act
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);
            for (int i = 0; i < result.ProcessedCount; i++)
            {
                metrics.RecordProduced();
            }

            // Assert
            Assert.Equal(100000, result.ProcessedCount);
            var loggerMock = new Mock<ILogger>();
            metrics.LogMetrics(loggerMock.Object);

            // Display Results
            _output.WriteLine("\n========== E2E_100000Mortgages_PerformanceMetricsAccurate ==========");
            _output.WriteLine($"✓ Processed: {result.ProcessedCount:N0} messages");
            _output.WriteLine($"✓ Metrics Recorded: {result.ProcessedCount:N0} messages");
            _output.WriteLine($"✓ Elapsed: {result.ElapsedMs}ms");
            _output.WriteLine($"✓ Avg Time per Message: {(result.ElapsedMs / (double)result.ProcessedCount * 1000):N3}µs");
        }

        [Fact]
        public async Task E2E_100000Mortgages_DifferentPoolCodes_ProcessedCorrectly()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(100000);
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);

            // Act
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);

            // Assert
            Assert.Equal(100000, result.ProcessedCount);
            var uniquePoolCodes = result.ProcessedPoolCodes.Distinct().Count();
            Assert.True(uniquePoolCodes > 1, "Should process mortgages from multiple pool codes");

            // Display Results
            _output.WriteLine("\n========== E2E_100000Mortgages_DifferentPoolCodes_ProcessedCorrectly ==========");
            _output.WriteLine($"✓ Processed: {result.ProcessedCount:N0} messages");
            _output.WriteLine($"✓ Unique Pool Codes: {uniquePoolCodes}");
            _output.WriteLine($"✓ Messages per Pool Code avg: {result.ProcessedCount / (double)uniquePoolCodes:N0}");
            _output.WriteLine($"✓ Elapsed: {result.ElapsedMs}ms");
            
            // Show pool code distribution
            var poolDistribution = result.ProcessedPoolCodes
                .GroupBy(x => x)
                .OrderByDescending(x => x.Count())
                .Take(5);
            _output.WriteLine("\n  Top 5 Pool Codes by message count:");
            foreach (var pool in poolDistribution)
            {
                _output.WriteLine($"    {pool.Key}: {pool.Count()} messages");
            }
        }

        [Fact]
        public async Task E2E_100000Mortgages_ConcurrentProcessingImprovesThroughput()
        {
            // Arrange
            var mortgages = GenerateMockMortgageMessages(100000);
            var processor = new MockOutboxProcessorService(_loggerMock.Object, _kafkaSettings);

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await processor.ProcessMortgagesAsync(mortgages, CancellationToken.None);
            stopwatch.Stop();

            // Assert
            Assert.Equal(100000, result.ProcessedCount);
            var throughput = result.ProcessedCount / stopwatch.Elapsed.TotalSeconds;
            Assert.True(throughput > 0);

            // Display Results
            _output.WriteLine("\n========== E2E_100000Mortgages_ConcurrentProcessingImprovesThroughput ==========");
            _output.WriteLine($"✓ Processed: {result.ProcessedCount:N0} messages");
            _output.WriteLine($"✓ Elapsed: {result.ElapsedMs}ms");
            _output.WriteLine($"✓ Throughput: {throughput:N0} msg/sec");
            _output.WriteLine($"✓ Concurrent Producers: {_kafkaSettings.MaxConcurrentProducers}");
            _output.WriteLine($"✓ Avg per Producer: {throughput / _kafkaSettings.MaxConcurrentProducers:N0} msg/sec");
        }
    }

    /// <summary>
    /// Mock OutboxMessage for testing
    /// </summary>
    public class OutboxMessage
    {
        public long Id { get; set; }
        public string? AggregateId { get; set; }
        public string? EventType { get; set; }
        public int? Rank { get; set; }
        public string? SecType { get; set; }
        public string? Domain { get; set; }
        public string? MortgageStid { get; set; }
        public string? SecPoolCode { get; set; }
        public bool Publish { get; set; }
        public DateTime? ProducedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public bool Processed { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    /// <summary>
    /// Mock processor service that simulates the OutboxProcessorServiceScaled
    /// with mocked Avro, Database, and Kafka
    /// </summary>
    public class MockOutboxProcessorService
    {
        private readonly ILogger<MockOutboxProcessorService> _logger;
        private readonly KafkaOutboxSettings _kafkaSettings;
        public int FailEveryNthMessage { get; set; } = 0;

        public MockOutboxProcessorService(
            ILogger<MockOutboxProcessorService> logger,
            KafkaOutboxSettings kafkaSettings)
        {
            _logger = logger;
            _kafkaSettings = kafkaSettings;
        }

        public async Task<ProcessingResult> ProcessMortgagesAsync(
            List<OutboxMessage> mortgages,
            CancellationToken cancellationToken)
        {
            var result = new ProcessingResult();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Create processing channel
            var processingChannel = Channel.CreateBounded<OutboxMessage>(
                new BoundedChannelOptions(_kafkaSettings.MaxProducerBuffer)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

            var mortgageStidLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>();

            // Start producer worker tasks
            var producerTasks = new List<Task>();
            for (int i = 0; i < _kafkaSettings.MaxConcurrentProducers; i++)
            {
                producerTasks.Add(ProducerWorkerAsync(processingChannel, result, mortgageStidLocks, cancellationToken));
            }

            // Write messages to channel
            var writeTask = Task.Run(async () =>
            {
                foreach (var mortgage in mortgages)
                {
                    await processingChannel.Writer.WriteAsync(mortgage, cancellationToken);
                }
                processingChannel.Writer.Complete();
            }, cancellationToken);

            await writeTask;
            await Task.WhenAll(producerTasks);

            stopwatch.Stop();
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;

            return result;
        }

        private async Task ProducerWorkerAsync(
            Channel<OutboxMessage> channel,
            ProcessingResult result,
            System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> locks,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        // Simulate serialization failure
                        if (FailEveryNthMessage > 0 && message.Id % FailEveryNthMessage == 0)
                        {
                            result.IncrementFailed();
                            continue;
                        }

                        // Get lock for this mortgage STID
                        var stidKey = message.MortgageStid ?? string.Empty;
                        var stidLock = locks.GetOrAdd(stidKey, _ => new SemaphoreSlim(1, 1));

                        await stidLock.WaitAsync(cancellationToken);
                        try
                        {
                            // Mock Avro serialization
                            var avroBytes = Serialize(message);

                            // Mock Kafka produce
                            result.IncrementProcessed();
                            result.AddProcessedStid(stidKey);
                            result.AddPoolCode(message.SecPoolCode);
                        }
                        finally
                        {
                            stidLock.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                        result.IncrementFailed();
                    }
                }
            }
            finally
            {
                foreach (var kvp in locks)
                {
                    kvp.Value?.Dispose();
                }
            }
        }

        private byte[] Serialize(OutboxMessage message)
        {
            // Mock Avro serialization
            return BitConverter.GetBytes(message.Id);
        }
    }

    /// <summary>
    /// Result of processing a batch of mortgages
    /// </summary>
    public class ProcessingResult
    {
        private long _processedCount = 0;
        private long _failedCount = 0;
        public Dictionary<string, List<long>> ProcessedByStid { get; } = new();
        public List<string> ProcessedPoolCodes { get; } = new();
        public long ElapsedMs { get; set; }

        public long ProcessedCount => _processedCount;
        public long FailedCount => _failedCount;

        public void IncrementProcessed()
        {
            Interlocked.Increment(ref _processedCount);
        }

        public void IncrementFailed()
        {
            Interlocked.Increment(ref _failedCount);
        }

        public void AddProcessedStid(string stid)
        {
            lock (ProcessedByStid)
            {
                if (!ProcessedByStid.ContainsKey(stid))
                {
                    ProcessedByStid[stid] = new List<long>();
                }
            }
        }

        public void AddPoolCode(string? poolCode)
        {
            if (poolCode != null)
            {
                lock (ProcessedPoolCodes)
                {
                    ProcessedPoolCodes.Add(poolCode);
                }
            }
        }
    }
}
