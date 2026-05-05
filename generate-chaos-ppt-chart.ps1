param(
    [string]$BatchDir = "artifacts/chaos-10x/batch-real5x10",
    [string]$OutputFile = "chaos-compare-5x10-detailed-charted.pptx"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Row([object[]]$rows, [string]$scenario, [string]$suite) {
    return $rows | Where-Object { $_.Scenario -eq $scenario -and $_.Suite -eq $suite } | Select-Object -First 1
}

function To-Double([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return 0.0 }
    $result = 0.0
    if ([double]::TryParse($value, [ref]$result)) { return $result }
    return 0.0
}

function Add-TextSlide([object]$presentation, [int]$index, [string]$title, [string]$body) {
    $slide = $presentation.Slides.Add($index, 2)
    $slide.Shapes.Title.TextFrame.TextRange.Text = $title
    $slide.Shapes.Item(2).TextFrame.TextRange.Text = $body
}

function Add-ScenarioStatsSlide([object]$presentation, [int]$index, [string]$scenario, [object]$cs, [object]$se) {
    $body = @"
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
    Add-TextSlide -presentation $presentation -index $index -title ("Scenario Stats: " + $scenario) -body $body
}

function Set-ChartData([object]$chart, [double[]]$csData, [double[]]$seData, [string]$seriesTitle, [string]$yAxisName) {
    $chart.ChartType = 4
    $chart.HasTitle = $true
    $chart.ChartTitle.Text = $seriesTitle
    $chart.HasLegend = $true

    try {
        while ($chart.SeriesCollection().Count -gt 0) {
            $chart.SeriesCollection(1).Delete() | Out-Null
        }
    } catch {}

    $x = @(1, 2, 3, 4, 5)
    $s1 = $chart.SeriesCollection().NewSeries()
    $s1.Name = "csredis"
    $s1.XValues = $x
    $s1.Values = @($csData[0], $csData[1], $csData[2], $csData[3], $csData[4])

    $s2 = $chart.SeriesCollection().NewSeries()
    $s2.Name = "stackexchange"
    $s2.XValues = $x
    $s2.Values = @($seData[0], $seData[1], $seData[2], $seData[3], $seData[4])

    if ($chart.HasAxis(2)) {
        $chart.Axes(2).HasTitle = $true
        $chart.Axes(2).AxisTitle.Text = $yAxisName
    }
}

function Add-ScenarioDetailSlide([object]$presentation, [int]$index, [string]$scenario, [object[]]$allRows, [string]$metricField, [string]$metricLabel, [string]$yAxisName) {
    $slide = $presentation.Slides.Add($index, 6)

    $titleBox = $slide.Shapes.AddTextbox(1, 30, 10, 900, 30)
    $titleBox.TextFrame.TextRange.Text = "Scenario Detail (5 runs): $scenario / $metricLabel"
    $titleBox.TextFrame.TextRange.Font.Size = 24

    $rows = $allRows | Where-Object { $_.Scenario -eq $scenario } | Sort-Object {[int]$_.Iteration}, Suite
    $csByIter = @{}
    $seByIter = @{}
    foreach ($row in $rows) {
        $iter = [int]$row.Iteration
        if ($row.Suite -eq "csredis") { $csByIter[$iter] = $row }
        elseif ($row.Suite -eq "stackexchange") { $seByIter[$iter] = $row }
    }

    $csMetric = New-Object 'double[]' 5
    $seMetric = New-Object 'double[]' 5

    for ($i = 1; $i -le 5; $i++) {
        if ($csByIter.ContainsKey($i)) {
            $csMetric[$i - 1] = To-Double $csByIter[$i].$metricField
        }
        if ($seByIter.ContainsKey($i)) {
            $seMetric[$i - 1] = To-Double $seByIter[$i].$metricField
        }
    }

    # aligned single-chart layout
    $left = 120
    $top = 70
    $width = 720
    $height = 300

    $shape = $slide.Shapes.AddChart2(201, 4, $left, $top, $width, $height)
    Set-ChartData -chart $shape.Chart -csData $csMetric -seData $seMetric -seriesTitle ($metricLabel + " by Iteration") -yAxisName $yAxisName
    try { $shape.Chart.ChartData.Workbook.Application.Quit() | Out-Null } catch {}
}

$summaryPath = Join-Path $BatchDir "aggregate.summary.csv"
$allPath = Join-Path $BatchDir "aggregate.all.csv"
if (-not (Test-Path -LiteralPath $summaryPath)) { throw "aggregate.summary.csv not found: $summaryPath" }
if (-not (Test-Path -LiteralPath $allPath)) { throw "aggregate.all.csv not found: $allPath" }

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

    Add-TextSlide -presentation $presentation -index 1 -title "Redis Chaos 5-Run Report (10x load)" -body "Order: scenario stats -> detail charts`nData source: $summaryPath"
    Add-TextSlide -presentation $presentation -index 2 -title "Test Setup" -body "- 5 real iterations`n- full cluster restart before each run`n- scenario per run: failover / node-down / scale-in`n- compare: csredis vs stackexchange"

    Add-ScenarioStatsSlide -presentation $presentation -index 3 -scenario "failover" -cs $failoverCs -se $failoverSe
    Add-ScenarioDetailSlide -presentation $presentation -index 4 -scenario "failover" -allRows $allRows -metricField "SuccessRate" -metricLabel "SuccessRate" -yAxisName "SuccessRate(%)"
    Add-ScenarioDetailSlide -presentation $presentation -index 5 -scenario "failover" -allRows $allRows -metricField "MixedP95" -metricLabel "MixedP95" -yAxisName "Latency(ms)"

    Add-ScenarioStatsSlide -presentation $presentation -index 6 -scenario "node-down" -cs $nodeDownCs -se $nodeDownSe
    Add-ScenarioDetailSlide -presentation $presentation -index 7 -scenario "node-down" -allRows $allRows -metricField "SuccessRate" -metricLabel "SuccessRate" -yAxisName "SuccessRate(%)"
    Add-ScenarioDetailSlide -presentation $presentation -index 8 -scenario "node-down" -allRows $allRows -metricField "MixedP95" -metricLabel "MixedP95" -yAxisName "Latency(ms)"

    Add-ScenarioStatsSlide -presentation $presentation -index 9 -scenario "scale-in" -cs $scaleInCs -se $scaleInSe
    Add-ScenarioDetailSlide -presentation $presentation -index 10 -scenario "scale-in" -allRows $allRows -metricField "SuccessRate" -metricLabel "SuccessRate" -yAxisName "SuccessRate(%)"
    Add-ScenarioDetailSlide -presentation $presentation -index 11 -scenario "scale-in" -allRows $allRows -metricField "MixedP95" -metricLabel "MixedP95" -yAxisName "Latency(ms)"

    Add-TextSlide -presentation $presentation -index 12 -title "Conclusion" -body "StackExchange.Redis remains stronger in availability and latency across all scenarios."

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
