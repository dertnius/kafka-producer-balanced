using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyDotNetApp.Models;
using MyDotNetApp.Services;
using MyDotNetApp.Data;
using MyDotNetApp.Data.Concrete;
using MyDotNetApp.Data.UnitOfWork;
using System.Data;
using Microsoft.Data.SqlClient;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Add controllers
        services.AddControllers();

        // Add database connection with proper pooling
        var connectionString = Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection is not configured in appsettings.json");
        }

        services.AddScoped<IDbConnection>(sp =>
        {
            var connection = new SqlConnection(connectionString);
            connection.Open();
            return connection;
        });

        // Register data access layer (repositories and unit of work)
        services.AddDataAccess(connectionString);

        // Register OutboxProcessingService
        services.AddScoped<OutboxProcessingService>();

        // Add services - singleton for use by singleton hosted services
        services.AddSingleton<IOutboxService>(sp => 
            new OutboxService(connectionString, sp.GetRequiredService<ILogger<OutboxService>>()));
        
        // Register shared Kafka producer pool (singleton so all services use same pool)
        services.AddSingleton<IKafkaProducerPool>(sp =>
            new KafkaProducerPool(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<KafkaProducerPool>>(),
                poolSize: 10));  // Match high-throughput needs
        
        services.AddSingleton<IKafkaService, KafkaService>();

        // Add batch publishing handler for high-performance publish updates
        var batchSize = Configuration.GetValue<int>("Publishing:BatchSize", 5000);
        var flushIntervalMs = Configuration.GetValue<int>("Publishing:FlushIntervalMs", 1000);
        services.AddSingleton<IPublishBatchHandler>(sp =>
            new PublishBatchHandler(
                sp,  // Pass IServiceProvider to create scopes for IOutboxService
                sp.GetRequiredService<ILogger<PublishBatchHandler>>(),
                batchSize,
                flushIntervalMs));

        // Register flush background service for publish status updates
        services.AddHostedService(sp =>
            new PublishFlushBackgroundService(
                sp.GetRequiredService<IPublishBatchHandler>(),
                sp.GetRequiredService<ILogger<PublishFlushBackgroundService>>(),
                flushIntervalMs));

        // Configure Kafka settings
        services.Configure<KafkaOutboxSettings>(Configuration.GetSection("KafkaOutboxSettings"));

        // Register outbox processor as a singleton so API/manual triggers reach the running instance
        services.AddSingleton<OutboxProcessorServiceScaled>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<OutboxProcessorServiceScaled>>();
            var settings = sp.GetRequiredService<IOptions<KafkaOutboxSettings>>();
            var kafkaService = sp.GetRequiredService<IKafkaService>();
            var publishBatchHandler = sp.GetRequiredService<IPublishBatchHandler>();

            return new OutboxProcessorServiceScaled(
                logger,
                settings,
                connectionString,
                kafkaService,
                publishBatchHandler);
        });

        // Reuse the same singleton for the hosted background service
        services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessorServiceScaled>());

        // Register multiple outbox consumer background services for parallel processing
        // All consumers share the same consumer group, Kafka distributes partitions among them
        // This enables 1M+ msg/min throughput within a single IIS application
        var consumerCount = Configuration.GetValue<int>("Consumer:InstanceCount", 3);
        for (int i = 0; i < consumerCount; i++)
        {
            var instanceId = i;
            services.AddHostedService(sp =>
                new OutboxConsumerService(
                    sp.GetRequiredService<ILogger<OutboxConsumerService>>(),
                    sp.GetRequiredService<IOutboxService>(),
                    sp.GetRequiredService<IConfiguration>(),
                    sp.GetRequiredService<IOptions<KafkaOutboxSettings>>(),
                    instanceId));
        }
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
    {
        logger.LogInformation("Starting application in {Environment} environment", env.EnvironmentName);

        // Error handling
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            // Production error handling
            app.UseExceptionHandler("/error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseRouting();

        // Add request logging middleware
        app.Use(async (context, next) =>
        {
            var logger2 = context.RequestServices.GetRequiredService<ILogger<Startup>>();
            logger2.LogInformation("HTTP {Method} {Path} started", context.Request.Method, context.Request.Path);
            
            try
            {
                await next();
                logger2.LogInformation("HTTP {Method} {Path} completed with status {StatusCode}", 
                    context.Request.Method, context.Request.Path, context.Response.StatusCode);
            }
            catch (Exception ex)
            {
                logger2.LogError(ex, "HTTP {Method} {Path} failed with exception", 
                    context.Request.Method, context.Request.Path);
                throw;
            }
        });

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            
            // Add root endpoint that shows available endpoints
            endpoints.MapGet("/", () => Results.Ok(new 
            {
                message = "MyDotNetApp API is running",
                endpoints = new[]
                {
                    "/api/health",
                    "/api/outbox/stats",
                    "/api/outbox/trigger",
                    "/api/outbox/stop",
                    "/api/outbox/resume"
                }
            }));
        });
    }
}