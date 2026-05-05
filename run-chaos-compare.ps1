$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

$opts = @{
    Scenario = "all" # all|failover|node-down|scale-in
    Suite = "both"   # both|csredis|stackexchange
    ComposeFile = "docker-compose-redis-cluster.yml"
    OutputDir = "artifacts/chaos-compare"
    RunId = ""
    DryRun = $false

    CsredisTarget = "http://127.0.0.1:50051"
    StackexchangeTarget = "http://127.0.0.1:50052"

    InjectionDelaySeconds = 30
    FaultDurationSeconds = 45
    FailoverMaster = "redis-cluster-1"
    NodeDownNode = "redis-cluster-4"
    ScaleInNode = "redis-cluster-6"
    RecoverAfterScenario = $true

    WarmupTotal = 500
    WarmupConcurrency = 20
    BaselineTotal = 5000
    BaselineConcurrency = 100
    MixedTotal = 20000
    MixedConcurrency = 200
    PipelineKeyCount = 3
    ExpirySeconds = 300
    ReadyTimeoutSeconds = 120
    ReadyProbeIntervalSeconds = 5
    ExtraCsredisCacheshellProbe = $true
    RestartAppsBeforeScenario = $false
}

for ($i = 0; $i -lt $args.Count; $i++) {
    $name = [string]$args[$i]
    switch ($name.ToLowerInvariant()) {
        "-scenario" { $i++; $opts.Scenario = [string]$args[$i] }
        "-suite" { $i++; $opts.Suite = [string]$args[$i] }
        "-composefile" { $i++; $opts.ComposeFile = [string]$args[$i] }
        "-outputdir" { $i++; $opts.OutputDir = [string]$args[$i] }
        "-runid" { $i++; $opts.RunId = [string]$args[$i] }
        "-dryrun" { $opts.DryRun = $true }

        "-csredistarget" { $i++; $opts.CsredisTarget = [string]$args[$i] }
        "-stackexchangetarget" { $i++; $opts.StackexchangeTarget = [string]$args[$i] }

        "-injectiondelayseconds" { $i++; $opts.InjectionDelaySeconds = [int]$args[$i] }
        "-faultdurationseconds" { $i++; $opts.FaultDurationSeconds = [int]$args[$i] }
        "-failovermaster" { $i++; $opts.FailoverMaster = [string]$args[$i] }
        "-nodedownnode" { $i++; $opts.NodeDownNode = [string]$args[$i] }
        "-scaleinnode" { $i++; $opts.ScaleInNode = [string]$args[$i] }
        "-norecoverafterscenario" { $opts.RecoverAfterScenario = $false }

        "-warmuptotal" { $i++; $opts.WarmupTotal = [int]$args[$i] }
        "-warmupconcurrency" { $i++; $opts.WarmupConcurrency = [int]$args[$i] }
        "-baselinetotal" { $i++; $opts.BaselineTotal = [int]$args[$i] }
        "-baselineconcurrency" { $i++; $opts.BaselineConcurrency = [int]$args[$i] }
        "-mixedtotal" { $i++; $opts.MixedTotal = [int]$args[$i] }
        "-mixedconcurrency" { $i++; $opts.MixedConcurrency = [int]$args[$i] }
        "-pipelinekeycount" { $i++; $opts.PipelineKeyCount = [int]$args[$i] }
        "-expiryseconds" { $i++; $opts.ExpirySeconds = [int]$args[$i] }
        "-readytimeoutseconds" { $i++; $opts.ReadyTimeoutSeconds = [int]$args[$i] }
        "-readyprobeintervalseconds" { $i++; $opts.ReadyProbeIntervalSeconds = [int]$args[$i] }
        "-noextracsrediscacheshellprobe" { $opts.ExtraCsredisCacheshellProbe = $false }
        "-restartappsbeforescenario" { $opts.RestartAppsBeforeScenario = $true }
        default { throw "Unknown argument: $name" }
    }
}

$validScenarios = @("all", "failover", "node-down", "scale-in")
$validSuites = @("both", "csredis", "stackexchange")
if ($validScenarios -notcontains $opts.Scenario.ToLowerInvariant()) {
    throw "Scenario must be all|failover|node-down|scale-in"
}
if ($validSuites -notcontains $opts.Suite.ToLowerInvariant()) {
    throw "Suite must be both|csredis|stackexchange"
}

