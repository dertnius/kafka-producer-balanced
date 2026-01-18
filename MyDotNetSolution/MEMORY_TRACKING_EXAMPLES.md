# Memory Tracking - Log Examples

## What You'll See in Application Logs

### 1. SERVICE STARTUP
When the service starts, you'll see:
```
🚀 Service Startup - Memory Info: Start=125.45 MB | Current=128.32 MB | Available: ~8192.50 MB
```

**Meaning:**
- Service reserved 125.45 MB at startup
- Currently using 128.32 MB (baseline + minimal overhead)
- System has about 8 GB available

---

### 2. PERIODIC REPORTS (Every 10 seconds during processing)
While processing messages:
```
Metrics - Fetched: 50000 (5000/s) | Produced: 45000 (4500/s) | Marked: 22500 | Failed: 500 | Elapsed: 10.5s | Memory: Start=125.45 MB | Current=156.78 MB | Used=31.33 MB | Peak=167.45 MB
```

**Breaking it down:**
- **Processing**: 50K messages fetched at 5K/sec, 45K produced, 22.5K marked, 500 failed
- **Memory**: Start at 125.45 MB, currently at 156.78 MB
- **Used**: 31.33 MB above baseline (156.78 - 125.45)
- **Peak**: Highest memory reached so far = 167.45 MB

**Next 10-second window (healthy behavior):**
```
Metrics - Fetched: 48000 (4800/s) | Produced: 43200 (4320/s) | Marked: 21600 | Failed: 480 | Elapsed: 10.2s | Memory: Start=125.45 MB | Current=148.92 MB | Used=23.47 MB | Peak=167.45 MB
```

**Notice:**
- Counters RESET to 0 after logging (new window)
- Memory `Used` decreased from 31.33 MB to 23.47 MB (good!)
- `Peak` stayed at 167.45 MB (remains highest point)

---

### 3. SERVICE SHUTDOWN
When the service stops:
```
🛑 Service Shutdown - Memory Summary: Start=125.45 MB | Final=128.92 MB | Total Used=3.47 MB | Peak Used=42.00 MB
```

**Meaning:**
- Started with 125.45 MB baseline
- Ended at 128.92 MB (only 3.47 MB additional)
- Peak went 42 MB above baseline (167.45 - 125.45)
- Memory was freed during service lifetime (good sign - no leak!)

---

## Real-World Examples

### Example 1: HEALTHY SERVICE (No Memory Leak)
```
INFO: 🚀 Service Startup - Memory Info: Start=150.25 MB | Current=152.18 MB | Available: ~10240.00 MB
INFO: Metrics - Fetched: 100000 (10000/s) | Produced: 90000 (9000/s) | Marked: 45000 | Failed: 1000 | Elapsed: 10.0s | Memory: Start=150.25 MB | Current=220.50 MB | Used=70.25 MB | Peak=225.00 MB
INFO: Metrics - Fetched: 98000 (9800/s) | Produced: 88000 (8800/s) | Marked: 44000 | Failed: 980 | Elapsed: 10.0s | Memory: Start=150.25 MB | Current=185.75 MB | Used=35.50 MB | Peak=225.00 MB
INFO: Metrics - Fetched: 102000 (10200/s) | Produced: 91000 (9100/s) | Marked: 45500 | Failed: 1020 | Elapsed: 10.0s | Memory: Start=150.25 MB | Current=178.25 MB | Used=28.00 MB | Peak=225.00 MB
INFO: 🛑 Service Shutdown - Memory Summary: Start=150.25 MB | Final=155.50 MB | Total Used=5.25 MB | Peak Used=74.75 MB
```

✅ **Analysis**: 
- Memory `Used` decreased (220 MB → 185 MB → 178 MB) - healthy!
- Final memory only 5.25 MB above start - properly cleaned up
- Peak was 74.75 MB above baseline - temporary during processing

---

### Example 2: MEMORY LEAK (Accumulation Problem)
```
INFO: 🚀 Service Startup - Memory Info: Start=150.25 MB | Current=152.18 MB | Available: ~10240.00 MB
INFO: Metrics - Fetched: 100000 (10000/s) | Produced: 90000 (9000/s) | Marked: 45000 | Failed: 1000 | Elapsed: 10.0s | Memory: Start=150.25 MB | Current=220.50 MB | Used=70.25 MB | Peak=225.00 MB
INFO: Metrics - Fetched: 98000 (9800/s) | Produced: 88000 (8800/s) | Marked: 44000 | Failed: 980 | Elapsed: 10.0s | Memory: Start=150.25 MB | Current=290.75 MB | Used=140.50 MB | Peak=290.75 MB
INFO: Metrics - Fetched: 102000 (10200/s) | Produced: 91000 (9100/s) | Marked: 45500 | Failed: 1020 | Elapsed: 10.0s | Memory: Start=150.25 MB | Current=385.25 MB | Used=235.00 MB | Peak=385.25 MB
INFO: Metrics - Fetched: 99000 (9900/s) | Produced: 89000 (8900/s) | Marked: 44500 | Failed: 990 | Elapsed: 10.0s | Memory: Start=150.25 MB | Current=512.80 MB | Used=362.55 MB | Peak=512.80 MB
INFO: ⚠️  WARNING: Memory usage critical! (512.80 MB / 1024.00 MB available)
INFO: 🛑 Service Shutdown - Memory Summary: Start=150.25 MB | Final=512.80 MB | Total Used=362.55 MB | Peak Used=362.55 MB
```

