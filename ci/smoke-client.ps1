#!/usr/bin/env pwsh
# Starts the real client (window and WebGPU renderer), lets it simulate a few ticks and checks that it
# prints the fingerprint line and exits with code 0. Needs a display and a Vulkan/Metal/D3D12 device
# (CI uses a virtual display and a software driver). Exit codes: 0 started and exited, 1 otherwise.
[CmdletBinding()]
param([string]$Configuration = 'Release', [int]$TimeoutSeconds = 120)

$ErrorActionPreference = 'Stop'
$arguments = @('run', '-c', $Configuration, '--project', 'src/Talt.Client', '--', '4', '1000', '1',
    '--print-fingerprint-at', '20', '--exit-after-fingerprint')
$stdout = [System.IO.Path]::GetTempFileName()
$stderr = [System.IO.Path]::GetTempFileName()
$process = Start-Process dotnet -ArgumentList $arguments -PassThru -NoNewWindow `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    Write-Host "FAIL client did not exit within $TimeoutSeconds s"
    Get-Content $stdout, $stderr | Write-Host
    exit 1
}

$output = Get-Content $stdout, $stderr
$output | Write-Host
if ($process.ExitCode -eq 0 -and ($output -match '^fingerprint at tick 20: [0-9A-F]{16}$')) {
    Write-Host 'PASS client started, simulated and exited cleanly'
    exit 0
}

Write-Host "FAIL client exit code $($process.ExitCode) or fingerprint line missing"
exit 1
