$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

$opts = @{
    Mode = "both" # both|grpc|direct
    RunId = ""
    OutputDir = "artifacts/phased-all"
    DryRun = $false

    # grpc phased options
    GrpcSuite = "both"
    CsredisTarget = "http://127.0.0.1:50051"
    StackexchangeTarget = "http://127.0.0.1:50052"

    # direct stackexchange phased options
    DirectConn = "redis-cluster-1:6379,redis-cluster-2:6379,redis-cluster-3:6379,abortConnect=false,connectTimeout=5000,syncTimeout=5000"

    # shared load knobs
    WarmupTotal = 500
    WarmupConcurrency = 20
    BaselineTotal = 5000
    BaselineConcurrency = 100
    MixedTotal = 20000
    MixedConcurrency = 200
    PipelineKeyCount = 3
    ExpirySeconds = 300
    MultiHashtag = $false
}

for ($i = 0; $i -lt $args.Count; $i++) {
    $name = [string]$args[$i]
    switch ($name.ToLowerInvariant()) {
        "-mode" { $i++; $opts.Mode = [string]$args[$i] }
        "-runid" { $i++; $opts.RunId = [string]$args[$i] }
        "-outputdir" { $i++; $opts.OutputDir = [string]$args[$i] }
        "-dryrun" { $opts.DryRun = $true }

        "-grpcsuite" { $i++; $opts.GrpcSuite = [string]$args[$i] }
        "-csredistarget" { $i++; $opts.CsredisTarget = [string]$args[$i] }
        "-stackexchangetarget" { $i++; $opts.StackexchangeTarget = [string]$args[$i] }
        "-directconn" { $i++; $opts.DirectConn = [string]$args[$i] }

        "-warmuptotal" { $i++; $opts.WarmupTotal = [int]$args[$i] }
        "-warmupconcurrency" { $i++; $opts.WarmupConcurrency = [int]$args[$i] }
        "-baselinetotal" { $i++; $opts.BaselineTotal = [int]$args[$i] }
        "-baselineconcurrency" { $i++; $opts.BaselineConcurrency = [int]$args[$i] }
        "-mixedtotal" { $i++; $opts.MixedTotal = [int]$args[$i] }
        "-mixedconcurrency" { $i++; $opts.MixedConcurrency = [int]$args[$i] }
        "-pipelinekeycount" { $i++; $opts.PipelineKeyCount = [int]$args[$i] }
        "-expiryseconds" { $i++; $opts.ExpirySeconds = [int]$args[$i] }
        "-multihashtag" { $opts.MultiHashtag = $true }
        default { throw "Unknown argument: $name" }
    }
}

$validModes = @("both", "grpc", "direct")
if ($validModes -notcontains $opts.Mode.ToLowerInvariant()) {
    throw "Mode must be both|grpc|direct"
}

$validGrpcSuites = @("csredis", "stackexchange", "both")
if ($validGrpcSuites -notcontains $opts.GrpcSuite.ToLowerInvariant()) {
    throw "GrpcSuite must be csredis|stackexchange|both"
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

$runId = Get-RunId $opts.RunId
Ensure-Dir $opts.OutputDir
$rootDir = Join-Path $opts.OutputDir ("run-" + $runId)
Ensure-Dir $rootDir

$grpcScript = Join-Path $PSScriptRoot "run-grpc-phased-stress.ps1"
$directScript = Join-Path $PSScriptRoot "run-stackexchange-phased-stress.ps1"

$jobs = @()

if ($opts.Mode -in @("both", "grpc")) {
    $grpcOutDir = Join-Path $rootDir "grpc"
    Ensure-Dir $grpcOutDir
    $grpcArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $grpcScript,
        "-Suite", $opts.GrpcSuite,
        "-CsredisTarget", $opts.CsredisTarget,
        "-StackexchangeTarget", $opts.StackexchangeTarget,
        "-OutputDir", $grpcOutDir,
        "-RunId", $runId,
        "-WarmupTotal", $opts.WarmupTotal,
        "-WarmupConcurrency", $opts.WarmupConcurrency,
        "-BaselineTotal", $opts.BaselineTotal,
        "-BaselineConcurrency", $opts.BaselineConcurrency,
        "-MixedTotal", $opts.MixedTotal,
        "-MixedConcurrency", $opts.MixedConcurrency,
        "-PipelineKeyCount", $opts.PipelineKeyCount,
        "-ExpirySeconds", $opts.ExpirySeconds
    )
    if ($opts.MultiHashtag) { $grpcArgs += "-MultiHashtag" }
    if ($opts.DryRun) { $grpcArgs += "-DryRun" }

    $grpcStdout = Join-Path $rootDir "grpc.console.log"
    $grpcStderr = Join-Path $rootDir "grpc.console.err.log"
    $jobs += [pscustomobject]@{
        Name = "grpc"
        Process = Start-Process -FilePath "powershell" -ArgumentList $grpcArgs -PassThru -NoNewWindow -RedirectStandardOutput $grpcStdout -RedirectStandardError $grpcStderr
        Stdout = $grpcStdout
        Summary = if ($opts.GrpcSuite -eq "both") {
            Join-Path (Join-Path $grpcOutDir ("run-" + $runId)) "summary.both.csv"
        }
        else {
            Join-Path (Join-Path $grpcOutDir ("run-" + $runId)) "summary.csv"
        }
    }
}

if ($opts.Mode -in @("both", "direct")) {
    $directOutDir = Join-Path $rootDir "direct"
    Ensure-Dir $directOutDir
    $directArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $directScript,
        "-Conn", $opts.DirectConn,
        "-OutputDir", $directOutDir,
        "-RunId", $runId,
        "-WarmupTotal", $opts.WarmupTotal,
        "-WarmupConcurrency", $opts.WarmupConcurrency,
        "-BaselineTotal", $opts.BaselineTotal,
        "-BaselineConcurrency", $opts.BaselineConcurrency,
        "-MixedTotal", $opts.MixedTotal,
        "-MixedConcurrency", $opts.MixedConcurrency,
        "-PipelineKeyCount", $opts.PipelineKeyCount,
        "-ExpirySeconds", $opts.ExpirySeconds
    )
    if ($opts.MultiHashtag) { $directArgs += "-MultiHashtag" }
    if ($opts.DryRun) { $directArgs += "-DryRun" }

    $directStdout = Join-Path $rootDir "direct.console.log"
    $directStderr = Join-Path $rootDir "direct.console.err.log"
    $jobs += [pscustomobject]@{
        Name = "direct"
        Process = Start-Process -FilePath "powershell" -ArgumentList $directArgs -PassThru -NoNewWindow -RedirectStandardOutput $directStdout -RedirectStandardError $directStderr
        Stdout = $directStdout
        Summary = Join-Path (Join-Path $directOutDir ("run-" + $runId)) "summary.csv"
    }
}

if ($jobs.Count -eq 0) {
    throw "No runnable job selected."
}

Write-Host "RunId: $runId"
Write-Host "Mode: $($opts.Mode)"
Write-Host "Output: $rootDir"
Write-Host "Jobs: $($jobs.Name -join ', ')"

$pids = $jobs | ForEach-Object { $_.Process.Id }
Wait-Process -Id $pids

foreach ($job in $jobs) {
    if (-not (Test-Path -LiteralPath $job.Summary)) {
        throw "Job '$($job.Name)' failed or missing summary. See log: $($job.Stdout)"
    }
}

Write-Host ""
Write-Host "All selected phased runs completed."
foreach ($job in $jobs) {
    Write-Host "$($job.Name) log: $($job.Stdout)"
}
