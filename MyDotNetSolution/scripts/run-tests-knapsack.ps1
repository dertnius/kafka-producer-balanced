param(
    [string]$ResultsDir = "..\Tests\MyDotNetApp.Tests\TestResults"
)
Push-Location $PSScriptRoot
Push-Location ..\Tests\MyDotNetApp.Tests
try {
    dotnet test .\MyDotNetApp.Tests.csproj --no-build --filter "FullyQualifiedName~Knapsack" --logger "trx;LogFileName=knapsack.trx" --results-directory "$ResultsDir"
}
finally {
    Pop-Location
    Pop-Location
}
