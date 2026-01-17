using Confluent.Kafka;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyDotNetApp.Services
{
    public class HomeBackgroundService : BackgroundService
    {
        private readonly ILogger<HomeBackgroundService> _logger;
        private readonly IDbConnection _dbConnection;
        private readonly ConcurrentQueue<IProducer<string, string>> _producerPool;
        private readonly IConfiguration _configuration;
        private readonly int _producerPoolSize;
        private readonly int _batchSize;
        private readonly int _maxRetryCount;

        public HomeBackgroundService(ILogger<HomeBackgroundService> logger, IDbConnection dbConnection, IConfiguration configuration)
        {
            _logger = logger;
            _dbConnection = dbConnection;
            _configuration = configuration;

            // Load configuration values
            _producerPoolSize = _configuration.GetValue<int>("Kafka:ProducerPoolSize", 5);
            _batchSize = _configuration.GetValue<int>("Processing:BatchSize", 100);
            _maxRetryCount = _configuration.GetValue<int>("Processing:MaxRetryCount", 5);

            // Initialize producer pool
            _producerPool = new ConcurrentQueue<IProducer<string, string>>(CreateProducerPool(_producerPoolSize));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Service started.");

            using var consumer = CreateConsumer();
            consumer.Subscribe("topic-2");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var messages = await FetchMessagesAsync(stoppingToken);
                    if (!messages.Any())
                    {
                        await Task.Delay(5000, stoppingToken); // Polling interval
                        continue;
                    }

                    foreach (var group in messages.GroupBy(m => m.Stid))
                    {
                        await ProcessMessageGroupAsync(group.ToList(), consumer, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in processing loop.");
                }
            }

            _logger.LogInformation("Service stopping.");
        }

        private async Task<List<OutboxItem>> FetchMessagesAsync(CancellationToken stoppingToken)
        {
            return (await _dbConnection.QueryAsync<OutboxItem>(
                @"SELECT TOP (@BatchSize) Id, Stid, Code, Rank, Processed, Retry, ErrorCode 
                  FROM Outbox 
                  WHERE Processed = 0 
                  ORDER BY Stid, CASE WHEN Code IS NULL THEN 0 ELSE 1 END, Id",
                new { BatchSize = _batchSize })).ToList();
        }

        private async Task ProcessMessageGroupAsync(List<OutboxItem> group, IConsumer<string, string> consumer, CancellationToken stoppingToken)
        {
            foreach (var message in group)
            {
                try
                {
                    // Mark message as processing
                    await MarkAsProcessingAsync(message.Id);

                    if (string.IsNullOrEmpty(message.Code))
                    {
                        // Handle empty Code message
                        await SendMessageToKafkaAsync(message, "topic-2", stoppingToken);
                        await WaitForAcknowledgmentAsync(message.Stid, consumer, stoppingToken);
                    }
                    else
                    {
                        // Handle non-empty Code message
                        await SendMessageToKafkaAsync(message, "outbox-topic", stoppingToken);
                    }

                    // Mark message as processed
                    await MarkAsProcessedAsync(message.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message ID: {Id}", message.Id);
                    await HandleFailedMessageAsync(message, stoppingToken);
                    break; // Stop processing further messages in the group
                }
            }
        }

        private async Task SendMessageToKafkaAsync(OutboxItem message, string topic, CancellationToken stoppingToken)
        {
            var producer = GetProducerFromPool();
            try
            {
                var kafkaMessage = new Message<string, string>
                {
                    Key = message.Stid ?? "unknown",
                    Value = $"Message ID: {message.Id}, Code: {message.Code}, Rank: {message.Rank}"
                };

                await producer.ProduceAsync(topic, kafkaMessage, stoppingToken);
                _logger.LogInformation("Message ID: {Id} sent to {Topic}.", message.Id, topic);
            }
            finally
            {
                ReturnProducerToPool(producer);
            }
        }

        private Task WaitForAcknowledgmentAsync(string? stid, IConsumer<string, string> consumer, CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(stid))
                throw new ArgumentException("Stid cannot be null or empty", nameof(stid));
            _logger.LogInformation("Waiting for acknowledgment for Stid: {Stid}", stid);

            while (!stoppingToken.IsCancellationRequested)
            {
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult.Message.Key == stid)
                {
                    _logger.LogInformation("Acknowledgment received for Stid: {Stid}", stid);
                    return Task.CompletedTask;
                }
            }

            throw new Exception($"Acknowledgment not received for Stid: {stid}");
        }

        private async Task MarkAsProcessingAsync(long id)
        {
            await _dbConnection.ExecuteAsync(
                "UPDATE Outbox SET Processing = 1 WHERE Id = @Id AND Processed = 0",
                new { Id = id });
        }

        private async Task MarkAsProcessedAsync(long id)
        {
            await _dbConnection.ExecuteAsync(
                "UPDATE Outbox SET Processed = 1, Processing = 0 WHERE Id = @Id",
                new { Id = id });
        }

        private Task HandleFailedMessageAsync(OutboxItem message, CancellationToken cancellationToken)
        {
            if (message.Retry >= _maxRetryCount)
            {
                _logger.LogError("Message ID: {Id} moved to Dead Letter Queue.", message.Id);
                return _dbConnection.ExecuteAsync(
                    "INSERT INTO DeadLetterQueue (Id, Stid, Code, Rank, ErrorCode, CreatedAt) VALUES (@Id, @Stid, @Code, @Rank, @ErrorCode, @CreatedAt)",
                    new { message.Id, message.Stid, message.Code, message.Rank, message.ErrorCode, CreatedAt = DateTime.UtcNow });
            }
            else
            {
                _logger.LogWarning("Retrying message ID: {Id}. Retry count: {Retry}", message.Id, message.Retry + 1);
                return _dbConnection.ExecuteAsync(
                    "UPDATE Outbox SET Retry = Retry + 1 WHERE Id = @Id",
                    new { message.Id });
            }
        }

        private IConsumer<string, string> CreateConsumer()
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _configuration.GetValue<string>("Kafka:BootstrapServers"),
                GroupId = _configuration.GetValue<string>("Kafka:GroupId", "delivery-confirmation-group"),
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            return new ConsumerBuilder<string, string>(config).Build();
        }

        private List<IProducer<string, string>> CreateProducerPool(int poolSize)
        {
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = _configuration.GetValue<string>("Kafka:BootstrapServers")
            };

            return Enumerable.Range(0, poolSize)
                .Select(_ => new ProducerBuilder<string, string>(producerConfig).Build())
                .ToList();
        }

        private IProducer<string, string> GetProducerFromPool()
        {
            if (_producerPool.TryDequeue(out var producer))
            {
                return producer;
            }

            throw new InvalidOperationException("No available producers in the pool.");
        }

        private void ReturnProducerToPool(IProducer<string, string> producer)
        {
            _producerPool.Enqueue(producer);
        }
    }
}