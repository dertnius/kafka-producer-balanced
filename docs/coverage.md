# 📊 Test Coverage Report

> Automated code coverage analysis generated on every push to GitHub

## Status

✅ **Workflow Active** - Test coverage is automatically collected and reported on each commit

## Coverage Report

- **Live HTML report**: [Open full report](coverage/index.html)
- **Badges**: Generated alongside the report in `docs/coverage/`
- **Source**: Produced by the [GitHub Actions workflow](https://github.com/dertnius/kafka-producer-balanced/actions/workflows/test-and-coverage.yml)

### Inline preview

<iframe
  src="coverage/index.html"
  style="width: 100%; height: 75vh; border: 1px solid #ddd; border-radius: 6px;"
  title="Coverage Report"
  loading="lazy"
></iframe>

## Running Tests Locally

To generate and view the coverage report on your machine:

```powershell
cd MyDotNetSolution
dotnet test Tests/MyDotNetApp.Tests/MyDotNetApp.Tests.csproj `
  --configuration Release `
  --collect:"XPlat Code Coverage" `
  --results-directory:TestResults
```

## Coverage Tools

- **Collector**: Coverlet (built into .NET test SDK)
- **Generator**: ReportGenerator
- **Format**: Cobertura XML → HTML Report

## See Also

- [Testing Commands](TESTING_COMMANDS.md)
- [Test Report](TEST_REPORT.md)
- [GitHub Actions Workflow](https://github.com/dertnius/kafka-producer-balanced/actions)

