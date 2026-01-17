using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyDotNetApp.Services
{
    /// <summary>
    /// Background service that processes outbox messages and sends them to Kafka
    /// </summary>
    public class MessaggingService : BackgroundService
    {
        private readonly ILogger<MessaggingService> _logger;
        private readonly IOutboxService _outboxService;
        private readonly IKafkaService _kafkaService;
        private readonly IConfiguration _configuration;
        private readonly int _batchSize;
        private readonly int _maxParallelStidGroups;
        private readonly int _pollIntervalMs;
        private readonly int _maxRetries;

        public MessaggingService(
            ILogger<MessaggingService> logger,
            IOutboxService outboxService,
            IKafkaService kafkaService,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _outboxService = outboxService ?? throw new ArgumentNullException(nameof(outboxService));
            _kafkaService = kafkaService ?? throw new ArgumentNullException(nameof(kafkaService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _batchSize = _configuration.GetValue<int>("Processing:BatchSize", 1000);
            _maxParallelStidGroups = _configuration.GetValue<int>("Processing:MaxParallelStidGroups", 10);
            _pollIntervalMs = _configuration.GetValue<int>("Processing:PollIntervalMs", 5000);
            _maxRetries = _configuration.GetValue<int>("Processing:MaxRetries", 3);

            ValidateConfiguration();
        }

        private void ValidateConfiguration()
        {
            if (_batchSize <= 0)
                throw new InvalidOperationException("Processing:BatchSize must be greater than 0");
            if (_maxParallelStidGroups <= 0)
                throw new InvalidOperationException("Processing:MaxParallelStidGroups must be greater than 0");
            if (_pollIntervalMs <= 0)
                throw new InvalidOperationException("Processing:PollIntervalMs must be greater than 0");
            if (_maxRetries < 0)
                throw new InvalidOperationException("Processing:MaxRetries cannot be negative");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MessaggingService is starting.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogDebug("Reading from Outbox table at: {Time}", DateTimeOffset.Now);

                        var offset = 0;
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            try
                            {
                                var outboxItems = await _outboxService.GetPendingOutboxItemsAsync(
                                    offset, _batchSize, stoppingToken);

                                if (!outboxItems.Any())
                                {
                                    _logger.LogDebug("No more outbox items to process at offset {Offset}", offset);
                                    break;
                                }

                                _logger.LogInformation("Processing {Count} outbox items from offset {Offset}", 
                                    outboxItems.Count, offset);

                                var groupedItems = outboxItems.GroupBy(item => item.Stid).ToList();

                                var throttler = new SemaphoreSlim(_maxParallelStidGroups);
                                var processingTasks = groupedItems.Select(async group =>
                                {
                                    await throttler.WaitAsync(stoppingToken);
                                    try
                                    {
                                        await ProcessStidGroupAsync(group.ToList(), stoppingToken);
                                    }
                                    finally
                                    {
                                        throttler.Release();
                                    }
                                });

                                await Task.WhenAll(processingTasks);

                                offset += _batchSize;
                            }
                            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                            {
                                _logger.LogInformation("Processing batch cancelled");
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing batch at offset {Offset}", offset);
                                // Continue with next batch
                                offset += _batchSize;
                            }
                        }

                        await Task.Delay(_pollIntervalMs, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error in MessaggingService, retrying...");
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            finally
            {
                _logger.LogInformation("MessaggingService is stopping.");
            }
        }

        private async Task ProcessStidGroupAsync(List<OutboxItem> group, CancellationToken stoppingToken)
        {
            if (!group.Any())
            {
                _logger.LogWarning("Received empty group in ProcessStidGroupAsync");
                return;
            }

            var stid = group.First().Stid;
            _logger.LogInformation("Processing {Count} messages for Stid: {Stid}", group.Count, stid);

            try
            {
                var emptyCodeMessages = group
                    .Where(item => string.IsNullOrEmpty(item.Code))
                    .OrderBy(item => item.Id)
                    .ToList();

                var nonEmptyCodeMessages = group
                    .Where(item => !string.IsNullOrEmpty(item.Code))
                    .OrderBy(item => item.Id)
                    .ToList();

                // Process empty code messages
                foreach (var item in emptyCodeMessages)
                {
                    try
                    {
                        _logger.LogDebug("Processing Outbox item with empty Code and ID: {Id}", item.Id);

                        await _outboxService.ProcessEmptyCodeMessageAsync(item, stoppingToken);

                        if (string.IsNullOrEmpty(item.Stid))
                            throw new InvalidOperationException($"Item {item.Id} has no Stid");

                        var isDelivered = await _kafkaService.WaitForDeliveryConfirmationAsync(
                            item.Stid, stoppingToken);

                        if (!isDelivered)
                        {
                            await _outboxService.HandleFailedMessageAsync(
                                item, "DELIVERY_FAILED", "Kafka delivery confirmation failed", stoppingToken);
                            continue;
                        }

                        await _outboxService.MarkMessageAsProcessedAsync(item.Id, stoppingToken);
                        _logger.LogDebug("Message {MessageId} processed successfully", item.Id);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing empty code message {MessageId}", item.Id);
                        await _outboxService.HandleFailedMessageAsync(
                            item, "PROCESSING_ERROR", ex.Message, stoppingToken);
                    }
                }

                // Process non-empty code messages in parallel
                var producerTasks = nonEmptyCodeMessages.Select(async item =>
                {
                    try
                    {
                        _logger.LogDebug("Producing message for Outbox item with ID: {Id}", item.Id);

                        await _kafkaService.ProduceMessageAsync(item, stoppingToken);

                        await _outboxService.MarkMessageAsProcessedAsync(item.Id, stoppingToken);
                        _logger.LogDebug("Message {MessageId} produced successfully", item.Id);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error producing message {MessageId}", item.Id);
                        await _outboxService.HandleFailedMessageAsync(
                            item, "KAFKA_PRODUCER_ERROR", ex.Message, stoppingToken);
                    }
                });

                await Task.WhenAll(producerTasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error processing Stid: {Stid}. Marking all records as not published", stid);
                try
                {
                    if (!string.IsNullOrEmpty(stid))
                    {
                        await _outboxService.MarkStidAsNotPublishedAsync(stid, stoppingToken);
                    }
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Failed to mark Stid {Stid} as not published", stid);
                }
            }
        }
    }
}