param(
    [Parameter(Mandatory)][string]$GamePath,
    [string[]]$TypeNames = @('PlayerStore', 'StoreClient', 'StoreClientData', 'StoreCalendar', 'DialogUIManager', 'GameGridInventory', 'GameItem', 'ModConfig', 'ModLog'),
    [string]$TypePattern,
    [string]$MemberPattern = '.',
    [switch]$IncludeNonPublic
)
$ErrorActionPreference = 'Stop'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
$sdkVersion = & $dotnet --version
$sdkRoot = Join-Path (Split-Path $dotnet) "sdk/$sdkVersion"
Add-Type -Path (Join-Path $sdkRoot 'System.Reflection.MetadataLoadContext.dll')
$managed = Join-Path $GamePath 'Probably Stolen_Data/Managed'
$assemblyPaths = [string[]](Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File).FullName
$resolver = [System.Reflection.PathAssemblyResolver]::new($assemblyPaths)
$context = [System.Reflection.MetadataLoadContext]::new($resolver, 'mscorlib')
try {
    $assembly = $context.LoadFromAssemblyPath((Join-Path $managed 'Assembly-CSharp.dll'))
    if ($TypePattern) {
        $assembly.GetTypes() | Where-Object { $_.FullName -match $TypePattern } | ForEach-Object { $_.FullName }
        return
    }
    foreach ($typeName in $TypeNames) {
        $type = $assembly.GetType($typeName)
        if (!$type) { Write-Output "TYPE NOT FOUND: $typeName"; continue }
        Write-Output "TYPE: $($type.FullName) BASE: $($type.BaseType.FullName)"
        $flags = [System.Reflection.BindingFlags]'Public, Instance, Static, DeclaredOnly'
        if ($IncludeNonPublic) { $flags = $flags -bor [System.Reflection.BindingFlags]::NonPublic }
        $type.GetFields($flags) | Where-Object { $_.Name -match $MemberPattern } | ForEach-Object {
            $constant = if ($_.IsLiteral) { " = $($_.GetRawConstantValue())" } else { '' }
            Write-Output "FIELD: $($_.ToString())$constant"
        }
        $type.GetProperties($flags) | Where-Object { $_.Name -match $MemberPattern } | ForEach-Object { Write-Output "PROPERTY: $($_.ToString())" }
        $type.GetMethods($flags) | Where-Object { !$_.IsSpecialName -and $_.Name -match $MemberPattern } | ForEach-Object {
            $parameterNames = ($_.GetParameters() | ForEach-Object { $_.Name }) -join ', '
            Write-Output "METHOD: $($_.ToString()) PARAMETERS: $parameterNames"
        }
    }
} finally { $context.Dispose() }
