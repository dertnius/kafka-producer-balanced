# Copy all markdown files to docs folder for Docsify
# Run from: C:\Users\midgard\dev\kafka-producer-balanced\MyDotNetSolution

Write-Host "🚀 Copying markdown files to docs/..." -ForegroundColor Cyan

# Create docs directory if it doesn't exist
if (-not (Test-Path "docs")) {
    New-Item -ItemType Directory -Path "docs" | Out-Null
}

# List of files to copy
$files = @(
    'README.md',
    'QUICK_REFERENCE.md',
    'PRODUCTION_READINESS.md',
    'MEMORY_VISUAL_GUIDE.md',
    'MEMORY_QUICK_START.md',
    'MEMORY_TRACKING_EXAMPLES.md',
    'MEMORY_TRACKING.md',
    'MEMORY_TRACKING_IMPLEMENTATION.md',
    'MEMORY_COMPLETE.md',
    'MEMORY_LEAK_RESOLUTION.md',
    'PUBLISHING_FEATURE_GUIDE.md',
    'PUBLISHING_IMPLEMENTATION.md',
    'README_PUBLISHING.md',
    'PRODUCTION_GUIDE.md',
    'DEPLOYMENT_CHECKLIST.md',
    'PERFORMANCE_ANALYSIS.md',
    'DOCUMENTATION_INDEX.md',
    'IMPLEMENTATION_SUMMARY.md',
    'COMPLETION_SUMMARY.md',
    'DATABASE_SCHEMA.sql',
    'mermaid.md'
)

$copied = 0
$missing = 0

foreach ($file in $files) {
    if (Test-Path $file) {
        Copy-Item $file -Destination "docs/$file" -Force
        Write-Host "✅ $file" -ForegroundColor Green
        $copied++
    } else {
        Write-Host "⚠️  $file (not found)" -ForegroundColor Yellow
        $missing++
    }
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "✅ Docsify setup complete!" -ForegroundColor Green
Write-Host "📊 Copied: $copied files" -ForegroundColor Cyan
if ($missing -gt 0) {
    Write-Host "⚠️  Missing: $missing files" -ForegroundColor Yellow
}
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host ""

Write-Host "📚 Next steps:" -ForegroundColor Cyan
Write-Host "  1. Create GitHub repository at https://github.com/new" -ForegroundColor White
Write-Host "  2. Push your code: git push origin main" -ForegroundColor White
Write-Host "  3. Enable Pages in Settings (Settings > Pages)" -ForegroundColor White
Write-Host "  4. Select 'main branch / root' or 'main branch / /docs'" -ForegroundColor White
Write-Host "  5. Your site will be at: https://yourusername.github.io/kafka-producer-balanced/" -ForegroundColor White
Write-Host ""
Write-Host "💡 View setup guide:" -ForegroundColor Cyan
Write-Host "  Open: docs/GITHUB_PAGES_SETUP.md" -ForegroundColor White
Write-Host ""
