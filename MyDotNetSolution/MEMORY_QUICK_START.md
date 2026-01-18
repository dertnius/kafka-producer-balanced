# Memory Tracking - Quick Start Guide

## Where to Find Memory Information

### In Application Logs
The memory information appears in your application logs in **three key moments**:

```
═══════════════════════════════════════════════════════════════
                    SERVICE STARTS
═══════════════════════════════════════════════════════════════
2024-01-18 10:00:00.000 INFO 🚀 Service Startup - Memory Info: 
  Start=125.45 MB | Current=128.32 MB | Available: ~8192.50 MB

2024-01-18 10:00:01.123 INFO Starting Scaled Kafka Outbox Processor Service
2024-01-18 10:00:01.456 INFO OutboxProcessorServiceScaled is starting

═══════════════════════════════════════════════════════════════
              PROCESSING (Every 10 seconds)
═══════════════════════════════════════════════════════════════
2024-01-18 10:00:11.000 INFO Metrics - 
  Fetched: 50000 (5000/s) | Produced: 45000 (4500/s) | 
  Marked: 22500 | Failed: 500 | Elapsed: 10.0s | 
  Memory: Start=125.45 MB | Current=156.78 MB | Used=31.33 MB | Peak=167.45 MB

2024-01-18 10:00:21.000 INFO Metrics - 
  Fetched: 48000 (4800/s) | Produced: 43200 (4320/s) | 
  Marked: 21600 | Failed: 480 | Elapsed: 10.1s | 
  Memory: Start=125.45 MB | Current=148.92 MB | Used=23.47 MB | Peak=167.45 MB

2024-01-18 10:00:31.000 INFO Metrics - 
  Fetched: 52000 (5200/s) | Produced: 46800 (4680/s) | 
  Marked: 26000 | Failed: 520 | Elapsed: 10.0s | 
  Memory: Start=125.45 MB | Current=152.15 MB | Used=26.70 MB | Peak=167.45 MB

═══════════════════════════════════════════════════════════════
                   SERVICE STOPS
═══════════════════════════════════════════════════════════════
2024-01-18 10:00:45.000 INFO Stopping Scaled Kafka Outbox Processor Service

2024-01-18 10:00:45.100 INFO Metrics - 
  Fetched: 10000 (2000/s) | Produced: 9000 (1800/s) | 
  Marked: 5000 | Failed: 100 | Elapsed: 5.0s | 
  Memory: Start=125.45 MB | Current=138.92 MB | Used=13.47 MB | Peak=167.45 MB

2024-01-18 10:00:45.200 INFO 🛑 Service Shutdown - Memory Summary: 
  Start=125.45 MB | Final=128.92 MB | Total Used=3.47 MB | Peak Used=42.00 MB

═══════════════════════════════════════════════════════════════
```

---

## Memory Data Breakdown

### 1️⃣ STARTUP MEMORY
```
🚀 Service Startup - Memory Info: 
  Start=125.45 MB     ← Baseline memory when service initialized
  | Current=128.32 MB ← Memory right now (mostly same as start)
  | Available: ~8192.50 MB ← System memory available
```

**What it means**: Service initialized successfully with 125.45 MB baseline

---

### 2️⃣ PERIODIC METRICS (Every 10 seconds)
```
Metrics - 
  Fetched: 50000 (5000/s)              ← Messages fetched this window
  | Produced: 45000 (4500/s)           ← Messages produced this window
  | Marked: 22500                      ← Marked in database
  | Failed: 500                        ← Failed messages
  | Elapsed: 10.0s                     ← Time for this window
  | Memory: 
    Start=125.45 MB                    ← Baseline (constant)
    | Current=156.78 MB                ← Memory being used now
    | Used=31.33 MB                    ← How much above baseline
    | Peak=167.45 MB                   ← Highest ever used
```

**What it means**: In the last 10 seconds, service processed 50K messages and is using 31.33 MB above baseline (memory properly managed - will reset next window)

**Key observation**: After next 10s, `Current` should decrease to ~148 MB if memory reset is working → healthy behavior

---

### 3️⃣ SHUTDOWN MEMORY
```
🛑 Service Shutdown - Memory Summary:
  Start=125.45 MB       ← Baseline at startup
  | Final=128.92 MB     ← Memory at shutdown
  | Total Used=3.47 MB  ← Actual memory used (Final - Start)
  | Peak Used=42.00 MB  ← Max memory above baseline during lifetime
```

**What it means**: 
- Service ran for some duration
- Only consumed 3.47 MB net (good - properly cleaned up)
- Peak was 42 MB above baseline temporarily (normal)
- **No memory leak** - memory was released

---

## Quick Visual Reference

