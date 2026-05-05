$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
# Prevent native-command stderr lines from being promoted to terminating errors.
$PSNativeCommandUseErrorActionPreference = $false

$opts = @{
    Suite = "csredis"
    Target = "http://127.0.0.1:50051"
    CsredisTarget = "http://127.0.0.1:50051"
    StackexchangeTarget = "http://127.0.0.1:50052"
    ProjectPath = "CsredisGrpcStressTests"
    OutputDir = "artifacts/grpc-stress"
    RunId = ""
    WarmupTotal = 500
    WarmupConcurrency = 20
    BaselineTotal = 5000
    BaselineConcurrency = 100
    MixedTotal = 20000
    MixedConcurrency = 200
    PipelineKeyCount = 3
    ExpirySeconds = 300
    MultiHashtag = $false
    Https = $false
    DryRun = $false
}

for ($i = 0; $i -lt $args.Count; $i++) {
    $name = [string]$args[$i]
    switch ($name.ToLowerInvariant()) {
        "-suite" { $i++; $opts.Suite = [string]$args[$i] }
        "-target" { $i++; $opts.Target = [string]$args[$i] }
        "-csredistarget" { $i++; $opts.CsredisTarget = [string]$args[$i] }
        "-stackexchangetarget" { $i++; $opts.StackexchangeTarget = [string]$args[$i] }
        "-projectpath" { $i++; $opts.ProjectPath = [string]$args[$i] }
        "-outputdir" { $i++; $opts.OutputDir = [string]$args[$i] }
        "-runid" { $i++; $opts.RunId = [string]$args[$i] }
        "-warmuptotal" { $i++; $opts.WarmupTotal = [int]$args[$i] }
        "-warmupconcurrency" { $i++; $opts.WarmupConcurrency = [int]$args[$i] }
        "-baselinetotal" { $i++; $opts.BaselineTotal = [int]$args[$i] }
        "-baselineconcurrency" { $i++; $opts.BaselineConcurrency = [int]$args[$i] }
        "-mixedtotal" { $i++; $opts.MixedTotal = [int]$args[$i] }
        "-mixedconcurrency" { $i++; $opts.MixedConcurrency = [int]$args[$i] }
        "-pipelinekeycount" { $i++; $opts.PipelineKeyCount = [int]$args[$i] }
        "-expiryseconds" { $i++; $opts.ExpirySeconds = [int]$args[$i] }
        "-multihashtag" { $opts.MultiHashtag = $true }
        "-https" { $opts.Https = $true }
        "-dryrun" { $opts.DryRun = $true }
        default { throw "Unknown argument: $name" }
    }
}

