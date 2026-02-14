# Kafka Producer Debug: Capturing CSFLE Logs

## Problem

Setting `"Confluent.Kafka": "Debug"` in appsettings.json alone doesn't show debug logs because Confluent.Kafka uses librdkafka's native logging, not .NET's ILogger.

---

## Solution: Enable Native Confluent Debug Logging

### Step 1: Add Log Handler to Producer

✅ **DONE** - Already added to `AvroKafkaProducerWithCSFLE.cs`:

```csharp
.SetLogHandler((producer, logMessage) =>
{
    // Maps librdkafka severity levels to .NET LogLevel
    var level = logMessage.Level switch
    {
        SyslogLevel.Debug => LogLevel.Debug,
        SyslogLevel.Info => LogLevel.Information,
        SyslogLevel.Warning => LogLevel.Warning,
        SyslogLevel.Err => LogLevel.Error,
        _ => LogLevel.Information
    };

    _logger.Log(level, 
        "🔧 Confluent.Kafka [{Facility}] {Message}", 
        logMessage.Facility, 
        logMessage.Message);
})
```

### Step 2: Enable Debug Flags in ProducerConfig

✅ **DONE** - Already added:

```csharp
var producerConfig = new ProducerConfig
{
    // ... other settings ...
    Debug = "broker,topic,metadata,protocol,serializer",
    LogConnectionClose = true,
};
```

**Debug contexts explained:**
- `broker` - Broker connection details
- `topic` - Topic metadata
- `metadata` - Schema/metadata operations
- `protocol` - Protocol-level details (most verbose)
- `serializer` - Serialization/deserialization details

### Step 3: Update Logging Configuration

✅ **DONE** - Already added to `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "MyDotNetApp.Services.AvroKafkaProducerWithCSFLE": "Debug",
      "MyDotNetApp.Services.AzureKeyVaultService": "Debug"
    }
  }
}
```

---

## What You'll See Now

When you call the producer endpoint after these changes:

### Confluent Logs (via SetLogHandler)
```
🔧 Confluent.Kafka [Client] Brokers added from bootstrap.servers: 1 brokers
🔧 Confluent.Kafka [TopicMetadata] Topic cached, 1 partitions, 1 replicas (broker 0)
🔧 Confluent.Kafka [Produce] TopicPartition: [topic-0] ProducerCallback triggered
🔧 Confluent.Kafka [Connection] Connected to broker at 127.0.0.1:9092 
🔧 Confluent.Kafka [Serializer] Serializing message with Avro schema
```

### Your Custom CSFLE Logs
```
🔐 ENCRYPTION PIPELINE START: Key=customer-123, EventType=CustomerCreated
  [1/5] Payload serialized: 256 bytes
  [2/5] Calling Azure Key Vault for encryption...
✅ ENCRYPTED: OriginalSize=256→384 bytes, Algorithm=AES256-CBC, Duration=145ms
  [3/5] Creating Avro message with encrypted payload
  [4/5] Serializing to Kafka with Schema Registry...
🔧 Confluent.Kafka [Serializer] Serializing message with Avro schema
✅ [5/5] PRODUCED to Kafka: Topic=encrypted-avro-topic, Partition=0, Offset=42
```

---

## How to Use

### 1. Rebuild the Solution
```bash
cd MyDotNetSolution
dotnet clean
dotnet build
```

### 2. Run the Application
```bash
dotnet run
```

### 3. Send Test Message
```bash
curl -X POST http://localhost:5000/api/encryptedmessages/send \
  -H "Content-Type: application/json" \
  -d '{
    "key": "test-123",
    "payload": {
      "CustomerId": "cust-456",
      "Amount": 99.99
    },
    "eventType": "test.created"
  }'
```

### 4. Watch Console Output
You should immediately see:
- ✅ Your custom pipeline logs (🔐, ✅, 🔧 emojis)
- ✅ Confluent internal logs (broker, topic, serializer details)
- ✅ Azure Key Vault logs (encryption duration)

---

## Complete Log Flow

Here's what the complete flow looks like with all logs enabled:

```
┌─ Your Code ─────────────────────────────────────┐
│                                                   │
├─ Logger: 📤 Calling ProduceAsync                │
│                                                   │
├─ Logger: 🔐 ENCRYPTION PIPELINE START           │
│                                                   │
├─ Logger: [1/5] Payload serialized: 256 bytes    │
│                                                   │
├─ Logger: [2/5] Calling Azure Key Vault          │
│                                                   │
├─ Confluent: 🔧 Azure.Security.KeyVault...       │
│                                                   │
├─ Logger: ✅ ENCRYPTED: 256→384 bytes, 145ms    │
│                                                   │
├─ Logger: [3/5] Creating Avro message            │
│                                                   │
├─ Logger: [4/5] Serializing to Kafka             │
│                                                   │
├─ Confluent: 🔧 Confluent.Kafka TopicMetadata... │
│                                                   │
├─ Confluent: 🔧 Confluent.Kafka Produce...       │
│                                                   │
├─ Confluent: 🔧 Confluent.Kafka Connection...    │
│                                                   │
├─ Logger: ✅ [5/5] PRODUCED to Kafka             │
│                                                   │
└─────────────────────────────────────────────────┘
```

