$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$opts = @{
    RootDir = "artifacts/chaos-compare"
    RunId = ""
    SuccessRateThreshold = 99.5
    P95RatioThreshold = 3.0
}

for ($i = 0; $i -lt $args.Count; $i++) {
    $name = [string]$args[$i]
    switch ($name.ToLowerInvariant()) {
        "-rootdir" { $i++; $opts.RootDir = [string]$args[$i] }
        "-runid" { $i++; $opts.RunId = [string]$args[$i] }
        "-successratethreshold" { $i++; $opts.SuccessRateThreshold = [double]$args[$i] }
        "-p95ratiothreshold" { $i++; $opts.P95RatioThreshold = [double]$args[$i] }
        default { throw "Unknown argument: $name" }
    }
}

function Resolve-RunDir([string]$rootDir, [string]$runId) {
    if (-not [string]::IsNullOrWhiteSpace($runId)) {
        $dir = Join-Path $rootDir ("run-" + $runId)
        if (-not (Test-Path -LiteralPath $dir)) {
            throw "Run directory not found: $dir"
        }
        return $dir
    }

    if (-not (Test-Path -LiteralPath $rootDir)) {
        throw "Root directory not found: $rootDir"
    }

    $latest = Get-ChildItem -LiteralPath $rootDir -Directory |
        Where-Object { $_.Name -like "run-*" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $latest) {
        throw "No run directories found under: $rootDir"
    }
    return $latest.FullName
}

function To-Double([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return [double]::NaN }
    $parsed = 0.0
    if ([double]::TryParse($value, [ref]$parsed)) {
        return $parsed
    }
    return [double]::NaN
}

function To-Int([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return 0 }
    $parsed = 0
    if ([int]::TryParse($value, [ref]$parsed)) {
        return $parsed
    }
    return 0
}

function Parse-MixedMethodP95([string]$logPath) {
    $map = @{}
    if ([string]::IsNullOrWhiteSpace($logPath)) {
        return $map
    }

    if (-not (Test-Path -LiteralPath $logPath)) {
        return $map
    }

    foreach ($line in Get-Content -LiteralPath $logPath) {
        if ($line -match "^\[GrpcStress:(?<method>[^\]]+)\].*p95=(?<p95>[0-9\.]+)ms, p99=(?<p99>[0-9\.]+)ms") {
            $map[$Matches["method"]] = [pscustomobject]@{
                P95 = [double]$Matches["p95"]
                P99 = [double]$Matches["p99"]
            }
        }
    }
    return $map
}

function Build-MarkdownReport([string]$path, [object[]]$rows, [double]$successThreshold, [double]$ratioThreshold) {
    $lines = @()
    $lines += "# Chaos Compare Analysis"
    $lines += ""
    $lines += "- Success rate threshold: $successThreshold%"
    $lines += "- Worst method p95 ratio threshold: $ratioThreshold x"
    $lines += ""

    $scenarioGroups = $rows | Group-Object Scenario | Sort-Object Name
    foreach ($scenario in $scenarioGroups) {
        $lines += "## Scenario: $($scenario.Name)"
        foreach ($entry in ($scenario.Group | Sort-Object Suite)) {
            $lines += "- Suite $($entry.Suite): verdict=$($entry.Verdict); class=$($entry.FailureClass); reason=$($entry.FailureReason); successRate=$($entry.SuccessRate)% ; mixedP95=$($entry.MixedP95) ; mixedP99=$($entry.MixedP99) ; mixedQps=$($entry.MixedQps) ; worstMethodP95Ratio=$($entry.WorstMethodP95Ratio)"
        }
        $lines += ""
    }

    Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
}

$runDir = Resolve-RunDir -rootDir $opts.RootDir -runId $opts.RunId
$summaryPath = Join-Path $runDir "summary.all.csv"
if (-not (Test-Path -LiteralPath $summaryPath)) {
    throw "summary.all.csv not found: $summaryPath"
}

$rows = Import-Csv -LiteralPath $summaryPath
$groups = $rows | Group-Object Scenario, Suite
$analysisRows = @()

