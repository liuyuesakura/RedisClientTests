$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
# Prevent native-command stderr lines from being promoted to terminating errors.
$PSNativeCommandUseErrorActionPreference = $false

$opts = @{
    Conn = "redis-cluster-1:6379,redis-cluster-2:6379,redis-cluster-3:6379,abortConnect=false,connectTimeout=5000,syncTimeout=5000"
    ProjectPath = "StackExchangeStressTests"
    OutputDir = "artifacts/stackexchange-stress"
    RunId = ""
    WarmupTotal = 500
    WarmupConcurrency = 20
    BaselineTotal = 5000
    BaselineConcurrency = 100
    MixedTotal = 20000
    MixedConcurrency = 200
    KeyPrefix = "se:phased"
    KeyCount = 2000
    PipelineKeyCount = 3
    ExpirySeconds = 300
    HashField = "field1"
    MultiHashtag = $false
    NoSeed = $false
    DryRun = $false
}

for ($i = 0; $i -lt $args.Count; $i++) {
    $name = [string]$args[$i]
    switch ($name.ToLowerInvariant()) {
        "-conn" { $i++; $opts.Conn = [string]$args[$i] }
        "-projectpath" { $i++; $opts.ProjectPath = [string]$args[$i] }
        "-outputdir" { $i++; $opts.OutputDir = [string]$args[$i] }
        "-runid" { $i++; $opts.RunId = [string]$args[$i] }
        "-warmuptotal" { $i++; $opts.WarmupTotal = [int]$args[$i] }
        "-warmupconcurrency" { $i++; $opts.WarmupConcurrency = [int]$args[$i] }
        "-baselinetotal" { $i++; $opts.BaselineTotal = [int]$args[$i] }
        "-baselineconcurrency" { $i++; $opts.BaselineConcurrency = [int]$args[$i] }
        "-mixedtotal" { $i++; $opts.MixedTotal = [int]$args[$i] }
        "-mixedconcurrency" { $i++; $opts.MixedConcurrency = [int]$args[$i] }
        "-keyprefix" { $i++; $opts.KeyPrefix = [string]$args[$i] }
        "-keycount" { $i++; $opts.KeyCount = [int]$args[$i] }
        "-pipelinekeycount" { $i++; $opts.PipelineKeyCount = [int]$args[$i] }
        "-expiryseconds" { $i++; $opts.ExpirySeconds = [int]$args[$i] }
        "-hashfield" { $i++; $opts.HashField = [string]$args[$i] }
        "-multihashtag" { $opts.MultiHashtag = $true }
        "-noseed" { $opts.NoSeed = $true }
        "-dryrun" { $opts.DryRun = $true }
        default { throw "Unknown argument: $name" }
    }
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

function Invoke-Phase([hashtable]$phase, [string]$runId, [string]$runDir, [hashtable]$optsLocal) {
    $phaseName = [string]$phase.Name
    $method = [string]$phase.Method
    $total = [int]$phase.Total
    $concurrency = [int]$phase.Concurrency
    $keyPrefix = "$($optsLocal.KeyPrefix):${phaseName}:$runId"
    $logPath = Join-Path $runDir ($phaseName + ".log")

    $cmd = @(
        "run", "--project", $optsLocal.ProjectPath, "--",
        "--conn", $optsLocal.Conn,
        "--method", $method,
        "--total", $total,
        "--concurrency", $concurrency,
        "--key-prefix", $keyPrefix,
        "--key-count", $optsLocal.KeyCount,
        "--pipeline-key-count", $optsLocal.PipelineKeyCount,
        "--expiry-seconds", $optsLocal.ExpirySeconds,
        "--hash-field", $optsLocal.HashField
    )
    if ($optsLocal.MultiHashtag) { $cmd += "--multi-hashtag" }
    $skipSeedForPhase = $optsLocal.NoSeed -or (($phase.ContainsKey("SkipSeed")) -and [bool]$phase.SkipSeed)
    if ($skipSeedForPhase) { $cmd += "--no-seed" }

    Write-Host ""
    Write-Host "=== Phase: $phaseName ==="
    Write-Host ("dotnet " + ($cmd -join " "))
    Write-Host "Log: $logPath"

    if ($optsLocal.DryRun) {
        return [pscustomobject]@{
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

    $prevErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & dotnet @cmd 2>&1
    }
    finally {
        $ErrorActionPreference = $prevErrorAction
    }
    $output | Tee-Object -FilePath $logPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Phase '$phaseName' failed. ExitCode=$LASTEXITCODE"
    }

    $overall = ($output | Where-Object { $_ -match "^\[SERedisStress\] Overall Success=" } | Select-Object -Last 1)
    $lat = ($output | Where-Object { $_ -match "^\[SERedisStress\] Overall Latency\(ms\):" } | Select-Object -Last 1)
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

$runId = Get-RunId $opts.RunId
Ensure-Dir $opts.OutputDir
$runDir = Join-Path $opts.OutputDir ("run-" + $runId)
Ensure-Dir $runDir

$phases = @(
    @{ Name = "warmup"; Method = "all"; Total = [int]$opts.WarmupTotal; Concurrency = [int]$opts.WarmupConcurrency; SkipSeed = $false },
    @{ Name = "baseline-get"; Method = "get"; Total = [int]$opts.BaselineTotal; Concurrency = [int]$opts.BaselineConcurrency; SkipSeed = $true },
    @{ Name = "baseline-pipeline"; Method = "pipeline"; Total = [int]$opts.BaselineTotal; Concurrency = [int]$opts.BaselineConcurrency; SkipSeed = $true },
    @{ Name = "baseline-cacheshell"; Method = "cacheshell"; Total = [int]$opts.BaselineTotal; Concurrency = [int]$opts.BaselineConcurrency; SkipSeed = $true },
    @{ Name = "baseline-slot"; Method = "slot"; Total = [int]$opts.BaselineTotal; Concurrency = [int]$opts.BaselineConcurrency; SkipSeed = $true },
    @{ Name = "baseline-node"; Method = "node"; Total = [Math]::Max([Math]::Min([int]$opts.BaselineTotal / 10, 2000), 100); Concurrency = [Math]::Max([Math]::Min([int]$opts.BaselineConcurrency / 4, 64), 4); SkipSeed = $true },
    @{ Name = "mixed"; Method = "all"; Total = [int]$opts.MixedTotal; Concurrency = [int]$opts.MixedConcurrency; SkipSeed = $true }
)

Write-Host "RunId: $runId"
Write-Host "Conn: $($opts.Conn)"
Write-Host "Output: $runDir"

$results = @()
foreach ($phase in $phases) {
    $results += Invoke-Phase -phase $phase -runId $runId -runDir $runDir -optsLocal $opts
}

$summary = Join-Path $runDir "summary.csv"
$results | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $summary

Write-Host ""
Write-Host "=== StackExchange Phased Stress Summary ==="
$results | Format-Table -AutoSize
Write-Host "Summary CSV: $summary"
