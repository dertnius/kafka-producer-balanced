# Manual Trigger API Documentation

The Outbox Processor now supports **dual-mode operation**: automatic timer-based polling AND manual API triggers.

## How It Works

The processor polls the database in two ways:

1. **Timer-based (automatic)**: Every `PollingIntervalMs` (default: 100ms)
2. **API-triggered (manual)**: When you call the trigger endpoint

Both triggers share the same processing logic - messages are fetched and added to the channel for immediate processing.

## API Endpoints

### 1. Trigger Processing

**POST** `/api/outbox/trigger`

Manually triggers immediate processing of outbox messages, bypassing the timer.

**Example:**
```bash
curl -X POST http://localhost:5000/api/outbox/trigger
```

**Response:**
```json
{
  "success": true,
  "message": "Outbox processing triggered successfully. Messages will be processed immediately.",
  "timestamp": "2026-01-18T10:30:45.123Z"
}
```

### 2. Get Processing Stats

**GET** `/api/outbox/stats`

Returns current processing statistics.

**Example:**
```bash
curl http://localhost:5000/api/outbox/stats
```

**Response:**
```json
{
  "inFlightMessages": 1247,
  "stidLocks": 523,
  "metrics": {
    "timestamp": "2026-01-18T10:30:45.123Z"
  }
}
```

## Use Cases

### When to Use Manual Trigger:

1. **Immediate Processing**: You just inserted urgent messages and want them sent ASAP
2. **Testing**: Trigger processing on-demand during development/testing
3. **Monitoring Integration**: External systems can trigger processing after bulk inserts
4. **Recovery**: Manually trigger after fixing issues (e.g., Kafka was down)

### Example Workflow:

```csharp
// 1. Bulk insert messages into Outbox table
await InsertMessagesToOutbox(messages);

// 2. Immediately trigger processing (don't wait for timer)
var httpClient = new HttpClient();
await httpClient.PostAsync("http://localhost:5000/api/outbox/trigger", null);

// Messages start processing immediately
```

## Configuration

Timer interval is configured in `appsettings.json`:

```json
{
  "KafkaOutbox": {
    "PollingIntervalMs": 100  // How often automatic polling occurs
  }
}
```

**Recommendations:**
- **Development**: 1000ms (1 second) - less DB load, use manual trigger for immediate testing
- **Production**: 100ms (default) - good balance, manual trigger for urgent messages
- **High volume**: 50ms - very responsive, manual trigger rarely needed

## Technical Details

- **Thread-safe**: Uses `SemaphoreSlim` for synchronization
- **No duplicate triggers**: Multiple API calls while processing won't queue up
- **Shared processing**: Timer and manual trigger use the same code path
- **In-flight tracking**: Prevents duplicate messages whether triggered by timer or API

## Logging

When manual trigger is called:

```
[Information] Manual trigger requested
[Information] Manual processing trigger received via API
[Debug] Fetched 500 messages from outbox (query took 15.2ms)
```

## Error Handling

- If trigger is called while one is already pending → logs "Manual trigger already pending" (debug level)
- Failed processing attempts still log errors and retry on next poll (timer or manual)
- Manual trigger does NOT crash the service if processing fails

## Integration Example

```csharp
public class OrderService
{
    private readonly IHttpClientFactory _httpClientFactory;
    
    public async Task CreateUrgentOrder(Order order)
    {
        // 1. Insert order + create outbox message
        await _dbContext.Orders.AddAsync(order);
        await _dbContext.Outbox.AddAsync(new OutboxMessage { ... });
        await _dbContext.SaveChangesAsync();
        
        // 2. Trigger immediate processing for urgent order
        var client = _httpClientFactory.CreateClient();
        await client.PostAsync("http://localhost:5000/api/outbox/trigger", null);
        
        // Order notification sent to Kafka within milliseconds!
    }
}
```

## Testing the API

### Using PowerShell:
```powershell
# Trigger processing
Invoke-WebRequest -Method POST -Uri "http://localhost:5000/api/outbox/trigger"

# Get stats
Invoke-WebRequest -Uri "http://localhost:5000/api/outbox/stats" | Select-Object -ExpandProperty Content
```

### Using curl:
```bash
# Trigger processing
curl -X POST http://localhost:5000/api/outbox/trigger

# Get stats
curl http://localhost:5000/api/outbox/stats | jq .
```

## Performance Impact

- **Minimal overhead**: Manual trigger just releases a semaphore (microseconds)
- **No extra polling**: Uses the same polling loop, just wakes it up early
- **Memory safe**: Only one pending trigger allowed at a time
- **No contention**: Timer and manual trigger coordinate through the same semaphore

## Summary

You now have **flexible control** over outbox processing:

✅ **Automatic**: Runs every 100ms by default  
✅ **On-Demand**: Call API to process immediately  
✅ **Zero configuration**: Works out of the box  
✅ **Production-ready**: Thread-safe and error-resistant