$validSuites = @("csredis", "stackexchange", "both")
if ($validSuites -notcontains $opts.Suite.ToLowerInvariant()) {
    throw "Suite must be csredis|stackexchange|both"
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

function Invoke-Phase([string]$suite, [string]$target, [hashtable]$phase, [string]$runId, [string]$runDir, [hashtable]$optsLocal) {
    $phaseName = [string]$phase.Name
    $method = [string]$phase.Method
    $total = [int]$phase.Total
    $concurrency = [int]$phase.Concurrency
    $keyPrefix = "grpc:phased:${suite}:${phaseName}:${runId}"
    $logPath = Join-Path $runDir ($phaseName + ".log")

    $cmd = @(
        "run", "--project", $optsLocal.ProjectPath, "--",
        "--suite", $suite,
        "--target", $target,
        "--method", $method,
        "--total", $total,
        "--concurrency", $concurrency,
        "--key-prefix", $keyPrefix,
        "--pipeline-key-count", $optsLocal.PipelineKeyCount,
        "--expiry-seconds", $optsLocal.ExpirySeconds
    )
    if ($optsLocal.Https) { $cmd += "--https" }
    if ($optsLocal.MultiHashtag) { $cmd += "--multi-hashtag" }

    Write-Host ""
    Write-Host "=== Phase: $phaseName ($suite) ==="
    Write-Host ("dotnet " + ($cmd -join " "))
    Write-Host "Log: $logPath"

    if ($optsLocal.DryRun) {
        return [pscustomobject]@{
            Suite = $suite
            Phase = $phaseName
            Method = $method
            Total = $total
            Concurrency = $concurrency
            Log = $logPath
            Qps = ""
            P95 = ""
            P99 = ""
            Success = ""
            Failed = ""
            Status = "DRYRUN"
        }
    }

    $output = & dotnet @cmd 2>&1
    $output | Tee-Object -FilePath $logPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Phase '$phaseName' failed. ExitCode=$LASTEXITCODE"
    }

    $overall = ($output | Where-Object { $_ -match "^\[GrpcStress\] Overall Success=" } | Select-Object -Last 1)
    $lat = ($output | Where-Object { $_ -match "^\[GrpcStress\] Overall Latency\(ms\):" } | Select-Object -Last 1)
    $qps = ""
    $success = ""
    $failed = ""
    $p95 = ""
    $p99 = ""
    if ($overall -match "Success=(\d+), Failed=(\d+), .*QPS=([0-9\.]+)") {
        $success = $Matches[1]
        $failed = $Matches[2]
        $qps = $Matches[3]
    }
    if ($lat -match "p95=([0-9\.]+), p99=([0-9\.]+)") {
        $p95 = $Matches[1]
        $p99 = $Matches[2]
    }

    return [pscustomobject]@{
        Suite = $suite
        Phase = $phaseName
        Method = $method
        Total = $total
        Concurrency = $concurrency
        Log = $logPath
        Qps = $qps
        P95 = $p95
        P99 = $p99
        Success = $success
        Failed = $failed
        Status = "OK"
    }
}

function Run-OneSuite([string]$suite, [string]$target, [string]$runId, [string]$baseDir, [hashtable]$optsLocal) {
    Ensure-Dir $baseDir
    $runDir = Join-Path $baseDir ("run-" + $runId)
    Ensure-Dir $runDir

    $phases = @(
        @{ Name = "warmup"; Method = "all"; Total = [int]$optsLocal.WarmupTotal; Concurrency = [int]$optsLocal.WarmupConcurrency },
        @{ Name = "baseline-get"; Method = "get"; Total = [int]$optsLocal.BaselineTotal; Concurrency = [int]$optsLocal.BaselineConcurrency },
        @{ Name = "baseline-pipeline"; Method = "pipeline"; Total = [int]$optsLocal.BaselineTotal; Concurrency = [int]$optsLocal.BaselineConcurrency },
        @{ Name = "baseline-cacheshell"; Method = "cacheshell"; Total = [int]$optsLocal.BaselineTotal; Concurrency = [int]$optsLocal.BaselineConcurrency },
        @{ Name = "baseline-slot"; Method = "slot"; Total = [int]$optsLocal.BaselineTotal; Concurrency = [int]$optsLocal.BaselineConcurrency },
        @{ Name = "baseline-node"; Method = "node"; Total = [Math]::Max([Math]::Min([int]$optsLocal.BaselineTotal / 10, 2000), 100); Concurrency = [Math]::Max([Math]::Min([int]$optsLocal.BaselineConcurrency / 4, 64), 4) },
        @{ Name = "mixed"; Method = "all"; Total = [int]$optsLocal.MixedTotal; Concurrency = [int]$optsLocal.MixedConcurrency }
    )

    Write-Host "RunId: $runId"
    Write-Host "Suite: $suite"
    Write-Host "Target: $target"
    Write-Host "Output: $runDir"

    $results = @()
    foreach ($phase in $phases) {
        $results += Invoke-Phase -suite $suite -target $target -phase $phase -runId $runId -runDir $runDir -optsLocal $optsLocal
    }

    $summary = Join-Path $runDir "summary.csv"
    $results | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $summary
    Write-Host ""
    Write-Host "=== Phased Stress Summary ($suite) ==="
    $results | Format-Table -AutoSize
    Write-Host "Summary CSV: $summary"
    return $summary
}

