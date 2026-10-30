param(
    [string]$GamePath = $env:PAWNPILOT_GAME_PATH,
    [switch]$NoRestore,
    [switch]$SkipTests
)
. (Join-Path $PSScriptRoot 'Build-Environment.ps1')
if (!$GamePath) { throw 'Pass -GamePath with the directory containing Probably Stolen.exe, or set PAWNPILOT_GAME_PATH.' }
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
if (!(Test-Path -LiteralPath (Join-Path $GamePath 'Probably Stolen_Data/Managed/Assembly-CSharp.dll'))) {
    throw 'GamePath does not contain the required game assembly.'
}
Push-Location $repoRoot
try {
    if (!$NoRestore) { Invoke-DotNet restore PawnPilot.slnx --locked-mode }
    Invoke-DotNet build PawnPilot.slnx -c Release --no-restore --disable-build-servers '-m:1' '-p:UseSharedCompilation=false' "-p:GamePath=$GamePath"
    if (!$SkipTests) {
        $mcpDll = Join-Path $repoRoot 'src/PawnPilot.Mcp/bin/Release/net10.0/PawnPilot.Mcp.dll'
        Invoke-DotNet tests/PawnPilot.SmokeTests/bin/Release/net10.0/PawnPilot.SmokeTests.dll $mcpDll
    }
    $modOutput = Join-Path $repoRoot 'artifacts/mod/PawnPilot'
    New-Item -ItemType Directory -Path $modOutput -Force | Out-Null
    $allowed = @('manifest.xml', 'PawnPilot.Mod.dll', 'PawnPilot.Contracts.dll')
    $unexpected = Get-ChildItem -LiteralPath $modOutput -Force | Where-Object { $_.Name -notin $allowed }
    if ($unexpected) { throw "Unexpected files in $modOutput. Move them out before packaging; only the declared Mod files may ship." }
    foreach ($file in $allowed) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "src/PawnPilot.Mod/bin/Release/netstandard2.1/$file") -Destination $modOutput -Force
    }
    $mcpOutput = Join-Path $repoRoot 'artifacts/mcp'
    Invoke-DotNet publish src/PawnPilot.Mcp/PawnPilot.Mcp.csproj -c Release --no-restore --disable-build-servers '-m:1' '-p:UseSharedCompilation=false' --self-contained false -o $mcpOutput
    $config = @{ mcpServers = @{ pawnpilot = @{ command = $dotnet; args = @((Join-Path $mcpOutput 'PawnPilot.Mcp.dll')) } } }
    $config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts/mcp-config.json') -Encoding utf8
    Write-Output "Mod package: $modOutput"
    Write-Output "MCP client config: $(Join-Path $repoRoot 'artifacts/mcp-config.json')"
} finally { Pop-Location }
