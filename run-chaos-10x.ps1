$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

$opts = @{
    Iterations = 10
    ComposeFile = "docker-compose-redis-cluster.yml"
    RootOutputDir = "artifacts/chaos-10x"
    BatchId = ""

    Scenario = "all"
    Suite = "both"
    InjectionDelaySeconds = 20
    FaultDurationSeconds = 30
    RestartAppsBeforeScenario = $false

    WarmupTotal = 50
    WarmupConcurrency = 10
    BaselineTotal = 200
    BaselineConcurrency = 40
    MixedTotal = 800
    MixedConcurrency = 80
    PipelineKeyCount = 3
    ExpirySeconds = 300
}

for ($i = 0; $i -lt $args.Count; $i++) {
    $name = [string]$args[$i]
    switch ($name.ToLowerInvariant()) {
        "-iterations" { $i++; $opts.Iterations = [int]$args[$i] }
        "-composefile" { $i++; $opts.ComposeFile = [string]$args[$i] }
        "-rootoutputdir" { $i++; $opts.RootOutputDir = [string]$args[$i] }
        "-batchid" { $i++; $opts.BatchId = [string]$args[$i] }
        "-scenario" { $i++; $opts.Scenario = [string]$args[$i] }
        "-suite" { $i++; $opts.Suite = [string]$args[$i] }
        "-injectiondelayseconds" { $i++; $opts.InjectionDelaySeconds = [int]$args[$i] }
        "-faultdurationseconds" { $i++; $opts.FaultDurationSeconds = [int]$args[$i] }
        "-restartappsbeforescenario" { $opts.RestartAppsBeforeScenario = $true }
        "-warmuptotal" { $i++; $opts.WarmupTotal = [int]$args[$i] }
        "-warmupconcurrency" { $i++; $opts.WarmupConcurrency = [int]$args[$i] }
        "-baselinetotal" { $i++; $opts.BaselineTotal = [int]$args[$i] }
        "-baselineconcurrency" { $i++; $opts.BaselineConcurrency = [int]$args[$i] }
        "-mixedtotal" { $i++; $opts.MixedTotal = [int]$args[$i] }
        "-mixedconcurrency" { $i++; $opts.MixedConcurrency = [int]$args[$i] }
        "-pipelinekeycount" { $i++; $opts.PipelineKeyCount = [int]$args[$i] }
        "-expiryseconds" { $i++; $opts.ExpirySeconds = [int]$args[$i] }
        default { throw "Unknown argument: $name" }
    }
}

function Ensure-Dir([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -Path $path -ItemType Directory | Out-Null
    }
}

function Get-BatchId([string]$candidate) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        return (Get-Date -Format "yyyyMMdd-HHmmss")
    }
    return $candidate
}

function Invoke-Docker([string[]]$argsLocal) {
    $cmdText = "docker compose -f $($opts.ComposeFile) " + ($argsLocal -join " ")
    Write-Host "[Batch] $cmdText"
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & docker compose -f $opts.ComposeFile @argsLocal 2>&1
    }
    finally {
        $ErrorActionPreference = $prev
    }
    $output | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $cmdText"
    }
}

if ($opts.Iterations -le 0) {
    throw "Iterations must be > 0"
}

$batchId = Get-BatchId $opts.BatchId
Ensure-Dir $opts.RootOutputDir
$batchDir = Join-Path $opts.RootOutputDir ("batch-" + $batchId)
Ensure-Dir $batchDir

$chaosScript = Join-Path $PSScriptRoot "run-chaos-compare.ps1"
$analyzeScript = Join-Path $PSScriptRoot "run-chaos-analyze.ps1"
$aggregateRows = @()
$clusterServices = @(
    "redis-cluster-1",
    "redis-cluster-2",
    "redis-cluster-3",
    "redis-cluster-4",
    "redis-cluster-5",
    "redis-cluster-6",
    "redis-cluster-init",
    "app",
    "csredis-tests-app"
)

Write-Host "BatchId: $batchId"
Write-Host "Iterations: $($opts.Iterations)"
Write-Host "Output: $batchDir"

