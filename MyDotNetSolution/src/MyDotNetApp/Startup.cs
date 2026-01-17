using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyDotNetApp.Models;
using MyDotNetApp.Services;
using System.Data;
using Microsoft.Data.SqlClient;

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

        // Add services
        services.AddScoped<IOutboxService, OutboxService>();
        
        // Register shared Kafka producer pool (singleton so all services use same pool)
        services.AddSingleton<IKafkaProducerPool>(sp =>
            new KafkaProducerPool(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<KafkaProducerPool>>(),
                poolSize: 5));  // Configurable pool size
        
        services.AddScoped<IKafkaService, KafkaService>();

        // Add batch publishing handler for high-performance publish updates
        var batchSize = Configuration.GetValue<int>("Publishing:BatchSize", 5000);
        var flushIntervalMs = Configuration.GetValue<int>("Publishing:FlushIntervalMs", 1000);
        services.AddSingleton<IPublishBatchHandler>(sp =>
            new PublishBatchHandler(
                sp.GetRequiredService<IOutboxService>(),
                sp.GetRequiredService<ILogger<PublishBatchHandler>>(),
                batchSize,
                flushIntervalMs));

        // Register flush background service for publish status updates
        services.AddHostedService<PublishFlushBackgroundService>();

        // Configure Kafka settings
        services.Configure<KafkaOutboxSettings>(Configuration.GetSection("KafkaOutboxSettings"));

        // Register outbox processor background service
        services.AddHostedService(sp =>
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
        });
    }
}