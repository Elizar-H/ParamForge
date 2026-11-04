param([switch]$Ui)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'native-tools.ps1')
$tools = Initialize-ParamForgeNativeTools -Preset 'windows-checks'
Invoke-ParamForgeBuild $tools $projectRoot
$run = Join-Path $tools.Work ('qa\checks-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$savedQa = $env:PARAMFORGE_QA_DIR
$savedScale = $env:QT_SCALE_FACTOR
$savedOracle = $env:PARAMFORGE_REFERENCE_FILE
try {
    $env:PARAMFORGE_QA_DIR = $run
    $coreReport = (Join-Path $run 'core.txt') + ',txt'
    & (Join-Path $tools.Build 'bin\ParamForge.Tests.exe') -o $coreReport -o '-,txt'
    if ($LASTEXITCODE -ne 0) { throw ('Core checks failed: ' + $run) }
    if ($Ui) {
        foreach ($scale in @('1', '1.25', '1.5', '2')) {
            $env:QT_SCALE_FACTOR = $scale
            $env:PARAMFORGE_QA_DIR = Join-Path $run ('ui-' + $scale)
            New-Item -ItemType Directory -Path $env:PARAMFORGE_QA_DIR -Force | Out-Null
            $uiReport = (Join-Path $env:PARAMFORGE_QA_DIR 'tests.txt') + ',txt'
            & (Join-Path $tools.Build 'bin\ParamForge.UiTests.exe') -o $uiReport -o '-,txt'
            if ($LASTEXITCODE -ne 0) { throw ('UI checks failed at scale ' + $scale + ': ' + $run) }
        }
    }
}
finally {
    $env:PARAMFORGE_QA_DIR = $savedQa
    $env:QT_SCALE_FACTOR = $savedScale
    $env:PARAMFORGE_REFERENCE_FILE = $savedOracle
}
Write-Output ('Checks: ' + $run)
