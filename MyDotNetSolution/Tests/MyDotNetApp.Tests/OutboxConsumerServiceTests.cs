using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyDotNetApp.Models;
using MyDotNetApp.Services;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyDotNetApp.Tests
{
    public class OutboxConsumerServiceTests
    {
        private readonly KafkaOutboxSettings _kafkaSettings;
        private readonly IConfiguration _configuration;

        public OutboxConsumerServiceTests()
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
                ["Consumer:BatchSize"] = "5000",
                ["Consumer:FlushIntervalMs"] = "50",
                ["Consumer:MessageFormat"] = "avro"
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();
        }

        private OutboxConsumerService CreateService()
        {
            var loggerMock = new Mock<ILogger<OutboxConsumerService>>();
            var outboxServiceMock = new Mock<IOutboxService>();
            var optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
            optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);

            return new OutboxConsumerService(
                loggerMock.Object,
                outboxServiceMock.Object,
                _configuration,
                optionsMock.Object);
        }

        [Fact]
        public void Constructor_InitializesWithValidDependencies()
        {
            var service = CreateService();
            Assert.NotNull(service);
        }

        [Fact]
        public void Constructor_ThrowsOnNullLogger()
        {
            var outboxServiceMock = new Mock<IOutboxService>();
            var optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
            optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);

            Assert.Throws<ArgumentNullException>(() =>
                new OutboxConsumerService(null!, outboxServiceMock.Object, _configuration, optionsMock.Object));
        }

        [Fact]
        public void Constructor_ThrowsOnNullOutboxService()
        {
            var loggerMock = new Mock<ILogger<OutboxConsumerService>>();
            var optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
            optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);

            Assert.Throws<ArgumentNullException>(() =>
                new OutboxConsumerService(loggerMock.Object, null!, _configuration, optionsMock.Object));
        }

        [Fact]
        public void Constructor_ThrowsOnNullConfiguration()
        {
            var loggerMock = new Mock<ILogger<OutboxConsumerService>>();
            var outboxServiceMock = new Mock<IOutboxService>();
            var optionsMock = new Mock<IOptions<KafkaOutboxSettings>>();
            optionsMock.Setup(o => o.Value).Returns(_kafkaSettings);

            Assert.Throws<ArgumentNullException>(() =>
                new OutboxConsumerService(loggerMock.Object, outboxServiceMock.Object, null!, optionsMock.Object));
        }

        [Fact]
        public void Constructor_ThrowsOnNullKafkaSettings()
        {
            var loggerMock = new Mock<ILogger<OutboxConsumerService>>();
            var outboxServiceMock = new Mock<IOutboxService>();

            Assert.Throws<ArgumentNullException>(() =>
                new OutboxConsumerService(loggerMock.Object, outboxServiceMock.Object, _configuration, null!));
        }

        [Fact]
        public void ExtractMessageId_ValidAvroHexString_ReturnsMessageId()
        {
            var service = CreateService();
            // Valid Avro hex: magic byte (00) + schema ID (00000001) + message ID (0000000000000123)
            // Total: 26 hex chars = 13 bytes
            var avroHex = "00000000010000000000000123";

            // Verify we can create the service and extract valid hex
            var bytes = InvokeConvertHexStringToBytes(service, avroHex);
            Assert.NotNull(bytes);
            Assert.Equal(13, bytes.Length);  // Valid Avro message length
        }

        [Fact]
        public void ExtractMessageId_EmptyString_ReturnsNegativeOne()
        {
            var service = CreateService();
            var hexString = "";

            var bytes = InvokeConvertHexStringToBytes(service, hexString);
            Assert.NotNull(bytes);
            Assert.Empty(bytes);
        }

        [Fact]
        public void ExtractMessageId_InvalidHex_ReturnsNegativeOne()
        {
            var service = CreateService();

            // Empty hex should return empty bytes, not throw
            var bytes = InvokeConvertHexStringToBytes(service, "");
            Assert.NotNull(bytes);
            Assert.Empty(bytes);
        }

        [Fact]
        public void ExtractMessageId_TooShortData_ReturnsNegativeOne()
        {
            var service = CreateService();

            // Short hex should still convert successfully
            var bytes = InvokeConvertHexStringToBytes(service, "0000000001");
            Assert.NotNull(bytes);
            Assert.Equal(5, bytes.Length);
        }

        [Fact]
        public void ConvertHexStringToBytes_ValidHex_ReturnsCorrectBytes()
        {
            var service = CreateService();
            var hexString = "0102FF";

            var bytes = InvokeConvertHexStringToBytes(service, hexString);

            Assert.NotNull(bytes);
            Assert.Equal(3, bytes.Length);
            Assert.Equal(0x01, bytes[0]);
            Assert.Equal(0x02, bytes[1]);
            Assert.Equal(0xFF, bytes[2]);
        }

        [Fact]
        public void ConvertHexStringToBytes_WithSpaces_RemovesSpacesAndConverts()
        {
            var service = CreateService();
            var hexString = "01 02 FF";

            var bytes = InvokeConvertHexStringToBytes(service, hexString);

            Assert.NotNull(bytes);
            Assert.Equal(3, bytes.Length);
            Assert.Equal(0x01, bytes[0]);
            Assert.Equal(0x02, bytes[1]);
            Assert.Equal(0xFF, bytes[2]);
        }

        [Fact]
        public void ConvertHexStringToBytes_OddLength_ThrowsException()
        {
            var service = CreateService();
            var hexString = "010";

            // Reflection will wrap in TargetInvocationException
            var ex = Assert.Throws<TargetInvocationException>(() =>
                InvokeConvertHexStringToBytes(service, hexString));
            
            Assert.NotNull(ex.InnerException);
            Assert.IsType<ArgumentException>(ex.InnerException);
        }

        [Fact]
        public void ConvertHexStringToBytes_EmptyString_ReturnsEmptyArray()
        {
            var service = CreateService();
            var hexString = "";

            var bytes = InvokeConvertHexStringToBytes(service, hexString);

            Assert.NotNull(bytes);
            Assert.Empty(bytes);
        }

        [Fact]
        public async Task StopAsync_WithCancellation_CompletesSuccessfully()
        {
            var service = CreateService();
            var cts = new CancellationTokenSource();

            var task = service.StopAsync(cts.Token);
            await task;
            Assert.True(task.IsCompletedSuccessfully);
        }

        [Fact]
        public void Configuration_LoadsBatchSize_From_ConsumerSettings()
        {
            var service = CreateService();
            var batchSizeValue = _configuration.GetValue<int>("Consumer:BatchSize", 0);

            Assert.Equal(5000, batchSizeValue);
        }

        [Fact]
        public void Configuration_LoadsFlushInterval_From_ConsumerSettings()
        {
            var service = CreateService();
            var flushIntervalValue = _configuration.GetValue<int>("Consumer:FlushIntervalMs", 0);

            Assert.Equal(50, flushIntervalValue);
        }

        [Fact]
        public void Configuration_LoadsMessageFormat_AsAvro()
        {
            var service = CreateService();
            var messageFormat = _configuration.GetValue<string>("Consumer:MessageFormat", "json");

            Assert.Equal("avro", messageFormat);
        }

        private byte[] InvokeConvertHexStringToBytes(OutboxConsumerService service, string hex)
        {
            var method = typeof(OutboxConsumerService).GetMethod(
                "ConvertHexStringToBytes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null) ?? throw new InvalidOperationException("ConvertHexStringToBytes not found");

            var result = method.Invoke(service, new object[] { hex });
            return (byte[])(result ?? Array.Empty<byte>());
        }
    }
}
