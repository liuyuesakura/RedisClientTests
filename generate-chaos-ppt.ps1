param(
    [string]$BatchDir = "artifacts/chaos-10x/batch-real5x10",
    [string]$OutputFile = "chaos-compare-5x10-detailed.pptx"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Row([object[]]$rows, [string]$scenario, [string]$suite) {
    return $rows | Where-Object { $_.Scenario -eq $scenario -and $_.Suite -eq $suite } | Select-Object -First 1
}

function Format-Overview([object]$cs, [object]$se) {
    return @"
CSRedis:
- AvgSuccessRate: $($cs.AvgSuccessRate)%
- AvgMixedQps: $($cs.AvgMixedQps)
- AvgMixedP95: $($cs.AvgMixedP95) ms
- AvgMixedP99: $($cs.AvgMixedP99) ms
- AvgWorstMethodP95Ratio: $($cs.AvgWorstMethodP95Ratio)x

StackExchange.Redis:
- AvgSuccessRate: $($se.AvgSuccessRate)%
- AvgMixedQps: $($se.AvgMixedQps)
- AvgMixedP95: $($se.AvgMixedP95) ms
- AvgMixedP99: $($se.AvgMixedP99) ms
- AvgWorstMethodP95Ratio: $($se.AvgWorstMethodP95Ratio)x
"@
}

function Format-Detail([object[]]$allRows, [string]$scenario) {
    $rows = $allRows | Where-Object { $_.Scenario -eq $scenario } | Sort-Object {[int]$_.Iteration}, Suite
    $lines = @()
    $lines += "Iteration | Suite | SuccessRate | MixedQps | MixedP95 | MixedP99 | WorstP95Ratio | Verdict"
    $lines += "--------- | ----- | ---------- | -------- | -------- | -------- | ------------- | -------"
    foreach ($r in $rows) {
        $lines += "$($r.Iteration) | $($r.Suite) | $($r.SuccessRate)% | $($r.MixedQps) | $($r.MixedP95) | $($r.MixedP99) | $($r.WorstMethodP95Ratio)x | $($r.Verdict)"
    }
    return ($lines -join "`n")
}

$summaryPath = Join-Path $BatchDir "aggregate.summary.csv"
$allPath = Join-Path $BatchDir "aggregate.all.csv"
if (-not (Test-Path -LiteralPath $summaryPath)) {
    throw "aggregate.summary.csv not found: $summaryPath"
}
if (-not (Test-Path -LiteralPath $allPath)) {
    throw "aggregate.all.csv not found: $allPath"
}

$summaryRows = Import-Csv -LiteralPath $summaryPath
$allRows = Import-Csv -LiteralPath $allPath

$failoverCs = Get-Row $summaryRows "failover" "csredis"
$failoverSe = Get-Row $summaryRows "failover" "stackexchange"
$nodeDownCs = Get-Row $summaryRows "node-down" "csredis"
$nodeDownSe = Get-Row $summaryRows "node-down" "stackexchange"
$scaleInCs = Get-Row $summaryRows "scale-in" "csredis"
$scaleInSe = Get-Row $summaryRows "scale-in" "stackexchange"

$outputPath = Join-Path $BatchDir $OutputFile
$outputFullPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $outputPath))

$ppt = $null
try {
    $ppt = New-Object -ComObject PowerPoint.Application
    $ppt.Visible = -1
    $presentation = $ppt.Presentations.Add()

    $s1 = $presentation.Slides.Add(1, 1)
    $s1.Shapes.Title.TextFrame.TextRange.Text = "Redis Chaos 5-run Comparison (10x Load)"
    $s1.Shapes.Item(2).TextFrame.TextRange.Text = "Scenarios: failover / node-down / scale-in`nClients: CSRedis vs StackExchange.Redis`nSource: $summaryPath"

    $s2 = $presentation.Slides.Add(2, 2)
    $s2.Shapes.Title.TextFrame.TextRange.Text = "Setup"
    $s2.Shapes.Item(2).TextFrame.TextRange.Text = @"
- 5 real iterations
- Full cluster restart before each iteration
- Request volume scaled to 10x
- Same workload for both clients
"@

    $s3 = $presentation.Slides.Add(3, 2)
    $s3.Shapes.Title.TextFrame.TextRange.Text = "Scenario Overview: failover"
    $s3.Shapes.Item(2).TextFrame.TextRange.Text = Format-Overview $failoverCs $failoverSe

    $s4 = $presentation.Slides.Add(4, 2)
    $s4.Shapes.Title.TextFrame.TextRange.Text = "Scenario Overview: node-down"
    $s4.Shapes.Item(2).TextFrame.TextRange.Text = Format-Overview $nodeDownCs $nodeDownSe

    $s5 = $presentation.Slides.Add(5, 2)
    $s5.Shapes.Title.TextFrame.TextRange.Text = "Scenario Overview: scale-in"
    $s5.Shapes.Item(2).TextFrame.TextRange.Text = Format-Overview $scaleInCs $scaleInSe

    $s6 = $presentation.Slides.Add(6, 2)
    $s6.Shapes.Title.TextFrame.TextRange.Text = "Detail: failover (all 5 runs)"
    $s6.Shapes.Item(2).TextFrame.TextRange.Text = Format-Detail $allRows "failover"

    $s7 = $presentation.Slides.Add(7, 2)
    $s7.Shapes.Title.TextFrame.TextRange.Text = "Detail: node-down (all 5 runs)"
    $s7.Shapes.Item(2).TextFrame.TextRange.Text = Format-Detail $allRows "node-down"

    $s8 = $presentation.Slides.Add(8, 2)
    $s8.Shapes.Title.TextFrame.TextRange.Text = "Detail: scale-in (all 5 runs)"
    $s8.Shapes.Item(2).TextFrame.TextRange.Text = Format-Detail $allRows "scale-in"

    $s9 = $presentation.Slides.Add(9, 2)
    $s9.Shapes.Title.TextFrame.TextRange.Text = "Summary"
    $s9.Shapes.Item(2).TextFrame.TextRange.Text = @"
- StackExchange.Redis remains stronger in availability and latency under all three fault scenarios.
- CSRedis shows severe instability in node-down and scale-in windows.
- Recommend StackExchange.Redis as the default production client for fault tolerance.
"@

    $presentation.SaveAs($outputFullPath)
    $presentation.Close()
    $ppt.Quit()
    Write-Host "PPT generated: $outputFullPath"
}
finally {
    if ($null -ne $ppt) {
        try { [System.Runtime.InteropServices.Marshal]::ReleaseComObject($ppt) | Out-Null } catch {}
    }
}
