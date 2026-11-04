$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$application = Join-Path $projectRoot 'app\ParamForge.exe'
try {
    if (-not (Test-Path -LiteralPath $application)) {
        & (Join-Path $PSScriptRoot 'build.ps1')
    }
    Start-Process -FilePath $application -WorkingDirectory (Split-Path -Parent $application)
}
catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'ParamForge', 'OK', 'Error') | Out-Null
}
