using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyDotNetApp.Services
{
    /// <summary>
    /// High-performance batch handler for publishing messages
    /// Optimized for pushing to Kafka and updating publish status
    /// </summary>
    public interface IPublishBatchHandler
    {
        Task EnqueueForPublishAsync(long messageId, CancellationToken cancellationToken);
        Task FlushAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Optimized batch publisher that accumulates message IDs and performs bulk updates
    /// Uses configurable batch size for optimal database performance
    /// </summary>
    public class PublishBatchHandler : IPublishBatchHandler
    {
        private readonly IOutboxService _outboxService;
        private readonly ILogger<PublishBatchHandler> _logger;
        private readonly int _batchSize;
        private readonly int _flushIntervalMs;
        
        private readonly object _lockObj = new object();
        private readonly List<long> _pendingMessageIds;
        private readonly Stopwatch _timer;

        public PublishBatchHandler(
            IOutboxService outboxService,
            ILogger<PublishBatchHandler> logger,
            int batchSize = 5000,
            int flushIntervalMs = 1000)
        {
            _outboxService = outboxService ?? throw new ArgumentNullException(nameof(outboxService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _batchSize = batchSize > 0 ? batchSize : throw new ArgumentException("Batch size must be greater than 0");
            _flushIntervalMs = flushIntervalMs > 0 ? flushIntervalMs : throw new ArgumentException("Flush interval must be greater than 0");
            
            _pendingMessageIds = new List<long>(_batchSize);
            _timer = Stopwatch.StartNew();
        }

        /// <summary>
        /// Enqueue a message ID for batch publishing
        /// Automatically flushes when batch size is reached
        /// </summary>
        public Task EnqueueForPublishAsync(long messageId, CancellationToken cancellationToken)
        {
            lock (_lockObj)
            {
                _pendingMessageIds.Add(messageId);
                
                // Auto-flush on batch size reached
                if (_pendingMessageIds.Count >= _batchSize)
                {
                    var toFlush = new List<long>(_pendingMessageIds);
                    _pendingMessageIds.Clear();
                    
                    // Fire and forget with error logging
                    _ = Task.Run(() => FlushInternalAsync(toFlush, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Flush any pending message IDs to database
        /// </summary>
        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            List<long> toFlush;
            
            lock (_lockObj)
            {
                if (_pendingMessageIds.Count == 0)
                    return;

                toFlush = new List<long>(_pendingMessageIds);
                _pendingMessageIds.Clear();
            }

            await FlushInternalAsync(toFlush, cancellationToken);
        }

        private async Task FlushInternalAsync(List<long> messageIds, CancellationToken cancellationToken)
        {
            if (messageIds.Count == 0)
                return;

            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                await _outboxService.MarkMessagesAsPublishedBatchAsync(messageIds, cancellationToken);
                
                stopwatch.Stop();
                var rate = (messageIds.Count / stopwatch.Elapsed.TotalSeconds);
                
                _logger.LogInformation(
                    "Batch published {MessageCount} messages in {ElapsedMs}ms ({Rate:F0} msgs/sec)",
                    messageIds.Count, stopwatch.ElapsedMilliseconds, rate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing {MessageCount} messages to publish", messageIds.Count);
                // Re-enqueue on failure for retry
                foreach (var id in messageIds)
                {
                    lock (_lockObj)
                    {
                        if (_pendingMessageIds.Count < _batchSize)
                        {
                            _pendingMessageIds.Add(id);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Background service for periodically flushing pending publish updates
    /// Ensures all messages are marked as published within configurable interval
    /// </summary>
    public class PublishFlushBackgroundService : BackgroundService
    {
        private readonly IPublishBatchHandler _batchHandler;
        private readonly ILogger<PublishFlushBackgroundService> _logger;
        private readonly int _flushIntervalMs;

        public PublishFlushBackgroundService(
            IPublishBatchHandler batchHandler,
            ILogger<PublishFlushBackgroundService> logger,
            int flushIntervalMs = 1000)
        {
            _batchHandler = batchHandler ?? throw new ArgumentNullException(nameof(batchHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _flushIntervalMs = flushIntervalMs > 0 ? flushIntervalMs : 1000;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PublishFlushBackgroundService started with flush interval {IntervalMs}ms", _flushIntervalMs);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_flushIntervalMs, stoppingToken);
                        await _batchHandler.FlushAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in publish flush service");
                    }
                }
            }
            finally
            {
                // Final flush on shutdown
                try
                {
                    await _batchHandler.FlushAsync(CancellationToken.None);
                    _logger.LogInformation("PublishFlushBackgroundService final flush completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during final publish flush on shutdown");
                }
            }
        }
    }
}