$runId = Get-RunId $opts.RunId
Ensure-Dir $opts.OutputDir

if ($opts.Suite -ne "both") {
    Run-OneSuite -suite $opts.Suite -target $opts.Target -runId $runId -baseDir $opts.OutputDir -optsLocal $opts | Out-Null
    exit 0
}

$rootDir = Join-Path $opts.OutputDir ("run-" + $runId)
Ensure-Dir $rootDir
$csBase = Join-Path $rootDir "csredis"
$seBase = Join-Path $rootDir "stackexchange"
Ensure-Dir $csBase
Ensure-Dir $seBase

$self = Join-Path $PSScriptRoot "run-grpc-phased-stress.ps1"
$common = @(
    "-ProjectPath", $opts.ProjectPath,
    "-WarmupTotal", $opts.WarmupTotal,
    "-WarmupConcurrency", $opts.WarmupConcurrency,
    "-BaselineTotal", $opts.BaselineTotal,
    "-BaselineConcurrency", $opts.BaselineConcurrency,
    "-MixedTotal", $opts.MixedTotal,
    "-MixedConcurrency", $opts.MixedConcurrency,
    "-PipelineKeyCount", $opts.PipelineKeyCount,
    "-ExpirySeconds", $opts.ExpirySeconds,
    "-RunId", $runId
)
if ($opts.Https) { $common += "-Https" }
if ($opts.MultiHashtag) { $common += "-MultiHashtag" }
if ($opts.DryRun) { $common += "-DryRun" }

$csArgs = @("-ExecutionPolicy", "Bypass", "-File", $self) + $common + @("-Suite", "csredis", "-Target", $opts.CsredisTarget, "-OutputDir", $csBase)
$seArgs = @("-ExecutionPolicy", "Bypass", "-File", $self) + $common + @("-Suite", "stackexchange", "-Target", $opts.StackexchangeTarget, "-OutputDir", $seBase)

Write-Host "RunId: $runId"
Write-Host "Suite: both (parallel)"
Write-Host "CsredisTarget: $($opts.CsredisTarget)"
Write-Host "StackexchangeTarget: $($opts.StackexchangeTarget)"
Write-Host "Output: $rootDir"

$csOut = Join-Path $rootDir "csredis.console.log"
$csErr = Join-Path $rootDir "csredis.console.err.log"
$seOut = Join-Path $rootDir "stackexchange.console.log"
$seErr = Join-Path $rootDir "stackexchange.console.err.log"

$p1 = Start-Process -FilePath "powershell" -ArgumentList $csArgs -PassThru -NoNewWindow -RedirectStandardOutput $csOut -RedirectStandardError $csErr
$p2 = Start-Process -FilePath "powershell" -ArgumentList $seArgs -PassThru -NoNewWindow -RedirectStandardOutput $seOut -RedirectStandardError $seErr
Wait-Process -Id $p1.Id, $p2.Id
$csSummary = Join-Path $csBase ("run-" + $runId + "\summary.csv")
$seSummary = Join-Path $seBase ("run-" + $runId + "\summary.csv")
$bothSummary = Join-Path $rootDir "summary.both.csv"
$csOk = Test-Path -LiteralPath $csSummary
$seOk = Test-Path -LiteralPath $seSummary
if (-not $csOk -or -not $seOk) {
    throw "Parallel run failed. Missing summary file(s). csredisSummary=$csOk, stackexchangeSummary=$seOk"
}
$rows = @()
if ($csOk) {
    $rows += Import-Csv -Path $csSummary
}
if ($seOk) {
    $rows += Import-Csv -Path $seSummary
}
if ($rows.Count -gt 0) {
    $rows | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $bothSummary
}

Write-Host ""
Write-Host "Both suites completed."
Write-Host "csredis log: $csOut"
Write-Host "stackexchange log: $seOut"
if (Test-Path -LiteralPath $bothSummary) {
    Write-Host "Combined summary CSV: $bothSummary"
}
