# Quick Start: Avro Kafka Producer with CSFLE

## 5-Minute Setup

### 1. Add to Your Startup

```csharp
// In Program.cs or Startup.cs
using MyDotNetApp.Extensions;

builder.Services.AddAvroKafkaWithCSFLE();
```

### 2. Configure appsettings.json

```json
{
  "KafkaOutboxSettings": {
    "BootstrapServers": "localhost:9092",
    "TopicName": "encrypted-avro-messages",
    "SchemaRegistryUrl": "http://localhost:8081"
  },
  "AzureKeyVault": {
    "VaultUrl": "",
    "UseLocalEncryptionForDev": true
  }
}
```

### 3. Use in Your Code

```csharp
public class MyService
{
    private readonly IAvroKafkaProducerWithCSFLE _producer;

    public MyService(IAvroKafkaProducerWithCSFLE producer)
    {
        _producer = producer;
    }

    public async Task SendMessage()
    {
        await _producer.ProduceAsync(
            topic: "my-topic",
            key: "my-key",
            payload: new { secret = "sensitive-data" },
            eventType: "my.event.type"
        );
    }
}
```

## Try the Demo API

Start your app and test:

```bash
# Send payment demo
curl -X POST http://localhost:5000/api/AvroKafkaDemo/demo/payment

# Bulk send test
curl -X POST http://localhost:5000/api/AvroKafkaDemo/demo/bulk-send?count=100

# Test encryption
curl http://localhost:5000/api/AvroKafkaDemo/demo/test-encryption
```

## What Gets Installed

- ✅ Azure Key Vault integration
- ✅ Avro serialization with Schema Registry
- ✅ AES-256-CBC encryption
- ✅ REST API controllers
- ✅ Demo endpoints

## Production Setup

For production, configure Azure Key Vault:

```bash
# Create Key Vault
az keyvault create --name my-kafka-kv --resource-group my-rg

# Update appsettings.Production.json
{
  "AzureKeyVault": {
    "VaultUrl": "https://my-kafka-kv.vault.azure.net/",
    "DataEncryptionKeyName": "dek-kafka-csfle"
  }
}
```

## Full Documentation

See [AVRO_CSFLE_GUIDE.md](AVRO_CSFLE_GUIDE.md) for complete details.

## Your Existing Code

✅ **No changes to your existing Kafka code**  
✅ **This is a completely separate producer**  
✅ **Use alongside your current implementation**