foreach ($group in $groups) {
    $parts = $group.Name -split ", "
    $scenario = $parts[0]
    $suite = $parts[1]
    $groupRows = $group.Group
    $failureClass = ($groupRows | Select-Object -First 1).FailureClass
    $failureReason = ($groupRows | Select-Object -First 1).FailureReason

    $successTotal = ($groupRows | Measure-Object -Property Success -Sum).Sum
    $failedTotal = ($groupRows | Measure-Object -Property Failed -Sum).Sum
    $successTotal = To-Int "$successTotal"
    $failedTotal = To-Int "$failedTotal"
    $totalOps = $successTotal + $failedTotal
    $successRate = if ($totalOps -gt 0) { [Math]::Round(($successTotal * 100.0) / $totalOps, 2) } else { [double]::NaN }

    $mixed = $groupRows | Where-Object { $_.Phase -eq "mixed" } | Select-Object -First 1
    $mixedP95 = if ($null -eq $mixed) { [double]::NaN } else { To-Double $mixed.P95 }
    $mixedP99 = if ($null -eq $mixed) { [double]::NaN } else { To-Double $mixed.P99 }
    $mixedQps = if ($null -eq $mixed) { [double]::NaN } else { To-Double $mixed.Qps }
    $mixedLogPath = if ($null -eq $mixed) { "" } else { [string]$mixed.Log }

    $baselineP95 = @{}
    foreach ($b in ($groupRows | Where-Object { $_.Phase -like "baseline-*" })) {
        $method = [string]$b.Method
        $value = To-Double $b.P95
        if (-not [double]::IsNaN($value)) {
            $baselineP95[$method] = $value
        }
    }

    $mixedMethodP95 = Parse-MixedMethodP95 -logPath $mixedLogPath
    $ratios = @()
    foreach ($method in $mixedMethodP95.Keys) {
        if ($baselineP95.ContainsKey($method) -and $baselineP95[$method] -gt 0) {
            $ratio = [Math]::Round($mixedMethodP95[$method].P95 / $baselineP95[$method], 2)
            $ratios += [pscustomobject]@{
                Method = $method
                Ratio = $ratio
            }
        }
    }
    $worstRatio = if ($ratios.Count -gt 0) { ($ratios | Sort-Object Ratio -Descending | Select-Object -First 1).Ratio } else { [double]::NaN }

    $isDryRunData = @($groupRows | Where-Object { $_.Status -ne "DRYRUN" }).Count -eq 0
    if ($isDryRunData) {
        $passSuccess = "N/A"
        $passP95Ratio = "N/A"
        $verdict = "DRYRUN"
    }
    else {
        $passSuccessBool = (-not [double]::IsNaN($successRate)) -and ($successRate -ge $opts.SuccessRateThreshold)
        $passP95RatioBool = ([double]::IsNaN($worstRatio)) -or ($worstRatio -le $opts.P95RatioThreshold)
        $passSuccess = "$passSuccessBool"
        $passP95Ratio = "$passP95RatioBool"
        $verdict = if ($passSuccessBool -and $passP95RatioBool) { "PASS" } else { "FAIL" }
    }

    $analysisRows += [pscustomobject]@{
        Scenario = $scenario
        Suite = $suite
        Success = $successTotal
        Failed = $failedTotal
        SuccessRate = if ([double]::IsNaN($successRate)) { "" } else { $successRate }
        MixedQps = if ([double]::IsNaN($mixedQps)) { "" } else { $mixedQps }
        MixedP95 = if ([double]::IsNaN($mixedP95)) { "" } else { $mixedP95 }
        MixedP99 = if ([double]::IsNaN($mixedP99)) { "" } else { $mixedP99 }
        WorstMethodP95Ratio = if ([double]::IsNaN($worstRatio)) { "" } else { $worstRatio }
        PassSuccessRate = $passSuccess
        PassP95Ratio = $passP95Ratio
        Verdict = $verdict
        FailureClass = $failureClass
        FailureReason = $failureReason
    }
}

$analysisCsv = Join-Path $runDir "analysis.summary.csv"
$analysisRows | Sort-Object Scenario, Suite | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $analysisCsv

$analysisMd = Join-Path $runDir "analysis.report.md"
Build-MarkdownReport -path $analysisMd -rows ($analysisRows | Sort-Object Scenario, Suite) -successThreshold $opts.SuccessRateThreshold -ratioThreshold $opts.P95RatioThreshold

Write-Host "RunDir: $runDir"
Write-Host "Analysis CSV: $analysisCsv"
Write-Host "Analysis Report: $analysisMd"
