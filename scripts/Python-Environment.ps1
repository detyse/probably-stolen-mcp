$pythonRepoRoot = Split-Path $PSScriptRoot -Parent
$env:UV_CACHE_DIR = Join-Path $pythonRepoRoot '.cache/uv'
$uvCommand = Get-Command uv -ErrorAction SilentlyContinue
if (!$uvCommand) { throw 'Install uv, reopen PowerShell, and run this script again. See docs/development.md.' }
$uv = $uvCommand.Source

function Invoke-Uv {
    & $uv @args
    if ($LASTEXITCODE -ne 0) { throw "uv command failed with exit code $LASTEXITCODE." }
}

function Initialize-PythonEnvironment {
    Invoke-Uv sync --project $pythonRepoRoot --locked
    $script:python = Join-Path $pythonRepoRoot '.venv/Scripts/python.exe'
    if (!(Test-Path -LiteralPath $python)) { throw 'The project Python environment was not created.' }
}
