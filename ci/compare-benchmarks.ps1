#!/usr/bin/env pwsh
# Compares BenchmarkDotNet JSON results with a stored baseline and writes a markdown report.
# Regressions above the threshold are flagged but never fail the run (warning only, spec FR-018).
# Exit codes: 0 always for regressions or a missing baseline, 2 for unreadable input.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Current,
    [double]$Threshold = 0.10,
    [string]$ReportPath = 'benchmark-report.md'
)

$ErrorActionPreference = 'Stop'

function Read-Means([string]$Path) {
    $json = Get-Content -Raw $Path | ConvertFrom-Json
    $means = [ordered]@{}
    foreach ($benchmark in $json.Benchmarks) {
        $means[$benchmark.FullName] = [double]$benchmark.Statistics.Mean
    }
    return $means
}

function Format-Time([double]$Nanoseconds) {
    if ($Nanoseconds -ge 1e6) { return ('{0:N3} ms' -f ($Nanoseconds / 1e6)) }
    if ($Nanoseconds -ge 1e3) { return ('{0:N3} us' -f ($Nanoseconds / 1e3)) }
    return ('{0:N1} ns' -f $Nanoseconds)
}

if (-not (Test-Path $Current)) {
    Write-Error "Current results not found: $Current"
    exit 2
}

try { $currentMeans = Read-Means $Current } catch { Write-Error "Cannot read $Current : $_"; exit 2 }

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Benchmark report')
$lines.Add('')

if (-not (Test-Path $Baseline)) {
    $lines.Add("No baseline found at ``$Baseline``: nothing to compare. To record these results as the first baseline, commit the downloaded results JSON to that path.")
    $lines.Add('')
    $lines.Add('| Benchmark | Mean |')
    $lines.Add('|---|---|')
    foreach ($name in $currentMeans.Keys) { $lines.Add("| $name | $(Format-Time $currentMeans[$name]) |") }
    Set-Content -Path $ReportPath -Value $lines
    exit 0
}

try { $baselineMeans = Read-Means $Baseline } catch { Write-Error "Cannot read $Baseline : $_"; exit 2 }

$regressions = 0
$rows = [System.Collections.Generic.List[string]]::new()
foreach ($name in $currentMeans.Keys) {
    $new = $currentMeans[$name]
    if (-not $baselineMeans.Contains($name)) {
        $rows.Add("| $name | - | $(Format-Time $new) | - | new |")
        continue
    }
    $old = $baselineMeans[$name]
    $change = if ($old -gt 0) { ($new - $old) / $old } else { 0 }
    $flag = ''
    if ($change -gt $Threshold) {
        $flag = '**REGRESSION**'
        $regressions++
        if ($env:GITHUB_ACTIONS) {
            Write-Host "::warning title=Benchmark regression::$name is $('{0:P1}' -f $change) slower than the baseline"
        }
    }
    $rows.Add("| $name | $(Format-Time $old) | $(Format-Time $new) | $('{0:+0.0%;-0.0%;0.0%}' -f $change) | $flag |")
}
foreach ($name in $baselineMeans.Keys) {
    if (-not $currentMeans.Contains($name)) { $rows.Add("| $name | $(Format-Time $baselineMeans[$name]) | - | - | missing |") }
}

$lines.Add("Threshold: $('{0:P0}' -f $Threshold) slower than the baseline. Regressions: **$regressions**.")
$lines.Add('')
$lines.Add('| Benchmark | Baseline | Current | Change | Flag |')
$lines.Add('|---|---|---|---|---|')
foreach ($row in $rows) { $lines.Add($row) }
Set-Content -Path $ReportPath -Value $lines
Write-Host ($lines -join "`n")
exit 0
