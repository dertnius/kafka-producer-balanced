# 🚀 Quick Start: VS Code Tasks for Testing

## Available Tasks

You can now run tests directly from VS Code using the Tasks feature. Press **Ctrl+Shift+B** (or Cmd+Shift+B on Mac) to see all available tasks.

### Test Tasks

#### 1. **Run All Tests** (Default)
- **Shortcut:** Ctrl+Shift+B → Select "Run All Tests"
- **What it does:** Runs all 93 tests with minimal verbosity
- **Duration:** ~27 seconds
- **Output:** Test results with pass/fail counts

```bash
dotnet test --no-build -v minimal
```

#### 2. **Run All Tests with Memory Report** ⭐ (RECOMMENDED)
- **Shortcut:** Ctrl+Shift+B → Select "Run All Tests with Memory Report"
- **What it does:** 
  - Runs all 93 tests
  - Shows memory cleanup status
  - Displays process memory (Working Set, Private Memory, Handles)
  - Verifies garbage collection
  - References the TEST_REPORT.md file
- **Duration:** ~35 seconds (includes GC verification)
- **Best for:** Regular testing and verification

#### 3. **Run Tests (Non-Knapsack Only)**
- **Shortcut:** Ctrl+Shift+B → Select "Run Tests (Non-Knapsack Only)"
- **What it does:** Runs 75 tests excluding knapsack algorithm tests
- **Duration:** ~10 seconds
- **Use case:** Quick validation of outbox processing logic

#### 4. **Run Tests (Knapsack Only)**
- **Shortcut:** Ctrl+Shift+B → Select "Run Tests (Knapsack Only)"
- **What it does:** Runs 18 knapsack algorithm tests
- **Duration:** ~14 seconds
- **Use case:** Isolate knapsack algorithm validation

### Build Tasks

#### 5. **Build Solution** (Default)
- **Shortcut:** Ctrl+Shift+B → Select "Build Solution"
- **What it does:** Compiles the entire solution

#### 6. **Clean Solution**
- **Shortcut:** Ctrl+Shift+B → Select "Clean Solution"
- **What it does:** Removes all build artifacts

#### 7. **Rebuild Solution**
- **Shortcut:** Ctrl+Shift+B → Select "Rebuild Solution"
- **What it does:** Cleans and rebuilds the entire solution

---

## 📊 Expected Output Example

### When running "Run All Tests with Memory Report":

```
=== STARTING TEST EXECUTION ===
Running all 93 tests...

Test run for C:\...\MyDotNetApp.Tests.dll (.NETCoreApp,Version=v8.0)
Microsoft (R) Test Execution Command Line Tool Version 17.8.0

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    93, Skipped:    0, Total:    93, Duration: 27 s

=== MEMORY CLEANUP VERIFICATION ===
✓ All generations collected
✓ All finalizers executed

Current Process Memory Status:
  Working Set: 121.07 MB
  Private Memory: 39.94 MB
  Handles: 817

✓ Memory is clean and ready for next operations

=== TEST REPORT ===
See TEST_REPORT.md in the workspace root for detailed analysis
```

---

## 📈 Test Metrics Reference

| Test Suite | Count | Duration | Status |
|-----------|-------|----------|--------|
| **All Tests** | 93 | 27 sec | ✅ PASSING |
| **Non-Knapsack** | 75 | 10 sec | ✅ PASSING |
| **Knapsack** | 18 | 14 sec | ✅ PASSING |

---

## 🔍 Detailed Reports

After running tests, check:
- **[TEST_REPORT.md](./TEST_REPORT.md)** - Comprehensive test analysis with memory metrics
- **Terminal Output** - Real-time test execution feedback

---

## 💡 Pro Tips

1. **Keyboard Shortcut:**
   - Press `Ctrl+Shift+B` to open task menu
   - Type task name to filter (e.g., "Memory Report")
   - Press Enter to execute

2. **Terminal Integration:**
   - Tasks run in VS Code's integrated terminal
   - Output is visible and searchable
   - Errors are highlighted with problem matcher

3. **Memory Monitoring:**
   - Use "Run All Tests with Memory Report" for comprehensive verification
   - Run regularly to detect memory leaks early
   - Check TEST_REPORT.md for historical trends

4. **CI/CD Integration:**
   - These tasks can be called from automation
   - PowerShell commands work on Windows, macOS, and Linux
   - Add to GitHub Actions or other CI pipelines if needed

---

## ⚠️ Troubleshooting

**Task not showing up?**
- Make sure you're in the workspace root
- Check that `.vscode/tasks.json` exists
- Reload VS Code (Ctrl+R or Cmd+R)

**Command not found error?**
- Ensure .NET SDK is installed
- Verify you're in the correct directory
- Check that the project builds successfully

**Memory report shows errors?**
- Run "Build Solution" first
- Ensure no other processes are using the test DLLs
- Try "Rebuild Solution" if issues persist

---

## 📝 Notes

- All test tasks use `--no-build` to reuse existing binaries (faster execution)
- If you modify code, run "Build Solution" before testing
- The memory report verifies garbage collection and finalizers
- Test isolation is maintained through proper IDisposable patterns

---

**Last Updated:** January 24, 2026  
**Framework:** .NET 8.0  
**Test Count:** 93 (18 Knapsack + 75 Outbox Processing)
