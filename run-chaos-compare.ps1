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
    NodeDownMaster = "redis-cluster-1"
    NodeDownReplica = "redis-cluster-4"
    ScaleInNode = "redis-cluster-6"
    ScaleInMaster = "redis-cluster-3"
    ScaleInReplicas = "redis-cluster-6"
    ScaleInReshardTargetMaster = "redis-cluster-1"
    ScaleInCoordinator = "redis-cluster-1"
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
        "-nodedownmaster" { $i++; $opts.NodeDownMaster = [string]$args[$i] }
        "-nodedownreplica" { $i++; $opts.NodeDownReplica = [string]$args[$i] }
        "-scaleinnode" { $i++; $opts.ScaleInNode = [string]$args[$i] }
        "-scaleinmaster" { $i++; $opts.ScaleInMaster = [string]$args[$i] }
        "-scaleinreplicas" { $i++; $opts.ScaleInReplicas = [string]$args[$i] }
        "-scaleinreshardtargetmaster" { $i++; $opts.ScaleInReshardTargetMaster = [string]$args[$i] }
        "-scaleincoordinator" { $i++; $opts.ScaleInCoordinator = [string]$args[$i] }
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

function Invoke-DockerCommand([string[]]$argsLocal, [bool]$dryRun) {
    $cmdText = "docker " + ($argsLocal -join " ")
    Write-Host "[Chaos] $cmdText"
    if ($dryRun) {
        return @()
    }

    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & docker @argsLocal 2>&1
    }
    finally {
        $ErrorActionPreference = $prev
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $cmdText`n$($output | Out-String)"
    }
    return @($output)
}

function Test-ContainerRunning([string]$containerName, [bool]$dryRun) {
    if ($dryRun) {
        return $null
    }

    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $inspect = & docker inspect -f "{{.State.Running}}" $containerName 2>&1
    }
    finally {
        $ErrorActionPreference = $prev
    }
    if ($LASTEXITCODE -ne 0) {
        throw "docker inspect failed for container '$containerName': $($inspect | Out-String)"
    }

    $value = (($inspect | Select-Object -First 1) -as [string]).Trim().ToLowerInvariant()
    return ($value -eq "true")
}

function Parse-ContainerList([string]$csv) {
    if ([string]::IsNullOrWhiteSpace($csv)) {
        return @()
    }
    return @(
        $csv.Split(",") |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
}

function Get-NodeSlotCountFromClusterNodes([string[]]$clusterNodes, [string]$nodeId) {
    $line = $clusterNodes | Where-Object { $_ -like "$nodeId *" } | Select-Object -First 1
    if ($null -eq $line) {
        throw "node id not found in CLUSTER NODES output: $nodeId"
    }

    $tokens = @($line -split "\s+")
    $slotCount = 0
    for ($i = 8; $i -lt $tokens.Count; $i++) {
        $token = $tokens[$i]
        if ($token -match "^\d+$") {
            $slotCount += [int]$token
            continue
        }
        if ($token -match "^(\d+)-(\d+)$") {
            $slotCount += (([int]$Matches[2]) - ([int]$Matches[1]) + 1)
        }
    }
    return $slotCount
}

function Prepare-ScaleInPersistentDown([hashtable]$optsLocal) {
    $sourceMaster = [string]$optsLocal.ScaleInMaster
    $replicas = Parse-ContainerList -csv ([string]$optsLocal.ScaleInReplicas)
    $targetMaster = [string]$optsLocal.ScaleInReshardTargetMaster
    $coordinator = [string]$optsLocal.ScaleInCoordinator
    $downContainers = @($sourceMaster) + $replicas | Sort-Object -Unique

    if ([string]::IsNullOrWhiteSpace($sourceMaster) -or [string]::IsNullOrWhiteSpace($targetMaster) -or [string]::IsNullOrWhiteSpace($coordinator)) {
        throw "scale-in requires ScaleInMaster / ScaleInReshardTargetMaster / ScaleInCoordinator"
    }

    if ($optsLocal.DryRun) {
        return [pscustomobject]@{
            SourceMaster = $sourceMaster
            SourceNodeId = ""
            TargetMaster = $targetMaster
            TargetNodeId = ""
            SlotCountMoved = ""
            DownContainers = $downContainers
        }
    }

    $sourceNodeId = ((Invoke-DockerCommand -argsLocal @("exec", $sourceMaster, "redis-cli", "CLUSTER", "MYID") -dryRun $optsLocal.DryRun | Select-Object -First 1) -as [string]).Trim()
    $targetNodeId = ((Invoke-DockerCommand -argsLocal @("exec", $targetMaster, "redis-cli", "CLUSTER", "MYID") -dryRun $optsLocal.DryRun | Select-Object -First 1) -as [string]).Trim()
    if ($sourceNodeId -eq $targetNodeId) {
        throw "scale-in reshard target must differ from source master"
    }

    $clusterNodesBefore = Invoke-DockerCommand -argsLocal @("exec", $coordinator, "redis-cli", "CLUSTER", "NODES") -dryRun $optsLocal.DryRun
    $sourceSlotCount = Get-NodeSlotCountFromClusterNodes -clusterNodes $clusterNodesBefore -nodeId $sourceNodeId

    if ($sourceSlotCount -gt 0) {
        Invoke-DockerCommand -argsLocal @(
            "exec", $coordinator, "redis-cli",
            "--cluster", "reshard", ($coordinator + ":6379"),
            "--cluster-from", $sourceNodeId,
            "--cluster-to", $targetNodeId,
            "--cluster-slots", [string]$sourceSlotCount,
            "--cluster-yes"
        ) -dryRun $optsLocal.DryRun | Out-Null
    }

    $clusterNodesAfter = Invoke-DockerCommand -argsLocal @("exec", $coordinator, "redis-cli", "CLUSTER", "NODES") -dryRun $optsLocal.DryRun
    $sourceSlotCountAfter = Get-NodeSlotCountFromClusterNodes -clusterNodes $clusterNodesAfter -nodeId $sourceNodeId
    if ($sourceSlotCountAfter -ne 0) {
        throw "scale-in precondition failed: source master still owns slots after reshard (remaining=$sourceSlotCountAfter)"
    }

    return [pscustomobject]@{
        SourceMaster = $sourceMaster
        SourceNodeId = $sourceNodeId
        TargetMaster = $targetMaster
        TargetNodeId = $targetNodeId
        SlotCountMoved = $sourceSlotCount
        DownContainers = $downContainers
    }
}

function Invoke-ScenarioFault([string]$scenario, [hashtable]$optsLocal) {
    switch ($scenario) {
        "failover" {
            throw "failover persistent-down mode is handled in main scenario flow"
        }
        "node-down" {
            throw "node-down persistent-pair-down mode is handled in main scenario flow"
        }
        "scale-in" {
            throw "scale-in persistent slot-reassign mode is handled in main scenario flow"
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

function Export-PhaseServiceLogs([string]$scenarioRoot, [object[]]$runs, [hashtable]$optsLocal) {
    foreach ($run in $runs) {
        if (-not (Test-Path -LiteralPath $run.Summary)) {
            continue
        }

        $summaryRows = @(Import-Csv -LiteralPath $run.Summary)
        if ($summaryRows.Count -eq 0) {
            continue
        }

        $containerName = if ($run.Suite -eq "csredis") { "csredis-tests-app" } else { "stackexchange-tests-app" }
        $suiteLogDirName = if ($run.Suite -eq "csredis") { "csredistest" } else { "stackexchangetest" }
        $suiteLogDir = Join-Path $scenarioRoot $suiteLogDirName
        Ensure-Dir $suiteLogDir

        foreach ($row in $summaryRows) {
            $phase = [string]$row.Phase
            if ([string]::IsNullOrWhiteSpace($phase)) {
                continue
            }

            $phaseLogPath = Join-Path $suiteLogDir ($phase + ".log")
            $phaseStart = ""
            $phaseEnd = ""
            if ($row.PSObject.Properties.Name -contains "PhaseStartUtc") {
                $phaseStart = [string]$row.PhaseStartUtc
            }
            if ($row.PSObject.Properties.Name -contains "PhaseEndUtc") {
                $phaseEnd = [string]$row.PhaseEndUtc
            }
            $hasWindow = (-not [string]::IsNullOrWhiteSpace($phaseStart)) -and (-not [string]::IsNullOrWhiteSpace($phaseEnd))
            if (-not $hasWindow) {
                continue
            }

            if ($optsLocal.DryRun) {
                "DRYRUN: docker logs --timestamps --since $phaseStart --until $phaseEnd $containerName" | Set-Content -LiteralPath $phaseLogPath -Encoding UTF8
                continue
            }

            Write-Host "[Chaos] export service log suite=$($run.Suite) phase=$phase"
            $prev = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                $logOutput = & docker logs --timestamps --since $phaseStart --until $phaseEnd $containerName 2>&1
            }
            finally {
                $ErrorActionPreference = $prev
            }

            if ($LASTEXITCODE -ne 0) {
                @(
                    "docker logs failed for $containerName phase=$phase",
                    ($logOutput | Out-String).Trim()
                ) | Set-Content -LiteralPath $phaseLogPath -Encoding UTF8
                continue
            }

            $logOutput | Set-Content -LiteralPath $phaseLogPath -Encoding UTF8
        }
    }
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
    $persistentDownContainers = @()
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

    if ($scenario -eq "failover") {
        Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_master_down_start" -detail "stop failover master before stress: $($opts.FailoverMaster)"
        Invoke-DockerCompose @("stop", $opts.FailoverMaster) $opts.DryRun
        if (-not $opts.DryRun) {
            $isRunning = Test-ContainerRunning -containerName $opts.FailoverMaster -dryRun $opts.DryRun
            if ($isRunning) {
                throw "failover master should be down before stress, but is still running: $($opts.FailoverMaster)"
            }
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_master_down_verified_start" -detail "container_running=$isRunning"
        }
    }
    elseif ($scenario -eq "node-down") {
        Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_pair_down_start" -detail "stop node-down pair before stress: master=$($opts.NodeDownMaster), replica=$($opts.NodeDownReplica)"
        Invoke-DockerCompose @("stop", $opts.NodeDownMaster, $opts.NodeDownReplica) $opts.DryRun
        if (-not $opts.DryRun) {
            $masterRunning = Test-ContainerRunning -containerName $opts.NodeDownMaster -dryRun $opts.DryRun
            $replicaRunning = Test-ContainerRunning -containerName $opts.NodeDownReplica -dryRun $opts.DryRun
            if ($masterRunning -or $replicaRunning) {
                throw "node-down pair should both be down before stress, but running states are master=$masterRunning replica=$replicaRunning"
            }
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_pair_down_verified_start" -detail "master_running=$masterRunning, replica_running=$replicaRunning"
        }
    }
    elseif ($scenario -eq "scale-in") {
        Add-Event -eventList $eventRows -scenario $scenario -eventName "scalein_reshard_start" -detail "source_master=$($opts.ScaleInMaster), target_master=$($opts.ScaleInReshardTargetMaster), coordinator=$($opts.ScaleInCoordinator)"
        $scaleInPrep = Prepare-ScaleInPersistentDown -optsLocal $opts
        Add-Event -eventList $eventRows -scenario $scenario -eventName "scalein_reshard_verified" -detail "source_node_id=$($scaleInPrep.SourceNodeId), target_node_id=$($scaleInPrep.TargetNodeId), moved_slots=$($scaleInPrep.SlotCountMoved)"
        $persistentDownContainers = @($scaleInPrep.DownContainers)
        Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_scalein_down_start" -detail ("stop containers: " + ($persistentDownContainers -join ","))
        Invoke-DockerCompose (@("stop") + $persistentDownContainers) $opts.DryRun
        if (-not $opts.DryRun) {
            foreach ($container in $persistentDownContainers) {
                $isRunning = Test-ContainerRunning -containerName $container -dryRun $opts.DryRun
                if ($isRunning) {
                    throw "scale-in containers should be down before stress, but container is still running: $container"
                }
            }
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_scalein_down_verified_start" -detail ("containers_down=" + ($persistentDownContainers -join ","))
        }
    }

    $runs = @()
    foreach ($suite in $suites) {
        Add-Event -eventList $eventRows -scenario $scenario -eventName "suite_stress_start" -detail "suite=$suite"
        $runs += Start-StressProcess -suite $suite -scenario $scenario -scenarioRoot $scenarioDir -runId $runId -optsLocal $opts
    }

    if (($scenario -ne "failover") -and ($scenario -ne "node-down") -and ($scenario -ne "scale-in")) {
        Write-Host "[Chaos] waiting $($opts.InjectionDelaySeconds)s before fault injection..."
        if (-not $opts.DryRun) {
            Start-Sleep -Seconds $opts.InjectionDelaySeconds
        }

        Add-Event -eventList $eventRows -scenario $scenario -eventName "fault_injection_start" -detail "scenario=$scenario"
        Invoke-ScenarioFault -scenario $scenario -optsLocal $opts
        Add-Event -eventList $eventRows -scenario $scenario -eventName "fault_injection_end" -detail "scenario=$scenario"
    }

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

    if ($scenario -eq "failover") {
        if (-not $opts.DryRun) {
            $isRunningBeforeRecover = Test-ContainerRunning -containerName $opts.FailoverMaster -dryRun $opts.DryRun
            if ($isRunningBeforeRecover) {
                throw "failover master came back during stress; expected down until suite end: $($opts.FailoverMaster)"
            }
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_master_down_verified_end" -detail "container_running=$isRunningBeforeRecover"
        }

        if ($opts.RecoverAfterScenario) {
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_master_recover_start" -detail "recover failover master: $($opts.FailoverMaster)"
            Invoke-DockerCompose @("up", "-d", $opts.FailoverMaster) $opts.DryRun
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_master_recover_end" -detail "recover completed: $($opts.FailoverMaster)"
        }
    }
    elseif ($scenario -eq "node-down") {
        if (-not $opts.DryRun) {
            $masterRunningBeforeRecover = Test-ContainerRunning -containerName $opts.NodeDownMaster -dryRun $opts.DryRun
            $replicaRunningBeforeRecover = Test-ContainerRunning -containerName $opts.NodeDownReplica -dryRun $opts.DryRun
            if ($masterRunningBeforeRecover -or $replicaRunningBeforeRecover) {
                throw "node-down pair came back during stress; expected both down until suite end. master=$masterRunningBeforeRecover replica=$replicaRunningBeforeRecover"
            }
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_pair_down_verified_end" -detail "master_running=$masterRunningBeforeRecover, replica_running=$replicaRunningBeforeRecover"
        }

        if ($opts.RecoverAfterScenario) {
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_pair_recover_start" -detail "recover node-down pair: master=$($opts.NodeDownMaster), replica=$($opts.NodeDownReplica)"
            Invoke-DockerCompose @("up", "-d", $opts.NodeDownMaster, $opts.NodeDownReplica) $opts.DryRun
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_pair_recover_end" -detail "recover completed: master=$($opts.NodeDownMaster), replica=$($opts.NodeDownReplica)"
        }
    }
    elseif ($scenario -eq "scale-in") {
        if (-not $opts.DryRun) {
            foreach ($container in $persistentDownContainers) {
                $runningBeforeRecover = Test-ContainerRunning -containerName $container -dryRun $opts.DryRun
                if ($runningBeforeRecover) {
                    throw "scale-in containers came back during stress; expected down until suite end: $container"
                }
            }
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_scalein_down_verified_end" -detail ("containers_down=" + ($persistentDownContainers -join ","))
        }

        if ($opts.RecoverAfterScenario) {
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_scalein_recover_start" -detail ("recover containers: " + ($persistentDownContainers -join ","))
            Invoke-DockerCompose (@("up", "-d") + $persistentDownContainers) $opts.DryRun
            Add-Event -eventList $eventRows -scenario $scenario -eventName "persistent_scalein_recover_end" -detail ("recover completed: " + ($persistentDownContainers -join ","))
        }
    }

    Export-PhaseServiceLogs -scenarioRoot $scenarioDir -runs $runs -optsLocal $opts

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
