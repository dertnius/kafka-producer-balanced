using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.IO;

namespace MyDotNetApp.Logging
{
    /// <summary>
    /// Configures Serilog with three separate file sinks:
    /// 1. Service operations (OutboxProcessorServiceScaled, OutboxConsumerService, etc.)
    /// 2. Kafka communication (producer/consumer interactions)
    /// 3. Message lifecycle (produced/consumed/processed events)
    /// 
    /// Each file rolls at 50MB with max 2 files retained.
    /// </summary>
    public static class LoggingConfiguration
    {
        public static Logger CreateLogger()
        {
            var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsPath);

            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "MyDotNetApp")
                
                // Console output for development
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                
                // Service operations log
                .WriteTo.File(
                    path: Path.Combine(logsPath, "service-operations-.txt"),
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: 52_428_800, // 50 MB
                    retainedFileCountLimit: 2,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information)
                
                // Kafka communication log (producer/consumer)
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e =>
                        e.Properties.ContainsKey("SourceContext") && (
                            e.Properties["SourceContext"].ToString().Contains("\"MyDotNetApp.Services.KafkaService\"") ||
                            e.Properties["SourceContext"].ToString().Contains("\"MyDotNetApp.Services.OutboxConsumerService\"") ||
                            e.Properties["SourceContext"].ToString().Contains("Confluent.Kafka")
                        ))
                    .WriteTo.File(
                        path: Path.Combine(logsPath, "kafka-communication-.txt"),
                        rollingInterval: RollingInterval.Infinite,
                        fileSizeLimitBytes: 52_428_800, // 50 MB
                        retainedFileCountLimit: 2,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                        restrictedToMinimumLevel: LogEventLevel.Information))
                
                // Message lifecycle log (produced/consumed/processed)
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e =>
                        e.MessageTemplate.Text.Contains("produced") ||
                        e.MessageTemplate.Text.Contains("consumed") ||
                        e.MessageTemplate.Text.Contains("processed") ||
                        e.MessageTemplate.Text.Contains("flushing") ||
                        e.MessageTemplate.Text.Contains("Throughput") ||
                        e.MessageTemplate.Text.Contains("in-flight"))
                    .WriteTo.File(
                        path: Path.Combine(logsPath, "message-lifecycle-.txt"),
                        rollingInterval: RollingInterval.Infinite,
                        fileSizeLimitBytes: 52_428_800, // 50 MB
                        retainedFileCountLimit: 2,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                        restrictedToMinimumLevel: LogEventLevel.Information))
                
                .CreateLogger();

            return logger;
        }
    }
}
