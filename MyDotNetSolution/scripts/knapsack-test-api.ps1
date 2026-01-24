# Simple PowerShell API to run knapsack tests via dotnet test and expose results
# Usage:
#   pwsh -File .\MyDotNetSolution\scripts\knapsack-test-api.ps1
# Endpoints:
#   GET  /status   -> current status
#   POST /run      -> start tests
#   GET  /results  -> JSON summary from TRX
#   POST /stop     -> stop running test

param(
    [int]$Port = 5055
)

Add-Type -AssemblyName System.Net.HttpListener

$listener = [System.Net.HttpListener]::new()
$prefix = "http://localhost:$Port/"
$listener.Prefixes.Add($prefix)

$global:testJob = $null
$global:testStatus = @{ state = 'idle'; startedAt = $null; finishedAt = $null; message = 'Ready' }
$global:resultsPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..' | Join-Path -ChildPath 'test-output'
$global:trxFile = Join-Path $global:resultsPath 'knapsack.trx'

function Ensure-ResultsDir {
    if (-not (Test-Path $global:resultsPath)) { New-Item -ItemType Directory -Path $global:resultsPath | Out-Null }
}

function Write-Response {
    param([System.Net.HttpListenerResponse]$response, [string]$content, [string]$contentType = 'application/json')
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($content)
    $response.ContentType = $contentType
    $response.ContentLength64 = $bytes.Length
    $response.OutputStream.Write($bytes, 0, $bytes.Length)
    $response.OutputStream.Close()
}

function Start-Tests {
    if ($global:testJob -and $global:testJob.State -eq 'Running') {
        return $false
    }
    Ensure-ResultsDir
    if (Test-Path $global:trxFile) { Remove-Item $global:trxFile -Force }

    $global:testStatus = @{ state = 'running'; startedAt = (Get-Date).ToString('o'); finishedAt = $null; message = 'Running knapsack tests' }

    $solutionDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..'

    $global:testJob = Start-Job -ScriptBlock {
        param($solutionDir, $resultsPath)
        Push-Location $solutionDir
        try {
            $cmd = 'dotnet test --no-build --filter "FullyQualifiedName~Knapsack" --logger "trx;LogFileName=knapsack.trx" --results-directory "' + $resultsPath + '"'
            & pwsh -NoLogo -NoProfile -Command $cmd
        } finally {
            Pop-Location
        }
    } -ArgumentList $solutionDir, $global:resultsPath

    return $true
}

function Stop-Tests {
    if ($global:testJob) {
        try { Stop-Job -Job $global:testJob -Force } catch {}
        Remove-Job -Job $global:testJob -Force
        $global:testJob = $null
        $global:testStatus = @{ state = 'stopped'; startedAt = $global:testStatus.startedAt; finishedAt = (Get-Date).ToString('o'); message = 'Stopped by user' }
        return $true
    }
    return $false
}

function Get-StatusJson {
    if ($global:testJob) {
        $state = $global:testJob.State
        if ($state -ne 'Running' -and $global:testStatus.state -eq 'running') {
            # Job finished
            $global:testStatus = @{ state = 'completed'; startedAt = $global:testStatus.startedAt; finishedAt = (Get-Date).ToString('o'); message = 'Completed' }
        }
    }
    return ($global:testStatus | ConvertTo-Json -Compress)
}

function Parse-TrxToJson {
    if (-not (Test-Path $global:trxFile)) { return '{"error":"No TRX found yet"}' }
    try {
        [xml]$trx = Get-Content -Path $global:trxFile -ErrorAction Stop
        $ns = $trx.TestRun
        $summary = $ns.ResultSummary
        $counters = $summary.Counters
        $results = @()
        foreach ($r in $ns.Results.UnitTestResult) {
            $obj = [ordered]@{
                testName = $r.testName
                outcome = $r.outcome
                duration = $r.duration
                startTime = $r.startTime
                endTime = $r.endTime
                stdout = ($r.Output.StdOut -join "\n")
            }
            $results += (New-Object PSObject -Property $obj)
        }
        $payload = [ordered]@{
            summary = [ordered]@{
                outcome = $summary.outcome
                total = [int]$counters.total
                passed = [int]$counters.passed
                failed = [int]$counters.failed
                skipped = [int]$counters.notExecuted
            }
            results = $results
        }
        return ($payload | ConvertTo-Json -Depth 4)
    } catch {
        return ('{"error":"Failed parsing TRX: ' + ($_.Exception.Message) + '"}')
    }
}

$listener.Start()
Write-Host "Knapsack Test API listening at $prefix"

try {
    while ($true) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request
        $res = $ctx.Response
        switch -Regex ($req.HttpMethod + ' ' + $req.Url.AbsolutePath) {
            '^GET /status$' { Write-Response -response $res -content (Get-StatusJson); break }
            '^POST /run$' {
                $started = Start-Tests
                Write-Response -response $res -content ('{"started":' + ($started.ToString().ToLower()) + '}')
                break
            }
            '^GET /results$' { Write-Response -response $res -content (Parse-TrxToJson); break }
            '^POST /stop$' {
                $stopped = Stop-Tests
                Write-Response -response $res -content ('{"stopped":' + ($stopped.ToString().ToLower()) + '}')
                break
            }
            default { Write-Response -response $res -content '{"error":"Not Found"}' }
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}