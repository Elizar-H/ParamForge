param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$NoDeploy)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'native-tools.ps1')
$tools = Initialize-ParamForgeNativeTools -Preset ('windows-' + $Configuration.ToLowerInvariant())
Invoke-ParamForgeBuild $tools $projectRoot
if (-not $NoDeploy -and $Configuration -eq 'Release') {
    $stage = Join-Path $tools.Work ('staging\app-' + [Guid]::NewGuid().ToString('N'))
    Copy-ParamForgeApplication $tools $projectRoot $stage
    $application = Join-Path $projectRoot 'app'
    $expected = [IO.Path]::GetFullPath((Join-Path $projectRoot 'app'))
    if ([IO.Path]::GetFullPath($application) -ne $expected) { throw 'Unexpected application directory.' }
    if (Test-Path -LiteralPath $application) {
        $archive = Join-Path $tools.Work ('archive\previous-app-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
        Move-Item -LiteralPath $application -Destination $archive
    }
    Move-Item -LiteralPath $stage -Destination $application
    Write-Output (Join-Path $application 'ParamForge.exe')
}
