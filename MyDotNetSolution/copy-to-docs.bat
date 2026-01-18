@echo off
REM Copy all markdown files to docs folder for Docsify

echo Copying markdown files to docs/...

REM Create docs directory if it doesn't exist
if not exist "docs" mkdir docs

REM Copy markdown files
copy README.md docs\README.md
copy QUICK_REFERENCE.md docs\QUICK_REFERENCE.md
copy PRODUCTION_READINESS.md docs\PRODUCTION_READINESS.md
copy MEMORY_VISUAL_GUIDE.md docs\MEMORY_VISUAL_GUIDE.md
copy MEMORY_QUICK_START.md docs\MEMORY_QUICK_START.md
copy MEMORY_TRACKING_EXAMPLES.md docs\MEMORY_TRACKING_EXAMPLES.md
copy MEMORY_TRACKING.md docs\MEMORY_TRACKING.md
copy MEMORY_TRACKING_IMPLEMENTATION.md docs\MEMORY_TRACKING_IMPLEMENTATION.md
copy MEMORY_COMPLETE.md docs\MEMORY_COMPLETE.md
copy MEMORY_LEAK_RESOLUTION.md docs\MEMORY_LEAK_RESOLUTION.md
copy PUBLISHING_FEATURE_GUIDE.md docs\PUBLISHING_FEATURE_GUIDE.md
copy PUBLISHING_IMPLEMENTATION.md docs\PUBLISHING_IMPLEMENTATION.md
copy README_PUBLISHING.md docs\README_PUBLISHING.md
copy PRODUCTION_GUIDE.md docs\PRODUCTION_GUIDE.md
copy DEPLOYMENT_CHECKLIST.md docs\DEPLOYMENT_CHECKLIST.md
copy PERFORMANCE_ANALYSIS.md docs\PERFORMANCE_ANALYSIS.md
copy DOCUMENTATION_INDEX.md docs\DOCUMENTATION_INDEX.md
copy IMPLEMENTATION_SUMMARY.md docs\IMPLEMENTATION_SUMMARY.md
copy COMPLETION_SUMMARY.md docs\COMPLETION_SUMMARY.md
copy DATABASE_SCHEMA.sql docs\DATABASE_SCHEMA.md
copy mermaid.md docs\mermaid.md

echo.
echo ✅ All markdown files copied to docs/
echo.
echo 📚 Docsify setup complete!
echo.
echo Next steps:
echo   1. Create GitHub repository (https://github.com/new)
echo   2. Push your code: git push origin main
echo   3. Enable Pages in Settings ^(Settings ^> Pages^)
echo   4. Select 'main branch / root' or 'main branch / /docs'
echo   5. Your site will be at: https://username.github.io/kafka-producer-balanced/
echo.
pause
