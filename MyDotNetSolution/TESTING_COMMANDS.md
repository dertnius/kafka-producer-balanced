# Testing Commands - PowerShell Reference

## Quick Commands

### 1. Build and Run All Tests
```powershell
# Build solution
dotnet build

# Run all tests
dotnet test

# Build and test in one command
dotnet build && dotnet test
```

---

## Test Execution Options

### 2. Run Tests Without Building
```powershell
# Skip build step (faster for repeated runs)
dotnet test --no-build
```

### 3. Run Specific Test Class
```powershell
# Run only OutboxConsumerServiceTests
dotnet test --filter "FullyQualifiedName~OutboxConsumerServiceTests"

# Run only OutboxConsumerParallelTests
dotnet test --filter "FullyQualifiedName~OutboxConsumerParallelTests"

# Run only OutboxProcessorServiceScaledTests
dotnet test --filter "FullyQualifiedName~OutboxProcessorServiceScaledTests"
```

### 4. Run Specific Test Method
```powershell
# Run single test by exact name
dotnet test --filter "FullyQualifiedName=MyDotNetApp.Tests.OutboxConsumerParallelTests.ParallelExecution_IsFasterThanSequential"

# Run tests matching pattern
dotnet test --filter "Name~Parallel"
```

---

## Output Verbosity Levels

### 5. Minimal Output (Default)
```powershell
dotnet test --verbosity minimal
```
**Shows:** Pass/Fail summary only

### 6. Normal Output
```powershell
dotnet test --verbosity normal
```
**Shows:** Test names, timing, pass/fail

### 7. Detailed Output
```powershell
dotnet test --verbosity detailed
```
**Shows:** Full diagnostic information, all logs

### 8. Quiet Output
```powershell
dotnet test --verbosity quiet
```
**Shows:** Errors only

---

## Console Logger Options

### 9. Detailed Console Output
```powershell
dotnet test --logger "console;verbosity=detailed"
```
**Shows:** Test execution order, timing, detailed results

### 10. Normal Console Output
```powershell
dotnet test --logger "console;verbosity=normal"
```
**Shows:** Standard test execution info

---

## Filtering and Searching Results

### 11. Show Only Test Names
```powershell
# List all tests without running
dotnet test --list-tests
```

### 12. List Only OutboxConsumer Tests
```powershell
dotnet test --list-tests | Select-String "OutboxConsumer"
```

### 13. Show Pass/Fail Summary
```powershell
dotnet test 2>&1 | Select-String -Pattern "Passed|Failed"
```

### 14. Show Last 10 Lines (Summary)
```powershell
dotnet test 2>&1 | Select-Object -Last 10
```

### 15. Show First 50 Lines
```powershell
dotnet test 2>&1 | Select-Object -First 50
```

### 16. Show Only Test Timings
```powershell
dotnet test --logger "console;verbosity=detailed" 2>&1 | Select-String -Pattern "\[.*ms\]"
```

---

## Parallel Test Specific Commands

### 17. Run Only Parallel Tests with Timing
```powershell
dotnet test --no-build --filter "FullyQualifiedName~OutboxConsumerParallelTests" --logger "console;verbosity=detailed"
```

### 18. Show Parallel Test Results Only
```powershell
dotnet test --no-build --filter "FullyQualifiedName~OutboxConsumerParallelTests" 2>&1 | Select-String -Pattern "Passed|Failed|Total"
```

### 19. Count Parallel Tests
```powershell
dotnet test --list-tests | Select-String "OutboxConsumerParallelTests" | Measure-Object | Select-Object -ExpandProperty Count
```

---

## Performance Analysis

### 20. Show All Tests Sorted by Execution Time
```powershell
dotnet test --logger "console;verbosity=detailed" 2>&1 | 
    Select-String -Pattern "Passed.*\[(.*ms|.*s)\]" | 
    Sort-Object
```

