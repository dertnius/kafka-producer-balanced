using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Linq;
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
        public async Task TriggerEndpoint_ReturnsOk_AndInvokesService()
        {
            // Arrange
            var processor = CreateProcessor();
            var controllerLogger = NullLogger<OutboxController>.Instance;
            var controller = new OutboxController(processor, controllerLogger);

            // Act
            var result = await controller.TriggerProcessing() as OkObjectResult;

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
            var result = await controller.TriggerProcessing();
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

            protected override async Task<List<MyDotNetApp.Services.OutboxMessage>> GetUnprocessedMessagesAsync(CancellationToken stoppingToken)
            {
                Interlocked.Increment(ref _fetchCount);
                // Return empty list to avoid database calls in tests
                await Task.CompletedTask;
                return new List<MyDotNetApp.Services.OutboxMessage>();
            }

            public Task RunPollAsync(CancellationToken token) => PollOutboxAsync(token);
        }

        [Fact]
        public async Task Host_Uses_Singleton_Instance_For_Controller_And_Background_Service()
        {
            var configValues = new Dictionary<string, string?>
            {
                {"ConnectionStrings:DefaultConnection", "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;"},
                {"KafkaOutboxSettings:BootstrapServers", "localhost:9092"},
                {"KafkaOutboxSettings:TopicName", "test-topic"},
                {"KafkaOutboxSettings:SchemaRegistryUrl", "http://localhost:8081"},
                {"KafkaOutboxSettings:PollingIntervalMs", "50"},
                {"KafkaOutboxSettings:BatchSize", "10"},
                {"KafkaOutboxSettings:MaxConcurrentProducers", "1"},
                {"KafkaOutboxSettings:MaxProducerBuffer", "10"},
                {"KafkaOutboxSettings:DatabaseConnectionPoolSize", "1"},
                {"Publishing:BatchSize", "10"},
                {"Publishing:FlushIntervalMs", "1000"}
            };

            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(configValues))
                .ConfigureServices((context, services) =>
                {
                    var startup = new Startup(context.Configuration);
                    startup.ConfigureServices(services);

                    // Remove unrelated hosted services (consumers, flushers, etc.)
                    services.RemoveAll<IHostedService>();

                    // Replace heavy dependencies with fakes to avoid external calls
                    services.RemoveAll<IKafkaService>();
                    services.AddSingleton(Mock.Of<IKafkaService>());
                    services.RemoveAll<IPublishBatchHandler>();
                    services.AddSingleton(Mock.Of<IPublishBatchHandler>());

                    // Swap the processor registration with a testable singleton
                    services.RemoveAll<OutboxProcessorServiceScaled>();
                    services.AddSingleton<OutboxProcessorServiceScaled>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<OutboxProcessorServiceScaled>>();
                        var settings = sp.GetRequiredService<IOptions<KafkaOutboxSettings>>();
                        var kafkaService = sp.GetRequiredService<IKafkaService>();
                        var publishBatchHandler = sp.GetRequiredService<IPublishBatchHandler>();
                        var connectionString = context.Configuration.GetConnectionString("DefaultConnection")!;
                        return new TestableOutboxProcessorSingleton(logger, settings, connectionString, kafkaService, publishBatchHandler);
                    });

                    // Ensure the hosted service uses the same singleton instance
                    services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<OutboxProcessorServiceScaled>());
                });

            using var host = hostBuilder.Build();
            await host.StartAsync();

            var processor = host.Services.GetRequiredService<OutboxProcessorServiceScaled>();
            var hostedProcessor = host.Services.GetServices<IHostedService>().OfType<OutboxProcessorServiceScaled>().Single();
            var controller = ActivatorUtilities.CreateInstance<OutboxController>(host.Services);

            var testProcessor = Assert.IsType<TestableOutboxProcessorSingleton>(processor);
            Assert.Same(processor, hostedProcessor); // hosted service and resolved singleton are the same instance
            Assert.True(testProcessor.Started); // hosted service was started

            // Act: trigger via controller, which should hit the same singleton instance
            await controller.TriggerProcessing();

            Assert.Equal(1, testProcessor.ManualTriggerCount);

            await host.StopAsync();
        }

        private sealed class TestableOutboxProcessorSingleton : OutboxProcessorServiceScaled
        {
            private readonly TaskCompletionSource<bool> _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TestableOutboxProcessorSingleton(
                ILogger<OutboxProcessorServiceScaled> logger,
                IOptions<KafkaOutboxSettings> kafkaSettings,
                string connectionString,
                IKafkaService kafkaService,
                IPublishBatchHandler publishBatchHandler) : base(logger, kafkaSettings, connectionString, kafkaService, publishBatchHandler)
            {
            }

            public bool Started => _startedTcs.Task.IsCompleted;

            protected override Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _startedTcs.TrySetResult(true);
                // Keep running until cancellation; no DB/Kafka work
                return Task.Delay(Timeout.Infinite, stoppingToken);
            }

            protected override Task<List<MyDotNetApp.Services.OutboxMessage>> GetUnprocessedMessagesAsync(CancellationToken cancellationToken)
            {
                // Override to avoid actual database calls
                return Task.FromResult(new List<MyDotNetApp.Services.OutboxMessage>());
            }

            public new long ManualTriggerCount => base.ManualTriggerCount;
        }
    }
}
