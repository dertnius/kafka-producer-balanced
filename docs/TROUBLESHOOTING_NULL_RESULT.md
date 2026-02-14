# Troubleshooting: ProduceAsync Returns Null

## Problem
When calling `await _producer.ProduceAsync(...)`, the result is `null` instead of a `DeliveryResult`. No exception is thrown.

---

## Quick Diagnosis

### Step 1: Run Diagnostic Endpoint
```bash
curl http://localhost:5000/api/encryptedmessages/diagnostics
```

**Expected Response:**
```json
{
  "producerStatus": "REGISTERED",
  "producerHealth": "HEALTHY",
  "checks": [
    {
      "check": "Producer Dependency Injection",
      "status": "PASS",
      "message": "Producer is properly injected"
    }
  ]
}
```

**If Producer is NOT_REGISTERED:**
Go to **Fix #1** below.

---

## Common Causes & Fixes

### Fix #1: Producer Not Registered in Dependency Injection

**Symptom:** Diagnostic shows `producerStatus: "NOT_REGISTERED"`

**Cause:** Missing service registration in `Startup.cs`

**Solution:**
Open `MyDotNetSolution/src/MyDotNetApp/Startup.cs` and add this to `ConfigureServices()`:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // ... existing code ...

    // ✅ ADD THIS BLOCK:
    services.AddSingleton<IAzureKeyVaultService, AzureKeyVaultService>();
    services.AddSingleton<IAvroKafkaProducerWithCSFLE, AvroKafkaProducerWithCSFLE>();
}
```

Then restart the application.

---

### Fix #2: Azure Key Vault Authentication Failed

**Symptom:** Producer returns null during encryption step

**Cause:** Azure credentials not available or insufficient permissions

**Check Logs:**
```bash
# Look for Azure-related errors
cat logs/service-operations-*.txt | grep -i "azure\|keyvault\|credential"
```

**Expected Log:**
```
✅ ENCRYPTED: OriginalSize=256→384 bytes, Algorithm=AES256-CBC, Duration=145ms
```

**If you see:**
```
❌ Failed to authenticate with Azure Key Vault
```

**Solution:**
1. Ensure you're running in Azure context (Managed Identity) or have local credentials:
   ```bash
   # For local development:
   az login
   ```

2. Verify the Key Vault exists and you have permissions:
   ```bash
   az keyvault show --name your-keyvault-name
   ```

3. Check DevelopmentAzureCredentials is configured in `appsettings.json`:
   ```json
   {
     "AzureKeyVault": {
       "VaultUrl": "https://your-keyvault.vault.azure.net/",
       "UseLocalEncryptionForDev": true
     }
   }
   ```

---

### Fix #3: Schema Registry Connection Failed

**Symptom:** Null result, no logs about encryption

**Cause:** Confluent Schema Registry not running or not accessible

**Check Logs:**
```bash
cat logs/kafka-communication-*.txt | grep -i "schema\|registry"
```

**Expected Log:**
```
GET /subjects/encrypted-avro-messages-value/versions/latest
```

**If Error:**
```
Failed to connect to Schema Registry
```

**Solution:**
1. Verify Schema Registry is running:
   ```bash
   curl http://localhost:8081/subjects
   ```

2. Check configuration in `appsettings.json`:
   ```json
   {
     "KafkaOutboxSettings": {
       "SchemaRegistryUrl": "http://localhost:8081"
     }
   }
   ```

3. Verify Schema is registered:
   ```bash
   curl http://localhost:8081/subjects/encrypted-avro-messages-value/versions/latest
   ```

---

### Fix #4: Kafka Broker Connection Failed

**Symptom:** Producer serializes but then returns null

**Cause:** Kafka broker not running or network issue

**Check Logs:**
```bash
cat logs/kafka-communication-*.txt | grep -i "broker\|bootstrap\|timeout"
```

**Expected Log:**
```
✅ PRODUCED to Kafka: Topic=encrypted-avro-topic, Partition=0, Offset=42
```

**Solution:**
1. Verify Kafka is running:
   ```bash
   kafka-broker-api-versions.sh --bootstrap-server localhost:9092
   ```

2. Check configuration:
   ```json
   {
     "KafkaOutboxSettings": {
       "BootstrapServers": "localhost:9092"
     }
   }
   ```

---

### Fix #5: Message Serialization Failed

**Symptom:** Error during Avro serialization

**Check Logs:**
```bash
cat logs/service-operations-*.txt | grep -i "serial\|avro"
```

**Look For:**
```
❌ Unexpected error producing message
```

**Solution:**
1. Verify payload object is serializable:
   ```csharp
   var payload = new { 
       CustomerId = "123",     // ✅ Serializable
       Amount = 99.99m         // ✅ Serializable
       // ❌ DO NOT use: Stream, HttpClient, etc.
   };
   ```

2. Ensure EncryptedAvroMessage schema matches registered schema:
   ```bash
   curl http://localhost:8081/subjects/encrypted-avro-messages-value/versions/latest
   ```

---

## Comprehensive Log Check

### View All Logs with Timestamps
```powershell
# Service operations (producer initialization, errors)
Get-Content logs/service-operations-*.txt | Select-Object -Last 100