### 21. Show Slowest Tests
```powershell
dotnet test --logger "console;verbosity=detailed" --no-build 2>&1 | 
    Select-String -Pattern "Passed.*\[(\d+)\s*ms\]" | 
    Select-Object -Last 10
```

### 22. Measure Total Test Execution Time
```powershell
Measure-Command { dotnet test --no-build }
```

---

## Coverage and Reporting

### 23. Run Tests with Code Coverage
```powershell
dotnet test --collect:"XPlat Code Coverage"
```

### 24. Run Tests and Generate HTML Report
```powershell
# Requires ReportGenerator tool
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coveragereport -reporttypes:Html
```

---

## Custom Output Formats

### 25. Save Test Results to File
```powershell
dotnet test --logger "console;verbosity=detailed" > test-results.txt
```

### 26. Save Only Failures to File
```powershell
dotnet test 2>&1 | Select-String -Pattern "Failed" > test-failures.txt
```

### 27. Export Test List to CSV
```powershell
dotnet test --list-tests | 
    Select-String "MyDotNetApp.Tests" | 
    ForEach-Object { $_.Line } | 
    Out-File -FilePath test-list.csv
```

---

## Watch Mode (Continuous Testing)

### 28. Run Tests on File Changes
```powershell
dotnet watch test
```

### 29. Watch Specific Tests
```powershell
dotnet watch test --filter "FullyQualifiedName~OutboxConsumer"
```

---

## Multi-Project Testing

### 30. Test Specific Project
```powershell
dotnet test tests/MyDotNetApp.Tests/MyDotNetApp.Tests.csproj
```

### 31. Test All Projects in Solution
```powershell
dotnet test MyDotNetSolution.sln
```

---

## Parallel Test Execution

### 32. Run Tests in Parallel (Default)
```powershell
dotnet test --parallel
```

### 33. Run Tests Sequentially
```powershell
dotnet test --parallel 1
```

### 34. Specify Max Parallel Jobs
```powershell
dotnet test --parallel 4
```

---

## Advanced Filtering

### 35. Run Tests by Category
```powershell
dotnet test --filter "Category=Integration"
```

### 36. Exclude Slow Tests
```powershell
dotnet test --filter "Category!=Slow"
```

### 37. Multiple Filter Conditions
```powershell
dotnet test --filter "(FullyQualifiedName~OutboxConsumer)&(Category=Unit)"
```

---

## Debugging Output

### 38. Show Diagnostic Logs
```powershell
dotnet test --diag:log.txt
```

### 39. Show Environment Variables
```powershell
dotnet test --logger "console;verbosity=detailed" --collect:"XPlat Code Coverage" --settings test.runsettings
```

---

## Custom Analysis Commands

### 40. Count Test Results by Type
```powershell
dotnet test --no-build 2>&1 | 
    Select-String -Pattern "Passed|Failed|Skipped" | 
    Group-Object | 
    Select-Object Name, Count
```

### 41. Show Only E2E Test Results
```powershell
dotnet test --filter "FullyQualifiedName~E2E" --logger "console;verbosity=detailed"
```

### 42. Display Test Performance Statistics
```powershell
@"
========================================
TEST PERFORMANCE STATISTICS
========================================
"@

$results = dotnet test --no-build --logger "console;verbosity=detailed" 2>&1

$passed = ($results | Select-String "Passed").Count
$failed = ($results | Select-String "Failed").Count
$total = ($results | Select-String "Total tests:").Line

Write-Host "Total Passed: $passed"
Write-Host "Total Failed: $failed"
Write-Host $total
```

### 43. Extract Parallel Test Metrics
```powershell
dotnet test --no-build --filter "FullyQualifiedName~ParallelExecution_IsFasterThanSequential" --logger "console;verbosity=detailed" 2>&1 | 
    Select-String -Pattern "Passed|ms"
```

