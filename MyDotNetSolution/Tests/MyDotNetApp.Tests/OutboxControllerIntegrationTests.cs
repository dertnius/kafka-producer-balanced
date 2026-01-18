using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MyDotNetApp.Controllers;
using MyDotNetApp.Models;
using MyDotNetApp.Services;
using Xunit;

namespace MyDotNetApp.Tests
{
    public class OutboxControllerIntegrationTests
    {
        private static KafkaOutboxSettings CreateSettings(int pollingIntervalMs = 500)
        {
            return new KafkaOutboxSettings
            {
                BootstrapServers = "localhost:9092",
                TopicName = "test-topic",
                SchemaRegistryUrl = "http://localhost:8081",
                BatchSize = 10,
                PollingIntervalMs = pollingIntervalMs,
                MaxConcurrentProducers = 1,
                MaxProducerBuffer = 10,
                DatabaseConnectionPoolSize = 1
            };
        }

        private static TestableOutboxProcessor CreateProcessor(int pollingIntervalMs = 500)
        {
            var logger = NullLogger<OutboxProcessorServiceScaled>.Instance;
            var options = Options.Create(CreateSettings(pollingIntervalMs));
            var kafkaMock = new Mock<IKafkaService>();
            var publishMock = new Mock<IPublishBatchHandler>();
            return new TestableOutboxProcessor(logger, options, "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;", kafkaMock.Object, publishMock.Object);
        }

        [Fact]
        public void TriggerEndpoint_ReturnsOk_AndInvokesService()
        {
            // Arrange
            var processor = CreateProcessor();
            var controllerLogger = NullLogger<OutboxController>.Instance;
            var controller = new OutboxController(processor, controllerLogger);

            // Act
            var result = controller.TriggerProcessing() as OkObjectResult;

            // Assert
            Assert.NotNull(result);
            var payload = result!.Value;
            Assert.NotNull(payload);

            var successProp = payload.GetType().GetProperty("success");
            var timestampProp = payload.GetType().GetProperty("timestamp");

            Assert.NotNull(successProp);
            Assert.NotNull(timestampProp);
            Assert.True((bool)successProp!.GetValue(payload)!);
            Assert.NotNull(timestampProp!.GetValue(payload));
        }

        [Fact]
        public async Task ManualTrigger_DoesNotBlock_TimerPolling()
        {
            // Arrange: long timer (500ms) so without trigger it would not poll before cancellation
            var processor = CreateProcessor(pollingIntervalMs: 500);
            var controllerLogger = NullLogger<OutboxController>.Instance;
            var controller = new OutboxController(processor, controllerLogger);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            // Act: start polling loop and trigger manually
            var pollTask = processor.RunPollAsync(cts.Token);
            await Task.Delay(50, CancellationToken.None); // allow delay to start
            var result = controller.TriggerProcessing();
            await pollTask; // should complete when token cancels

            // Assert
            Assert.IsType<OkObjectResult>(result);
            Assert.True(processor.FetchCount >= 1); // trigger woke the loop and performed a fetch
        }

        public sealed class TestableOutboxProcessor : OutboxProcessorServiceScaled
        {
            private int _fetchCount;
            public int FetchCount => _fetchCount;

            public TestableOutboxProcessor(
                ILogger<OutboxProcessorServiceScaled> logger,
                IOptions<KafkaOutboxSettings> kafkaSettings,
                string connectionString,
                IKafkaService kafkaService,
                IPublishBatchHandler publishBatchHandler) : base(logger, kafkaSettings, connectionString, kafkaService, publishBatchHandler)
            {
            }

            protected override async Task PollOutboxAsync(CancellationToken stoppingToken)
            {
                Interlocked.Increment(ref _fetchCount);
                await base.PollOutboxAsync(stoppingToken);
            }

            public Task RunPollAsync(CancellationToken token) => PollOutboxAsync(token);
        }
    }
}
