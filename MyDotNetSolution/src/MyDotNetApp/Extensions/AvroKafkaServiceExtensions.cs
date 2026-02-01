using Microsoft.Extensions.DependencyInjection;
using MyDotNetApp.Services;

namespace MyDotNetApp.Extensions;

/// <summary>
/// Service registration extensions for Avro Kafka Producer with CSFLE
/// This is a separate registration that doesn't affect your existing Kafka services
/// </summary>
public static class AvroKafkaServiceExtensions
{
    /// <summary>
    /// Add Avro Kafka Producer with Client-Side Field Level Encryption (CSFLE) and Azure Key Vault
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddAvroKafkaWithCSFLE(this IServiceCollection services)
    {
        // Register Azure Key Vault service
        services.AddSingleton<IAzureKeyVaultService, AzureKeyVaultService>();

        // Register Avro Kafka Producer with CSFLE
        services.AddSingleton<IAvroKafkaProducerWithCSFLE, AvroKafkaProducerWithCSFLE>();

        return services;
    }
}
