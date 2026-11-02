param(
    [string]$GamePath = $env:PSMCP_GAME_PATH,
    [switch]$NoRestore,
    [switch]$SkipTests
)
. (Join-Path $PSScriptRoot 'Build-Environment.ps1')
if (!$GamePath) { throw 'Pass -GamePath with the directory containing Probably Stolen.exe, or set PSMCP_GAME_PATH.' }
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
if (!(Test-Path -LiteralPath (Join-Path $GamePath 'Probably Stolen_Data/Managed/Assembly-CSharp.dll'))) {
    throw 'GamePath does not contain the required game assembly.'
}
Push-Location $repoRoot
try {
    if (!$NoRestore) { Invoke-DotNet restore PSMcp.slnx --locked-mode }
    Invoke-DotNet build PSMcp.slnx -c Release --no-restore --disable-build-servers '-m:1' '-p:UseSharedCompilation=false' "-p:GamePath=$GamePath"
    if (!$SkipTests) {
        $mcpDll = Join-Path $repoRoot 'src/PSMcp.Server/bin/Release/net10.0/PSMcp.Server.dll'
        Invoke-DotNet tests/PSMcp.SmokeTests/bin/Release/net10.0/PSMcp.SmokeTests.dll $mcpDll
    }
    $modOutput = Join-Path $repoRoot 'artifacts/mod/probably-stolen-mcp'
    New-Item -ItemType Directory -Path $modOutput -Force | Out-Null
    $allowed = @('manifest.xml', 'PSMcp.Mod.dll', 'PSMcp.Contracts.dll')
    $unexpected = Get-ChildItem -LiteralPath $modOutput -Force | Where-Object { $_.Name -notin $allowed }
    if ($unexpected) { throw "Unexpected files in $modOutput. Move them out before packaging; only the declared Mod files may ship." }
    foreach ($file in $allowed) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "src/PSMcp.Mod/bin/Release/netstandard2.1/$file") -Destination $modOutput -Force
    }
    $mcpOutput = Join-Path $repoRoot 'artifacts/mcp'
    Invoke-DotNet publish src/PSMcp.Server/PSMcp.Server.csproj -c Release --no-restore --disable-build-servers '-m:1' '-p:UseSharedCompilation=false' --self-contained false -o $mcpOutput
    $config = @{ mcpServers = @{ 'probably-stolen-mcp' = @{ command = $dotnet; args = @((Join-Path $mcpOutput 'PSMcp.Server.dll')) } } }
    $config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts/mcp-config.json') -Encoding utf8
    Write-Output "Mod package: $modOutput"
    Write-Output "MCP client config: $(Join-Path $repoRoot 'artifacts/mcp-config.json')"
} finally { Pop-Location }
