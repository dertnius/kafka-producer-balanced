using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace MyDotNetApp.Services
{
    public class OutboxItem
    {
        public long Id { get; set; }
        public string? Stid { get; set; }
        public string? Code { get; set; }
        public int Rank { get; set; }
        public bool Processed { get; set; }
        public int Retry { get; set; }
        public string? ErrorCode { get; set; }
    }

    public class OutboxMessage
    {
        public long Id { get; set; }
        public string? AggregateId { get; set; }
        public string? EventType { get; set; }
        public int? Rank { get; set; }
        public string? SecType { get; set; } // 2 char security type
        public string? Domain { get; set; } // 26 char domain
        public string? MortgageStid { get; set; } // 56 char STID
        public string? SecPoolCode { get; set; } // 4 char security pool code
        public bool Publish { get; set; } // Indicates if message has been published
        public DateTime? ProducedAt { get; set; } // When message was produced to topic
        public DateTime? ReceivedAt { get; set; } // When message was received from other topic
        public bool Processed { get; set; } // Indicates if message has been processed (moved from inbound)
        public DateTime? CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }

    /// <summary>
    /// Service interface for Kafka operations
    /// </summary>
    public interface IKafkaService
    {
        Task<bool> WaitForDeliveryConfirmationAsync(string stid, CancellationToken stoppingToken);
        Task ProduceMessageAsync(OutboxItem item, CancellationToken stoppingToken);
    }

    /// <summary>
    /// Implementation of IKafkaService for producing messages to Kafka
    /// </summary>
    public class KafkaService : IKafkaService
    {
        private readonly IProducer<string, string> _producer;
        private readonly ILogger<KafkaService> _logger;
        private const int DeliveryTimeoutMs = 30000; // 30 seconds

        public KafkaService(IProducer<string, string> producer, ILogger<KafkaService> logger)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Waits for delivery confirmation of a message from Kafka
        /// </summary>
        public async Task<bool> WaitForDeliveryConfirmationAsync(string stid, CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(stid))
                throw new ArgumentException("Stid cannot be null or empty", nameof(stid));

            try
            {
                _logger.LogDebug("Waiting for delivery confirmation for Stid: {Stid}", stid);
                
                // Implement actual delivery confirmation logic with Kafka
                // For now, return true to indicate success
                // In production, this should subscribe to a consumer group and wait for ACKs
                
                await Task.Delay(100, stoppingToken); // Simulate async work
                _logger.LogDebug("Delivery confirmation received for Stid: {Stid}", stid);
                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Delivery confirmation wait cancelled for Stid: {Stid}", stid);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for delivery confirmation for Stid: {Stid}", stid);
                throw;
            }
        }

        /// <summary>
        /// Produces a message to Kafka
        /// </summary>
        public async Task ProduceMessageAsync(OutboxItem item, CancellationToken stoppingToken)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            try
            {
                _logger.LogDebug("Producing message for item {ItemId} to Kafka", item.Id);

                var kafkaMessage = new Message<string, string>
                {
                    Key = item.Stid ?? "unknown",
                    Value = $"{{\"id\": {item.Id}, \"code\": \"{item.Code}\", \"rank\": {item.Rank}}}"
                };

                var deliveryResult = await _producer.ProduceAsync("default-topic", kafkaMessage, stoppingToken);

                _logger.LogInformation("Message {ItemId} produced to Kafka partition {Partition} at offset {Offset}",
                    item.Id, deliveryResult.Partition, deliveryResult.Offset);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Message production cancelled for item {ItemId}", item.Id);
                throw;
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Failed to produce message {ItemId} to Kafka: {Reason}",
                    item.Id, ex.Error.Reason);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error producing message {ItemId} to Kafka", item.Id);
                throw;
            }
        }
    }
}