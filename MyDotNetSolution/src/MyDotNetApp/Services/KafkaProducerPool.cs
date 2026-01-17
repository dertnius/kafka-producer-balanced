using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MyDotNetApp.Services
{
    /// <summary>
    /// Interface for a shared Kafka producer pool
    /// </summary>
    public interface IKafkaProducerPool
    {
        IProducer<string, string> GetProducer();
        void ReturnProducer(IProducer<string, string> producer);
        void Flush(TimeSpan timeout);
    }

    /// <summary>
    /// Shared producer pool for all Kafka operations
    /// Provides round-robin access to a pool of producers for load balancing
    /// </summary>
    public class KafkaProducerPool : IKafkaProducerPool, IDisposable
    {
        private readonly ConcurrentQueue<IProducer<string, string>> _availableProducers;
        private readonly List<IProducer<string, string>> _allProducers;
        private readonly ILogger<KafkaProducerPool> _logger;
        private readonly object _lockObj = new object();
        private int _producerRoundRobin = 0;

        public KafkaProducerPool(
            IConfiguration configuration,
            ILogger<KafkaProducerPool> logger,
            int poolSize = 10)  // Increase default pool to match throughput
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _availableProducers = new ConcurrentQueue<IProducer<string, string>>();
            _allProducers = new List<IProducer<string, string>>();

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = configuration["KafkaOutboxSettings:BootstrapServers"] ?? "localhost:9092",
                Acks = Acks.Leader,          // Only wait for leader (faster than Acks.All)
                MessageTimeoutMs = 10000,    // Shorter timeout for high throughput
                CompressionType = CompressionType.Snappy,
                LingerMs = 10,               // Allow small batching window
                BatchSize = 512 * 1024,      // 512 KB batches for better throughput
                QueueBufferingMaxMessages = 100000,  // More buffer for burst traffic
                QueueBufferingMaxKbytes = 262144,    // 256 MB max memory
                SocketKeepaliveEnable = true,
                RequestTimeoutMs = 10000,
                MaxInFlight = 5              // Allow more in-flight requests
            };

            // Create producer pool
            for (int i = 0; i < poolSize; i++)
            {
                var producer = new ProducerBuilder<string, string>(producerConfig)
                    .SetErrorHandler((_, error) =>
                    {
                        _logger.LogError("Kafka producer error: {Error}", error.Reason);
                    })
                    .Build();

                _allProducers.Add(producer);
                _availableProducers.Enqueue(producer);
            }

            _logger.LogInformation("Initialized Kafka producer pool with {PoolSize} producers", poolSize);
        }

        /// <summary>
        /// Get a producer from the pool using round-robin
        /// </summary>
        public IProducer<string, string> GetProducer()
        {
            lock (_lockObj)
            {
                var index = Interlocked.Increment(ref _producerRoundRobin) % _allProducers.Count;
                return _allProducers[index];
            }
        }

        /// <summary>
        /// Return a producer to the pool (no-op for now, kept for future queue-based implementation)
        /// </summary>
        public void ReturnProducer(IProducer<string, string> producer)
        {
            // With round-robin, producers are always available
            // This is a placeholder for potential queue-based pool in future
        }

        /// <summary>
        /// Flush all producers
        /// </summary>
        public void Flush(TimeSpan timeout)
        {
            foreach (var producer in _allProducers)
            {
                try
                {
                    producer?.Flush(timeout);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error flushing producer");
                }
            }
        }

        public void Dispose()
        {
            foreach (var producer in _allProducers)
            {
                producer?.Dispose();
            }
            _allProducers.Clear();
        }
    }
}