```
Memory Flow During Service Lifetime:

Start              Peak              Final
 │                  │                 │
 ├──────────────────┤                 │
 │   125.45 MB      │                 │
 │                  │  167.45 MB      │
 │                  ├─────────────────┤ (peak - only temporary)
 │                  │                 │
 │                  │    Memory       │  128.92 MB
 │◄─────────────────┼──Declined───────►│  (properly released)
 │                  │    (Good!)      │
 └────────────────────────────────────┘
      ▲              ▲                  ▲
   Start          Processing         Shutdown
   Phase           Phase              Phase
```

---

## Memory Monitoring Checklist

### ✅ At Startup (First 30 seconds)
- [ ] See 🚀 emoji with startup memory
- [ ] `Start` memory shows reasonable value (100-200 MB typical)
- [ ] `Current` is close to `Start`
- [ ] Proceed to next step

### ✅ During Processing (Watch for 1-2 minutes)
- [ ] See metrics every 10 seconds
- [ ] Memory `Used` shows processing overhead
- [ ] Memory `Used` **decreases or stays stable** between windows
- [ ] Memory `Peak` **doesn't keep growing**
- [ ] Throughput is consistent (msg/sec rate stable)
- [ ] Failed count is low (< 1% of processed)

### ✅ At Shutdown (Final check)
- [ ] See 🛑 emoji with shutdown summary
- [ ] `Final` memory is close to `Start`
- [ ] `Total Used` is small (< 10 MB typical)
- [ ] `Peak Used` shows temporary spike but not sustained

### ❌ Red Flags (Investigate if seen)
- ❌ Memory `Used` continuously increasing
- ❌ Memory `Peak` growing with every window
- ❌ `Final` memory much higher than `Start`
- ❌ Service crashes due to out-of-memory

---

## How to Capture Memory Data for Analysis

### From Console Output
```bash
# Save all logs to file
dotnet run 2>&1 | tee app.log

# Later, extract memory lines
grep -E "(🚀|Memory:|🛑)" app.log > memory-report.txt
```

### From File Logs (if using Serilog file sink)
```bash
# Extract memory logs
grep "Memory:" logs.txt | tee memory-metrics.csv

# Count windows processed
grep "Metrics -" logs.txt | wc -l

# Check for leaks
grep "Used=" logs.txt | awk '{print $NF}' | sort -n | tail -10
```

### Create Timeline CSV
Extract columns for charting:
```
Timestamp,Fetched/s,Produced/s,Current_MB,Used_MB,Peak_MB
10:00:11,5000,4500,156.78,31.33,167.45
10:00:21,4800,4320,148.92,23.47,167.45
10:00:31,5200,4680,152.15,26.70,167.45
```

Then import into Excel/Python/PowerBI for visualization.

---

## Interpretation Quick Reference

| What You See | What It Means | Action |
|--------------|--------------|--------|
| `Used=` decreasing each window | Memory releasing properly | ✅ Healthy - no action |
| `Used=` stable with ~same value | Steady state operation | ✅ Healthy - no action |
| `Used=` increasing every window | Memory accumulating | ❌ Investigate - possible leak |
| `Peak=` constantly growing | Memory pressure increasing | ❌ Investigate - scale resources |
| `Final=` >> `Start=` | Memory not released at exit | ❌ Investigate - cleanup issues |

---

## Log Filtering Cheatsheet

```bash
# Show only memory-related logs
grep -E "(Memory:|🚀|🛑)" app.log

# Show only startup and shutdown
grep -E "(🚀 Service Startup|🛑 Service Shutdown)" app.log

# Show memory trends (Get "Used=" column)
grep "Used=" app.log | cut -d= -f5 | cut -d| -f1

# Check for memory growth pattern
grep "Used=" app.log | tail -20 | cut -d= -f5

# Count metrics windows
grep "Metrics -" app.log | grep "Memory:" | wc -l

# Find maximum memory used
grep "Peak=" app.log | cut -d= -f7 | sort -n | tail -1
```

---

## Integration with Monitoring Tools

### Splunk
```
index=logs "Memory:" | stats avg(Used_MB), max(Peak_MB) by host
```

### ELK Stack (Elasticsearch)
```
GET /logs/_search
{
  "query": {"match": {"message": "Memory:"}},
  "aggs": {"peak_memory": {"max": {"field": "peak_mb"}}}
}
```

### Application Insights
```csharp
// Metrics automatically ingested by Serilog
// Use custom properties to query
// SELECT toDateTime(timestamp), Used_MB, Peak_MB FROM traces
```

---

## Summary

**You Now Have:**
1. ✅ Automatic memory tracking at startup
2. ✅ Periodic memory metrics every 10 seconds  
3. ✅ Final memory report at shutdown
4. ✅ Human-readable format (MB/GB)
5. ✅ Thread-safe operations
6. ✅ Zero configuration needed

**Next:** Start the service and check logs for 🚀, Memory:, and 🛑 markers!