for ($iter = 1; $iter -le $opts.Iterations; $iter++) {
    $iterTag = ("{0:D2}" -f $iter)
    $iterRunId = "iter-$iterTag"
    $iterOut = Join-Path $batchDir $iterRunId
    Ensure-Dir $iterOut

    Write-Host ""
    Write-Host "========== Iteration $iter/$($opts.Iterations) =========="
    Write-Host "[Batch] Restart full redis/app stack before test..."
    Invoke-Docker @("down")
    Invoke-Docker (@("up", "-d") + $clusterServices)

    $chaosArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $chaosScript,
        "-Scenario", $opts.Scenario,
        "-Suite", $opts.Suite,
        "-ComposeFile", $opts.ComposeFile,
        "-OutputDir", $iterOut,
        "-RunId", $iterRunId,
        "-InjectionDelaySeconds", $opts.InjectionDelaySeconds,
        "-FaultDurationSeconds", $opts.FaultDurationSeconds,
        "-WarmupTotal", $opts.WarmupTotal,
        "-WarmupConcurrency", $opts.WarmupConcurrency,
        "-BaselineTotal", $opts.BaselineTotal,
        "-BaselineConcurrency", $opts.BaselineConcurrency,
        "-MixedTotal", $opts.MixedTotal,
        "-MixedConcurrency", $opts.MixedConcurrency,
        "-PipelineKeyCount", $opts.PipelineKeyCount,
        "-ExpirySeconds", $opts.ExpirySeconds
    )
    if ($opts.RestartAppsBeforeScenario) {
        $chaosArgs += "-RestartAppsBeforeScenario"
    }
    Write-Host "[Batch] Run chaos compare: powershell $($chaosArgs -join ' ')"
    & powershell @chaosArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Chaos compare failed at iteration $iter"
    }

    $iterChaosRoot = $iterOut
    $analyzeArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $analyzeScript,
        "-RootDir", $iterChaosRoot,
        "-RunId", $iterRunId
    )
    Write-Host "[Batch] Run chaos analyze: powershell $($analyzeArgs -join ' ')"
    & powershell @analyzeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Chaos analyze failed at iteration $iter"
    }

    $analysisCsv = Join-Path (Join-Path $iterOut ("run-" + $iterRunId)) "analysis.summary.csv"
    if (-not (Test-Path -LiteralPath $analysisCsv)) {
        throw "analysis.summary.csv missing at iteration ${iter}: $analysisCsv"
    }

    $rows = Import-Csv -LiteralPath $analysisCsv
    foreach ($row in $rows) {
        $aggregateRows += [pscustomobject]@{
            Iteration = $iter
            Scenario = $row.Scenario
            Suite = $row.Suite
            Success = $row.Success
            Failed = $row.Failed
            SuccessRate = $row.SuccessRate
            MixedQps = $row.MixedQps
            MixedP95 = $row.MixedP95
            MixedP99 = $row.MixedP99
            WorstMethodP95Ratio = $row.WorstMethodP95Ratio
            Verdict = $row.Verdict
            FailureClass = $row.FailureClass
            FailureReason = $row.FailureReason
        }
    }
}

$aggregateCsv = Join-Path $batchDir "aggregate.all.csv"
$aggregateRows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $aggregateCsv

$summaryRows = @()
$grouped = $aggregateRows | Group-Object Scenario, Suite
foreach ($group in $grouped) {
    $parts = $group.Name -split ", "
    $scenario = $parts[0]
    $suite = $parts[1]
    $rows = $group.Group

    $successRates = @($rows | ForEach-Object { if ($_.SuccessRate -ne "") { [double]$_.SuccessRate } })
    $p95Values = @($rows | ForEach-Object { if ($_.MixedP95 -ne "") { [double]$_.MixedP95 } })
    $p99Values = @($rows | ForEach-Object { if ($_.MixedP99 -ne "") { [double]$_.MixedP99 } })
    $qpsValues = @($rows | ForEach-Object { if ($_.MixedQps -ne "") { [double]$_.MixedQps } })
    $ratioValues = @($rows | ForEach-Object { if ($_.WorstMethodP95Ratio -ne "") { [double]$_.WorstMethodP95Ratio } })
    $passCount = @($rows | Where-Object { $_.Verdict -eq "PASS" }).Count
    $failCount = @($rows | Where-Object { $_.Verdict -eq "FAIL" }).Count

    $summaryRows += [pscustomobject]@{
        Scenario = $scenario
        Suite = $suite
        Runs = @($rows).Count
        PassCount = $passCount
        FailCount = $failCount
        AvgSuccessRate = if ($successRates.Count -gt 0) { [Math]::Round(($successRates | Measure-Object -Average).Average, 2) } else { "" }
        AvgMixedQps = if ($qpsValues.Count -gt 0) { [Math]::Round(($qpsValues | Measure-Object -Average).Average, 2) } else { "" }
        AvgMixedP95 = if ($p95Values.Count -gt 0) { [Math]::Round(($p95Values | Measure-Object -Average).Average, 2) } else { "" }
        AvgMixedP99 = if ($p99Values.Count -gt 0) { [Math]::Round(($p99Values | Measure-Object -Average).Average, 2) } else { "" }
        AvgWorstMethodP95Ratio = if ($ratioValues.Count -gt 0) { [Math]::Round(($ratioValues | Measure-Object -Average).Average, 2) } else { "" }
        TopFailureClass = (
            ($rows | Group-Object FailureClass | Sort-Object Count -Descending | Select-Object -First 1).Name
        )
    }
}

$summaryCsv = Join-Path $batchDir "aggregate.summary.csv"
$summaryRows | Sort-Object Scenario, Suite | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $summaryCsv

$reportPath = Join-Path $batchDir "aggregate.report.md"
$report = @()
$report += "# Chaos 10x Aggregate Report"
$report += ""
$report += "- BatchId: $batchId"
$report += "- Iterations: $($opts.Iterations)"
$report += "- Scenario: $($opts.Scenario)"
$report += "- Suite: $($opts.Suite)"
$report += ""
foreach ($row in ($summaryRows | Sort-Object Scenario, Suite)) {
    $report += "## $($row.Scenario) / $($row.Suite)"
    $report += "- Runs: $($row.Runs), PASS: $($row.PassCount), FAIL: $($row.FailCount)"
    $report += "- AvgSuccessRate: $($row.AvgSuccessRate)%"
    $report += "- AvgMixedQps: $($row.AvgMixedQps)"
    $report += "- AvgMixedP95: $($row.AvgMixedP95) ms"
    $report += "- AvgMixedP99: $($row.AvgMixedP99) ms"
    $report += "- AvgWorstMethodP95Ratio: $($row.AvgWorstMethodP95Ratio) x"
    $report += "- TopFailureClass: $($row.TopFailureClass)"
    $report += ""
}
Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8

Write-Host ""
Write-Host "[Batch] Completed."
Write-Host "[Batch] Aggregate all: $aggregateCsv"
Write-Host "[Batch] Aggregate summary: $summaryCsv"
Write-Host "[Batch] Aggregate report: $reportPath"