function Ensure-Dir([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -Path $path -ItemType Directory | Out-Null
    }
}

function Get-RunId([string]$candidate) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        return (Get-Date -Format "yyyyMMdd-HHmmss")
    }
    return $candidate
}

function Add-Event([System.Collections.Generic.List[object]]$eventList, [string]$scenario, [string]$eventName, [string]$detail) {
    $eventList.Add([pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString("o")
        Scenario = $scenario
        Event = $eventName
        Detail = $detail
    })
}

function Invoke-DockerCompose([string[]]$argsLocal, [bool]$dryRun) {
    $cmdText = "docker compose -f $($opts.ComposeFile) " + ($argsLocal -join " ")
    Write-Host "[Chaos] $cmdText"
    if ($dryRun) { return }

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

function Start-StressProcess([string]$suite, [string]$scenario, [string]$scenarioRoot, [string]$runId, [hashtable]$optsLocal) {
    $suiteOutDir = Join-Path $scenarioRoot $suite
    Ensure-Dir $suiteOutDir
    $scriptPath = Join-Path $PSScriptRoot "run-grpc-phased-stress.ps1"
    $target = if ($suite -eq "csredis") { $optsLocal.CsredisTarget } else { $optsLocal.StackexchangeTarget }
    $argsLocal = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $scriptPath,
        "-Suite", $suite,
        "-Target", $target,
        "-OutputDir", $suiteOutDir,
        "-RunId", $runId,
        "-WarmupTotal", $optsLocal.WarmupTotal,
        "-WarmupConcurrency", $optsLocal.WarmupConcurrency,
        "-BaselineTotal", $optsLocal.BaselineTotal,
        "-BaselineConcurrency", $optsLocal.BaselineConcurrency,
        "-MixedTotal", $optsLocal.MixedTotal,
        "-MixedConcurrency", $optsLocal.MixedConcurrency,
        "-PipelineKeyCount", $optsLocal.PipelineKeyCount,
        "-ExpirySeconds", $optsLocal.ExpirySeconds
    )
    if ($optsLocal.DryRun) { $argsLocal += "-DryRun" }

    $stdOut = Join-Path $suiteOutDir "$suite.console.log"
    $stdErr = Join-Path $suiteOutDir "$suite.console.err.log"
    Write-Host "[Chaos] Start stress suite=$suite scenario=$scenario"
    Write-Host "[Chaos] powershell $($argsLocal -join ' ')"
    if ($optsLocal.DryRun) {
        $dryRunSummaryPath = Join-Path (Join-Path $suiteOutDir ("run-" + $runId)) "summary.csv"
        Ensure-Dir (Split-Path -Parent $dryRunSummaryPath)
        @(
            [pscustomobject]@{
                Suite = $suite
                Phase = "dryrun"
                Method = "all"
                Total = ""
                Concurrency = ""
                Log = $stdOut
                Qps = ""
                P95 = ""
                P99 = ""
                Success = ""
                Failed = ""
                Status = "DRYRUN"
            }
        ) | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $dryRunSummaryPath

        return [pscustomobject]@{
            Suite = $suite
            Stdout = $stdOut
            Summary = $dryRunSummaryPath
            Process = $null
        }
    }

    $process = Start-Process -FilePath "powershell" -ArgumentList $argsLocal -PassThru -NoNewWindow -RedirectStandardOutput $stdOut -RedirectStandardError $stdErr
    return [pscustomobject]@{
        Suite = $suite
        Stdout = $stdOut
        Summary = Join-Path (Join-Path $suiteOutDir ("run-" + $runId)) "summary.csv"
        Process = $process
    }
}