❌ **Analysis**:
- Memory `Used` INCREASED (70 → 140 → 235 → 362 MB) - LEAK!
- `Peak` constantly increasing - never declining
- Final memory 362.55 MB above start - memory not released
- **Action needed**: Check for unreleased objects, connection pools, caches

---

### Example 3: SPIKE THEN RECOVERY (Normal Pattern)
```
INFO: 🚀 Service Startup - Memory Info: Start=100.00 MB | Current=102.00 MB | Available: ~8192.00 MB
INFO: Metrics - Fetched: 50000 (5000/s) | Produced: 45000 (4500/s) | Marked: 22500 | Failed: 500 | Elapsed: 10.0s | Memory: Start=100.00 MB | Current=180.50 MB | Used=80.50 MB | Peak=180.50 MB
INFO: Metrics - Fetched: 55000 (5500/s) | Produced: 49500 (4950/s) | Marked: 27500 | Failed: 550 | Elapsed: 10.0s | Memory: Start=100.00 MB | Current=250.25 MB | Used=150.25 MB | Peak=250.25 MB
INFO: Metrics - Fetched: 10000 (1000/s) | Produced: 9000 (900/s) | Marked: 5000 | Failed: 100 | Elapsed: 10.0s | Memory: Start=100.00 MB | Current=125.75 MB | Used=25.75 MB | Peak=250.25 MB
INFO: Metrics - Fetched: 5000 (500/s) | Produced: 4500 (450/s) | Marked: 2500 | Failed: 50 | Elapsed: 10.0s | Memory: Start=100.00 MB | Current=110.50 MB | Used=10.50 MB | Peak=250.25 MB
INFO: 🛑 Service Shutdown - Memory Summary: Start=100.00 MB | Final=110.50 MB | Total Used=10.50 MB | Peak Used=150.25 MB
```

✅ **Analysis**:
- Spike occurred: Memory went from 100 MB to 250 MB (processing burst)
- Recovery occurred: Memory dropped to 110 MB (garbage collection)
- Final memory close to start - no accumulation
- Peak shows max pressure (150 MB above baseline during burst)

---

## Quick Interpretation Guide

| Metric | What It Means | Healthy Range | Warning Signs |
|--------|--------------|---------------|----------------|
| **Start MB** | Baseline memory | N/A | Should not change once set |
| **Current MB** | Memory right now | < Start + (2x max window) | Constantly growing |
| **Used MB** | Above baseline | Variable, should reset | Accumulating |
| **Peak MB** | Highest ever | < Available / 2 | Growing monotonically |

---

## How to Export & Analyze

### Extract Memory Data from Logs
```bash
# Get all memory metrics
grep "Memory:" app-logs.txt

# Get startup memory
grep "🚀 Service Startup" app-logs.txt

# Get shutdown summary  
grep "🛑 Service Shutdown" app-logs.txt

# Monitor memory over time (Linux/Mac)
grep "Memory:" app-logs.txt | awk '{print $NF}' | sort -u
```

### Convert to CSV
```
Timestamp,StartMB,CurrentMB,UsedMB,PeakMB
2024-01-18T10:00:01Z,125.45,156.78,31.33,167.45
2024-01-18T10:00:11Z,125.45,148.92,23.47,167.45
2024-01-18T10:00:21Z,125.45,142.15,16.70,167.45
```

Then import into Excel/PowerBI for visualization.

---

## Key Takeaways

1. ✅ **Memory Reset Mechanism**: Counters reset every 10 seconds - see metrics for that window
2. 📈 **Peak Tracking**: Peak memory never resets - shows maximum pressure during entire run
3. 🔍 **Quick Check**: If `Used` MB stays low and stable → healthy
4. ⚠️ **Red Flag**: If `Used` MB keeps growing → investigate for memory leak
5. 🎯 **Production Ready**: All memory tracking is thread-safe and production-safe

---

**Next Steps**: 
- Start the service and monitor logs for memory metrics
- Compare startup, periodic, and shutdown memory logs
- If memory is stable, feature is working correctly
- If memory grows unbounded, investigate application state management
