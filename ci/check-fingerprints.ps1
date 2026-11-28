#!/usr/bin/env pwsh
# Runs the headless simulation and compares its fingerprints with the golden values, then replays
# the recorded sessions. Same script on every platform (specs/002-continuous-integration/contracts/ci-scripts.md).
# Exit codes: 0 all checks pass, 1 a check failed, 2 usage error or missing file.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Golden = 'ci/golden-fingerprints.json',
    [switch]$SkipReference
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Golden)) {
    Write-Error "Golden file not found: $Golden"
    exit 2
}

$expected = Get-Content -Raw $Golden | ConvertFrom-Json
$project = 'tools/Talt.Headless'
$failed = $false

# Build once, then run without rebuilding so timings and output stay clean.
dotnet build -c $Configuration $project --nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error 'Build of Talt.Headless failed'; exit 2 }

function Invoke-Headless([string[]]$Arguments) {
    $output = & dotnet run -c $Configuration --no-build --project $project -- @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n") }
}

foreach ($run in $expected.runs) {
    if ($SkipReference -and $run.name -eq 'reference') { continue }
    $result = Invoke-Headless $run.args
    $actual = if ($result.Output -match '(?m)^fingerprint:\s*([0-9A-Fa-f]+)') { $Matches[1].ToUpperInvariant() } else { '<none>' }
    if ($result.ExitCode -eq 0 -and $actual -eq $run.fingerprint) {
        Write-Host "PASS run $($run.name) expected=$($run.fingerprint) actual=$actual"
    } else {
        Write-Host "FAIL run $($run.name) expected=$($run.fingerprint) actual=$actual exit=$($result.ExitCode)"
        $failed = $true
    }
}

foreach ($replay in $expected.replays) {
    if (-not (Test-Path $replay.session)) { Write-Error "Session not found: $($replay.session)"; exit 2 }
    $result = Invoke-Headless @('replay', $replay.session, '--checkpoint-every', '1000')
    if ($result.ExitCode -eq 0 -and $result.Output -notmatch 'MISMATCH') {
        Write-Host "PASS replay $($replay.name)"
    } else {
        Write-Host "FAIL replay $($replay.name) exit=$($result.ExitCode)"
        Write-Host $result.Output
        $failed = $true
    }
}

foreach ($save in $expected.saves) {
    $result = Invoke-Headless @('load', $save.path, '--ticks', $save.ticks)
    if ($result.ExitCode -eq 0 -and $result.Output -match "(?m)^loaded tick=\d+ fingerprint=$($save.loaded)\s*$" -and
        $result.Output -match "(?m)^fingerprint: $($save.continued)\s*$") {
        Write-Host "PASS $($save.path) and $($save.ticks)-tick continuation"
    } else {
        Write-Host "FAIL $($save.path): $($result.Output)"
        $failed = $true
    }
}

if ($failed) { exit 1 }
exit 0