function Wait-TargetReady([string]$suite, [string]$target, [hashtable]$optsLocal) {
    if ($optsLocal.DryRun) {
        return
    }

    $probeProject = Join-Path $PSScriptRoot "CsredisGrpcStressTests"
    $deadline = [DateTime]::UtcNow.AddSeconds($optsLocal.ReadyTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $probeCmd = @(
            "run", "--project", $probeProject, "--",
            "--suite", $suite,
            "--target", $target,
            "--method", "get",
            "--total", "1",
            "--concurrency", "1",
            "--key-prefix", "grpc:ready:$suite"
        )

        $prev = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $probeOutput = & dotnet @probeCmd 2>&1
            $okLine = $probeOutput | Where-Object { $_ -match "^\[GrpcStress\] Overall Success=" } | Select-Object -Last 1
            if ($okLine -and $okLine -match "Success=(\d+), Failed=(\d+)") {
                $ok = [int]$Matches[1] -gt 0 -and [int]$Matches[2] -eq 0
                if ($ok) {
                    Write-Host "[Chaos] target ready: suite=$suite target=$target"
                    return
                }
            }
        }
        finally {
            $ErrorActionPreference = $prev
        }

        Start-Sleep -Seconds $optsLocal.ReadyProbeIntervalSeconds
    }

    throw "target not ready within timeout: suite=$suite target=$target"
}

function Invoke-ScenarioFault([string]$scenario, [hashtable]$optsLocal) {
    switch ($scenario) {
        "failover" {
            Invoke-DockerCompose @("stop", $optsLocal.FailoverMaster) $optsLocal.DryRun
            if (-not $optsLocal.DryRun) { Start-Sleep -Seconds $optsLocal.FaultDurationSeconds }
            if ($optsLocal.RecoverAfterScenario) {
                Invoke-DockerCompose @("up", "-d", $optsLocal.FailoverMaster) $optsLocal.DryRun
            }
        }
        "node-down" {
            Invoke-DockerCompose @("stop", $optsLocal.NodeDownNode) $optsLocal.DryRun
            if (-not $optsLocal.DryRun) { Start-Sleep -Seconds $optsLocal.FaultDurationSeconds }
            if ($optsLocal.RecoverAfterScenario) {
                Invoke-DockerCompose @("up", "-d", $optsLocal.NodeDownNode) $optsLocal.DryRun
            }
        }
        "scale-in" {
            # Simulate dynamic scale-in by taking one replica out for the whole scenario window.
            Invoke-DockerCompose @("stop", $optsLocal.ScaleInNode) $optsLocal.DryRun
            if (-not $optsLocal.DryRun) { Start-Sleep -Seconds $optsLocal.FaultDurationSeconds }
            if ($optsLocal.RecoverAfterScenario) {
                Invoke-DockerCompose @("up", "-d", $optsLocal.ScaleInNode) $optsLocal.DryRun
            }
        }
        default { throw "unsupported scenario: $scenario" }
    }
}

function Restart-AppServices([hashtable]$optsLocal) {
    Invoke-DockerCompose @("restart", "csredis-tests-app", "app") $optsLocal.DryRun
}

function Get-FailureClassification([object[]]$suiteRows) {
    $warmup = $suiteRows | Where-Object { $_.Phase -eq "warmup" } | Select-Object -First 1
    $mixed = $suiteRows | Where-Object { $_.Phase -eq "mixed" } | Select-Object -First 1
    $baselineGet = $suiteRows | Where-Object { $_.Phase -eq "baseline-get" } | Select-Object -First 1
    $baselinePipeline = $suiteRows | Where-Object { $_.Phase -eq "baseline-pipeline" } | Select-Object -First 1
    $baselineSlot = $suiteRows | Where-Object { $_.Phase -eq "baseline-slot" } | Select-Object -First 1
    $baselineNode = $suiteRows | Where-Object { $_.Phase -eq "baseline-node" } | Select-Object -First 1
    $baselineCache = $suiteRows | Where-Object { $_.Phase -eq "baseline-cacheshell" } | Select-Object -First 1

    $allCoreZero = @($warmup, $baselineGet, $baselinePipeline, $baselineSlot, $baselineNode) |
        Where-Object { $null -ne $_ } |
        ForEach-Object { [int]$_.Success } |
        Where-Object { $_ -gt 0 } |
        Measure-Object
    if ($allCoreZero.Count -eq 0) {
        return [pscustomobject]@{
            FailureClass = "transport_connectivity"
            FailureReason = "warmup_and_core_baselines_all_zero_success"
        }
    }

    if ($baselineCache -and [int]$baselineCache.Success -eq 0 -and [int]$baselineCache.Failed -gt 0 -and
        $baselineGet -and [int]$baselineGet.Success -gt 0 -and
        $baselinePipeline -and [int]$baselinePipeline.Success -gt 0 -and
        $baselineSlot -and [int]$baselineSlot.Success -gt 0) {
        return [pscustomobject]@{
            FailureClass = "business_cacheshell"
            FailureReason = "baseline_cacheshell_failed_while_other_methods_passed"
        }
    }

    if ($mixed -and [double]$mixed.P95 -ge 2000 -and [int]$mixed.Failed -gt 0) {
        return [pscustomobject]@{
            FailureClass = "timeout_dominant"
            FailureReason = "mixed_p95_high_with_failures"
        }
    }

    return [pscustomobject]@{
        FailureClass = "mixed_or_unknown"
        FailureReason = "no_single_dominant_pattern"
    }
}