# Kafka communication (serialization, network)
Get-Content logs/kafka-communication-*.txt | Select-Object -Last 100

# Message lifecycle (encryption, produce)
Get-Content logs/message-lifecycle-*.txt | Select-Object -Last 100
```

### Search for Errors
```powershell
# Find all errors
Get-Content logs/*.txt | Select-String "ERROR|ERROR|❌"

# Find null-related issues
Get-Content logs/*.txt | Select-String "null|NULL|failed"

# Find Azure issues
Get-Content logs/*.txt | Select-String "Azure|KeyVault|credential"
```

---

## Debug Mode: Add Detailed Logging

Update `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning",
      "Confluent.Kafka": "Debug",
      "Confluent.SchemaRegistry": "Debug",
      "MyDotNetApp.Services.AvroKafkaProducerWithCSFLE": "Debug"
    }
  }
}
```

Then restart and reproduce the issue. This will show detailed step-by-step logs.

---

## Testing ProduceAsync Directly

### Test Endpoint #1: Send Single Message
```bash
curl -X POST http://localhost:5000/api/encryptedmessages/send \
  -H "Content-Type: application/json" \
  -d '{
    "key": "test-123",
    "payload": {
      "CustomerId": "12345",
      "Amount": 99.99,
      "Message": "Test encryption"
    },
    "eventType": "test.created"
  }'
```

### Test Endpoint #2: Check Diagnostics
```bash
curl http://localhost:5000/api/encryptedmessages/diagnostics
```

### Test Endpoint #3: Health Check
```bash
curl http://localhost:5000/api/encryptedmessages/health
```

---

## Expected Behavior

### Success Flow:
```
1. POST /api/encryptedmessages/send
2. Payload validated ✅
3. Payload serialized to JSON ✅
4. Encrypted via Azure Key Vault ✅
5. Produced to Kafka ✅
6. Return: {success: true, topic: ..., partition: ..., offset: ...}
```

### Logs You Should See:
```
INFO: 📤 Calling ProduceAsync: Key=test-123, EventType=test.created
DEBUG: 🔐 ENCRYPTION PIPELINE START
DEBUG: [1/5] Payload serialized: 256 bytes
DEBUG: [2/5] Calling Azure Key Vault for encryption
INFO: ✅ ENCRYPTED: OriginalSize=256→384 bytes, Duration=145ms
DEBUG: [3/5] Creating Avro message
DEBUG: [4/5] Serializing to Kafka
INFO: ✅ [5/5] PRODUCED to Kafka: Topic=encrypted-avro-topic, Partition=0, Offset=42
```

---

## If Nothing Works

### Capture Full Debug Output
```powershell
# Clear logs
Remove-Item logs/*.txt

# Set debug logging
# (update appsettings.json as above)

# Restart app and reproduce issue

# Get all output
$allLogs = Get-Content logs/*.txt -ErrorAction SilentlyContinue | Select-String "."
$allLogs | Out-File debug-output.txt -Encoding UTF8

# Share debug-output.txt for analysis
```

### Contact Support
Provide:
1. Output of `/api/encryptedmessages/diagnostics`
2. Last 50 lines of each log file
3. Your `appsettings.json` (without secrets)
4. Error message from the response body

---

## Summary Table

| Symptom | Cause | Fix |
|---------|-------|-----|
| `producerStatus: "NOT_REGISTERED"` | DI not configured | Add service registration to Startup.cs |
| Logs mention "Azure" errors | Key Vault auth failed | Run `az login` or check permissions |
| Logs mention "Schema Registry" errors | Connection failed | Verify SR is running on :8081 |
| Logs mention "Kafka" or "broker" errors | Kafka not running | Start Kafka broker |
| No error logs, just null | Unknown error | Enable Debug logging and retry |
| Null in AggregateException handler | Multiple async errors | Check InnerExceptions in response |

---

**Last Updated:** February 2026  
**Version:** 1.0.0
