param([switch]$NoRestore, [switch]$Offline, [switch]$HelpOnly)
$ErrorActionPreference = 'Stop'
if ($Offline -and $HelpOnly) { throw 'Choose either -Offline or -HelpOnly.' }
. (Join-Path $PSScriptRoot 'Python-Environment.ps1')
Push-Location $pythonRepoRoot
try {
    Initialize-PythonEnvironment
    if ($HelpOnly) {
        & $python -m pytest tests/python/test_stdio.py
        if ($LASTEXITCODE -ne 0) { throw 'Python help checks failed.' }
        return
    }
    $pytestArgs = if ($Offline) { @('-m', 'not integration') } else { @() }
    & $python -m pytest @pytestArgs
    if ($LASTEXITCODE -ne 0) { throw 'Python MCP checks failed.' }
    . (Join-Path $PSScriptRoot 'Build-Environment.ps1')
    if (!$NoRestore) { Invoke-DotNet restore tests/PSMcp.SmokeTests/PSMcp.SmokeTests.csproj --locked-mode }
    Invoke-DotNet build tests/PSMcp.SmokeTests/PSMcp.SmokeTests.csproj -c Release --no-restore --disable-build-servers '-m:1' '-p:UseSharedCompilation=false'
    if ($Offline) {
        Invoke-DotNet tests/PSMcp.SmokeTests/bin/Release/net10.0/PSMcp.SmokeTests.dll --offline
    } else {
        Invoke-DotNet tests/PSMcp.SmokeTests/bin/Release/net10.0/PSMcp.SmokeTests.dll $python
    }
} finally { Pop-Location }