function Run-CsredisCacheshellProbe([string]$scenarioDir, [string]$runId, [hashtable]$optsLocal) {
    if ($optsLocal.DryRun -or -not $optsLocal.ExtraCsredisCacheshellProbe) {
        return ""
    }

    $probeDir = Join-Path $scenarioDir "csredis-cacheshell-probe"
    Ensure-Dir $probeDir
    $logPath = Join-Path $probeDir "probe.log"
    $cmd = @(
        "run", "--project", (Join-Path $PSScriptRoot "CsredisGrpcStressTests"), "--",
        "--suite", "csredis",
        "--target", $optsLocal.CsredisTarget,
        "--method", "cacheshell",
        "--total", [string]([Math]::Max([int]$optsLocal.BaselineTotal, 200)),
        "--concurrency", [string]([Math]::Max([int]$optsLocal.BaselineConcurrency, 20)),
        "--key-prefix", "grpc:probe:cacheshell:$runId",
        "--pipeline-key-count", [string]$optsLocal.PipelineKeyCount,
        "--expiry-seconds", [string]$optsLocal.ExpirySeconds
    )

    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & dotnet @cmd 2>&1
    }
    finally {
        $ErrorActionPreference = $prev
    }
    $output | Tee-Object -FilePath $logPath | Out-Host
    return $logPath
}

function Merge-ScenarioSummaries([string]$scenarioRoot, [object[]]$runs, [string]$outputPath) {
    $rows = @()
    foreach ($run in $runs) {
        if (Test-Path -LiteralPath $run.Summary) {
            $imported = Import-Csv -Path $run.Summary
            $classification = Get-FailureClassification -suiteRows $imported
            foreach ($row in $imported) {
                $rows += [pscustomobject]@{
                    Scenario = Split-Path -Leaf $scenarioRoot
                    Suite = $row.Suite
                    Phase = $row.Phase
                    Method = $row.Method
                    Total = $row.Total
                    Concurrency = $row.Concurrency
                    Qps = $row.Qps
                    P95 = $row.P95
                    P99 = $row.P99
                    Success = $row.Success
                    Failed = $row.Failed
                    Status = $row.Status
                    Log = $row.Log
                    FailureClass = $classification.FailureClass
                    FailureReason = $classification.FailureReason
                }
            }
        }
    }

    if ($rows.Count -gt 0) {
        $rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $outputPath
    }
}

$runId = Get-RunId $opts.RunId
Ensure-Dir $opts.OutputDir
$rootDir = Join-Path $opts.OutputDir ("run-" + $runId)
Ensure-Dir $rootDir
$eventRows = [System.Collections.Generic.List[object]]::new()

$scenarios = if ($opts.Scenario -eq "all") { @("failover", "node-down", "scale-in") } else { @($opts.Scenario) }
$suites = if ($opts.Suite -eq "both") { @("csredis", "stackexchange") } else { @($opts.Suite) }

Write-Host "RunId: $runId"
Write-Host "Scenarios: $($scenarios -join ', ')"
Write-Host "Suites: $($suites -join ', ')"
Write-Host "Output: $rootDir"
Write-Host "ComposeFile: $($opts.ComposeFile)"

