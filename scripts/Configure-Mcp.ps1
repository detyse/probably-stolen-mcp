$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path $PSScriptRoot -Parent
$uvCommand = Get-Command uv -CommandType Application -ErrorAction SilentlyContinue
if (!$uvCommand) { throw 'Install uv, reopen PowerShell, and run this script again. See README.md.' }
if (!(Test-Path -LiteralPath (Join-Path $packageRoot 'pyproject.toml')) -or
    !(Test-Path -LiteralPath (Join-Path $packageRoot 'uv.lock'))) {
    throw 'Keep this script in the scripts directory of the extracted release or source checkout.'
}
$config = @{
    mcpServers = @{
        'probably-stolen-mcp' = @{
            command = $uvCommand.Source
            args = @('--directory', $packageRoot, 'run', '--locked', '--no-dev', 'probably-stolen-mcp')
        }
    }
}
$outputDirectory = Join-Path $packageRoot 'artifacts'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputPath = Join-Path $outputDirectory 'mcp-config.json'
$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Output "MCP client config: $outputPath"
