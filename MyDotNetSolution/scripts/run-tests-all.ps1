param(
    [string]$ResultsDir = "..\Tests\MyDotNetApp.Tests\TestResults"
)
Push-Location $PSScriptRoot
Push-Location ..\Tests\MyDotNetApp.Tests
try {
    dotnet test .\MyDotNetApp.Tests.csproj --no-build --logger "trx;LogFileName=all.trx" --results-directory "$ResultsDir"
}
finally {
    Pop-Location
    Pop-Location
}