foreach ($scenario in $scenarios) {
    $scenarioDir = Join-Path $rootDir $scenario
    Ensure-Dir $scenarioDir
    Write-Host ""
    Write-Host "===== Scenario: $scenario ====="
    Add-Event -eventList $eventRows -scenario $scenario -eventName "scenario_start" -detail "start scenario execution"

    if ($opts.RestartAppsBeforeScenario) {
        Add-Event -eventList $eventRows -scenario $scenario -eventName "app_restart_start" -detail "restart csredis-tests-app and app"
        Restart-AppServices -optsLocal $opts
        Add-Event -eventList $eventRows -scenario $scenario -eventName "app_restart_end" -detail "app services restarted"
    }

    foreach ($suite in $suites) {
        $target = if ($suite -eq "csredis") { $opts.CsredisTarget } else { $opts.StackexchangeTarget }
        Add-Event -eventList $eventRows -scenario $scenario -eventName "readiness_probe_start" -detail "suite=$suite target=$target"
        Wait-TargetReady -suite $suite -target $target -optsLocal $opts
        Add-Event -eventList $eventRows -scenario $scenario -eventName "readiness_probe_ok" -detail "suite=$suite target=$target"
    }

    $runs = @()
    foreach ($suite in $suites) {
        Add-Event -eventList $eventRows -scenario $scenario -eventName "suite_stress_start" -detail "suite=$suite"
        $runs += Start-StressProcess -suite $suite -scenario $scenario -scenarioRoot $scenarioDir -runId $runId -optsLocal $opts
    }

    Write-Host "[Chaos] waiting $($opts.InjectionDelaySeconds)s before fault injection..."
    if (-not $opts.DryRun) {
        Start-Sleep -Seconds $opts.InjectionDelaySeconds
    }

    Add-Event -eventList $eventRows -scenario $scenario -eventName "fault_injection_start" -detail "scenario=$scenario"
    Invoke-ScenarioFault -scenario $scenario -optsLocal $opts
    Add-Event -eventList $eventRows -scenario $scenario -eventName "fault_injection_end" -detail "scenario=$scenario"

    Add-Event -eventList $eventRows -scenario $scenario -eventName "csredis_cacheshell_probe_start" -detail "start extra probe"
    $probeLog = Run-CsredisCacheshellProbe -scenarioDir $scenarioDir -runId $runId -optsLocal $opts
    Add-Event -eventList $eventRows -scenario $scenario -eventName "csredis_cacheshell_probe_end" -detail "log=$probeLog"

    if (-not $opts.DryRun) {
        $pids = $runs | Where-Object { $_.Process -ne $null } | ForEach-Object { $_.Process.Id }
        if ($pids.Count -gt 0) {
            foreach ($procId in $pids) {
                $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
                if ($null -ne $proc) {
                    Wait-Process -Id $procId -ErrorAction SilentlyContinue
                }
            }
        }
    }

    foreach ($run in $runs) {
        if (-not (Test-Path -LiteralPath $run.Summary)) {
            throw "Missing summary for suite=$($run.Suite), scenario=$scenario. Log: $($run.Stdout)"
        }
        Add-Event -eventList $eventRows -scenario $scenario -eventName "suite_stress_end" -detail "suite=$($run.Suite)"
    }

    $scenarioSummary = Join-Path $scenarioDir "summary.compare.csv"
    Merge-ScenarioSummaries -scenarioRoot $scenarioDir -runs $runs -outputPath $scenarioSummary
    Write-Host "[Chaos] scenario summary: $scenarioSummary"
    Add-Event -eventList $eventRows -scenario $scenario -eventName "scenario_end" -detail "scenario summary generated"
}

$allRows = @()
foreach ($scenario in $scenarios) {
    $scenarioSummary = Join-Path (Join-Path $rootDir $scenario) "summary.compare.csv"
    if (Test-Path -LiteralPath $scenarioSummary) {
        $allRows += Import-Csv -Path $scenarioSummary
    }
}
$allSummary = Join-Path $rootDir "summary.all.csv"
if ($allRows.Count -gt 0) {
    $allRows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $allSummary
    Write-Host ""
    Write-Host "[Chaos] all scenario summary: $allSummary"
}

$eventPath = Join-Path $rootDir "events.timeline.csv"
$eventRows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $eventPath
Write-Host "[Chaos] event timeline: $eventPath"
