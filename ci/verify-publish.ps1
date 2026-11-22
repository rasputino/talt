#!/usr/bin/env pwsh
# Verifies that a self-contained publish output contains the client executable and the native
# graphics (wgpu-native) and windowing (GLFW) libraries for its runtime.
# Exit codes: 0 all present, 1 something missing, 2 usage error.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64')][string]$Rid
)

$required = switch ($Rid) {
    'win-x64'    { @('Talt.Client.exe', 'wgpu_native.dll', 'glfw3.dll') }
    'linux-x64'  { @('Talt.Client', 'libwgpu_native.so', 'libglfw.so.3') }
    'osx-arm64'  { @('Talt.Client', 'libwgpu_native.dylib', 'libglfw.3.dylib') }
}

if (-not (Test-Path $Directory)) {
    Write-Error "Directory not found: $Directory"
    exit 2
}

$missing = @($required | Where-Object { -not (Test-Path (Join-Path $Directory $_)) })
if ($missing.Count -gt 0) {
    foreach ($item in $missing) { Write-Host "MISSING $item in $Directory ($Rid)" }
    exit 1
}

Write-Host "OK ${Rid}: $($required -join ', ')"
exit 0
