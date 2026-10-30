param([switch]$NoRestore)
. (Join-Path $PSScriptRoot 'Build-Environment.ps1')
Push-Location $repoRoot
try {
    if (!$NoRestore) { Invoke-DotNet restore tests/PawnPilot.SmokeTests/PawnPilot.SmokeTests.csproj --locked-mode }
    Invoke-DotNet build tests/PawnPilot.SmokeTests/PawnPilot.SmokeTests.csproj -c Release --no-restore --disable-build-servers '-m:1' '-p:UseSharedCompilation=false'
    $mcpDll = Join-Path $repoRoot 'src/PawnPilot.Mcp/bin/Release/net10.0/PawnPilot.Mcp.dll'
    Invoke-DotNet tests/PawnPilot.SmokeTests/bin/Release/net10.0/PawnPilot.SmokeTests.dll $mcpDll
} finally { Pop-Location }