### 44. Show Consumer Test Coverage
```powershell
@"
========================================
OUTBOX CONSUMER TEST COVERAGE
========================================
"@

$allTests = dotnet test --list-tests 2>&1 | Select-String "OutboxConsumer"
$unitTests = $allTests | Select-String "OutboxConsumerServiceTests"
$parallelTests = $allTests | Select-String "OutboxConsumerParallelTests"

Write-Host "Total Consumer Tests: $(($allTests | Measure-Object).Count)"
Write-Host "Unit Tests: $(($unitTests | Measure-Object).Count)"
Write-Host "Parallel Tests: $(($parallelTests | Measure-Object).Count)"
```

---

## Real-Time Monitoring

### 45. Watch Test Output in Real-Time
```powershell
dotnet test --logger "console;verbosity=detailed" | Out-Host
```

### 46. Monitor Test Execution Progress
```powershell
dotnet test --logger "console;verbosity=normal" 2>&1 | 
    ForEach-Object { 
        Write-Host $_ -ForegroundColor Cyan
    }
```

---

## Comprehensive Test Report

### 47. Generate Complete Test Report
```powershell
@"
========================================
COMPLETE TEST REPORT
========================================
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
========================================
"@

# List all tests
Write-Host "`n=== ALL TESTS ===" -ForegroundColor Yellow
dotnet test --list-tests

# Run tests with timing
Write-Host "`n=== TEST EXECUTION ===" -ForegroundColor Yellow
dotnet test --no-build --logger "console;verbosity=detailed"

# Summary
Write-Host "`n=== SUMMARY ===" -ForegroundColor Yellow
dotnet test --no-build 2>&1 | Select-String -Pattern "Total tests|Passed|Failed"
```

### 48. Parallel Tests Summary Report
```powershell
@"
========================================
PARALLEL TESTS EXECUTION REPORT
========================================
"@

dotnet test --no-build `
    --filter "FullyQualifiedName~OutboxConsumerParallelTests" `
    --logger "console;verbosity=detailed" 2>&1 | 
    Select-String -Pattern "OutboxConsumerParallelTests|Passed|ms|Total"
```

---

## Comparison Commands

### 49. Compare Sequential vs Parallel Performance
```powershell
Write-Host "Running ParallelExecution_IsFasterThanSequential test..."
dotnet test --no-build `
    --filter "Name~ParallelExecution_IsFasterThanSequential" `
    --logger "console;verbosity=detailed"
```

### 50. Full Test Suite with Statistics
```powershell
@"
========================================
FULL TEST SUITE EXECUTION
========================================
"@

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$output = dotnet test --no-build --logger "console;verbosity=detailed" 2>&1
$stopwatch.Stop()

Write-Host "`nExecution Time: $($stopwatch.Elapsed.TotalSeconds) seconds" -ForegroundColor Green
Write-Host "`n=== RESULTS ===" -ForegroundColor Yellow
$output | Select-String -Pattern "Passed|Failed|Total tests"
```

---

## Most Useful Daily Commands

### Quick Test Run (Recommended)
```powershell
dotnet test --no-build --logger "console;verbosity=normal"
```

### Check Parallel Tests Only
```powershell
dotnet test --no-build --filter "FullyQualifiedName~OutboxConsumerParallelTests"
```

### Full Report with Timing
```powershell
dotnet test --logger "console;verbosity=detailed" 2>&1 | Select-Object -First 100
```

### Watch Mode for Development
```powershell
dotnet watch test --filter "FullyQualifiedName~OutboxConsumer"
```

---

## Notes

- Use `--no-build` for faster repeated test runs after initial build
- `2>&1` redirects stderr to stdout for PowerShell filtering
- `Select-String` filters output by pattern
- `Select-Object -First/Last N` limits output lines
- `Measure-Command` measures execution time
- Tests run in parallel by default for better performance

---

## Environment Variables

```powershell
# Set test environment
$env:DOTNET_ENVIRONMENT = "Test"
dotnet test

# Enable detailed logging
$env:VSTEST_CONSOLE_LOG_LEVEL = "Verbose"
dotnet test
```
