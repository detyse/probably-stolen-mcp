param([switch]$NoRestore)
. (Join-Path $PSScriptRoot 'Build-Environment.ps1')
Push-Location $repoRoot
try {
    if (!$NoRestore) { Invoke-DotNet restore tests/PSMcp.SmokeTests/PSMcp.SmokeTests.csproj --locked-mode }
    Invoke-DotNet build tests/PSMcp.SmokeTests/PSMcp.SmokeTests.csproj -c Release --no-restore --disable-build-servers '-m:1' '-p:UseSharedCompilation=false'
    $mcpDll = Join-Path $repoRoot 'src/PSMcp.Server/bin/Release/net10.0/PSMcp.Server.dll'
    Invoke-DotNet tests/PSMcp.SmokeTests/bin/Release/net10.0/PSMcp.SmokeTests.dll $mcpDll
} finally { Pop-Location }