---

## If Still Not Seeing Logs

### Check Current Debug Level
```powershell
# Fast check
Get-Content logs/service-operations-*.txt -Tail 20 | grep -i "confluent\|encryption"
```

### Increase Verbosity Further

Edit `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "MyDotNetApp": "Debug",
      "MyDotNetApp.Services": "Debug",
      "Confluent": "Debug"
    }
  }
}
```

### Enable All Kafka Debug Contexts
```csharp
Debug = "all",  // Enable ALL debug contexts
```

Or individually:
```csharp
Debug = "all",
// OR
Debug = "broker,topic,metadata,protocol,serializer,generic,security,cipher,connections,fetch",
```

---

## Debug Contexts Reference

| Context | Shows |
|---------|-------|
| `broker` | Broker discovery and connection |
| `topic` | Topic metadata and partitioning |
| `metadata` | Schema/metadata caching |
| `protocol` | WIRE protocol details (very verbose) |
| `serializer` | Avro serialization steps |
| `security` | Security/SSL/SASL details |
| `connections` | TCP connection details |
| `fetch` | Consumer fetch details |
| `generic` | Generic librdkafka details |
| `all` | Everything (very verbose) |

---

## Verify CSFLE is Actually Working

Watch for these specific log lines:

### ✅ Encryption Happened:
```
✅ ENCRYPTED: OriginalSize=256→384 bytes
```
- Size should INCREASE (encryption adds overhead)

### ✅ Correct Algorithm:
```
Algorithm=AES256-CBC, KeyId=dek-kafka-csfle
```

### ✅ Correct Headers:
```
Headers=[encryption:CSFLE-AKV,key-id:dek-kafka-csfle]
```

### ✅ Message in Kafka:
```
✅ [5/5] PRODUCED to Kafka: Topic=encrypted-avro-topic, Partition=0, Offset=42
```

---

## Common Issues & Fixes

| Issue | Cause | Fix |
|-------|-------|-----|
| No Confluent logs appear | SetLogHandler not called | Rebuild solution, restart app |
| Logs too verbose | All debug enabled | Change `Debug = "serializer"` (one context) |
| Still no encryption logs | Producer not initialized | Run `/api/encryptedmessages/diagnostics` |
| Logs don't match | Old binary still running | `dotnet clean`, then `dotnet build` |

---

## Real-World Example Output

Here's what your complete logs should look like:

```
2026-02-14 14:32:15.123 INFO: 📤 Calling ProduceAsync: Key=test-123, EventType=test.created
2026-02-14 14:32:15.124 DEBUG: 🔐 ENCRYPTION PIPELINE START: Key=test-123, EventType=test.created
2026-02-14 14:32:15.125 DEBUG: [1/5] Payload serialized: 256 bytes
2026-02-14 14:32:15.126 DEBUG: [2/5] Calling Azure Key Vault for encryption...
2026-02-14 14:32:15.234 DEBUG: 🔧 Confluent.Kafka [Azure] Authenticating with Azure credentials
2026-02-14 14:32:15.291 INFO: ✅ ENCRYPTED: OriginalSize=256→384 bytes, Compression=-50%, Algorithm=AES256-CBC, KeyId=dek-kafka-csfle, Duration=165ms
2026-02-14 14:32:15.292 DEBUG: [3/5] Creating Avro message with encrypted payload
2026-02-14 14:32:15.293 DEBUG: [4/5] Serializing to Kafka with Schema Registry...
2026-02-14 14:32:15.294 DEBUG: 🔧 Confluent.Kafka [BrokerMetadata] Querying for topic: encrypted-avro-topic
2026-02-14 14:32:15.310 DEBUG: 🔧 Confluent.Kafka [TopicPartition] Topic resolved to partition 0, leader=1
2026-02-14 14:32:15.311 DEBUG: 🔧 Confluent.Kafka [Produce] Producing to partition 0 with compression=snappy
2026-02-14 14:32:15.325 INFO: ✅ [5/5] PRODUCED to Kafka: Topic=encrypted-avro-topic, Partition=0, Offset=42, MessageSize=384, Headers=[encryption:CSFLE-AKV,key-id:dek-kafka-csfle], Duration=31ms
2026-02-14 14:32:15.326 DEBUG: 🔓 ENCRYPTION PIPELINE COMPLETE (Total: 196ms)
```

---

## Summary

**What Changed:**
1. ✅ Added `SetLogHandler()` to capture Confluent logs
2. ✅ Enabled `Debug = "broker,topic,metadata,protocol,serializer"` in ProducerConfig
3. ✅ Updated appsettings.json with Debug log levels
4. ✅ Now you'll see BOTH custom pipeline logs AND Confluent internal logs

**How to Test:**
1. Rebuild: `dotnet clean && dotnet build`
2. Run: `dotnet run`
3. Call: `POST http://localhost:5000/api/encryptedmessages/send`
4. Watch console/logs for complete pipeline with 🔐 and ✅ markers

Done! 🎯
