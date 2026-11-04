. (Join-Path $PSScriptRoot 'workspace.ps1')

function Initialize-ParamForgeNativeTools {
    param([ValidateSet('windows-debug', 'windows-release', 'windows-checks')]
        [string]$Preset = 'windows-release')
    $workRoot = Get-ParamForgeWorkRoot
    $qtCandidates = if ($env:PARAMFORGE_QT_ROOT) { @($env:PARAMFORGE_QT_ROOT) } else { @('C:\Qt\6.12.0\msvc2022_64') }
    $qtRoot = $qtCandidates | Where-Object {
        $_ -and (Test-Path -LiteralPath (Join-Path $_ 'lib\cmake\Qt6\Qt6Config.cmake'))
    } | Select-Object -First 1
    if (-not $qtRoot) { throw 'Qt 6.12 MSVC 2022 x64 SDK was not found. Set PARAMFORGE_QT_ROOT.' }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer was not found.' }
    $visualStudio = & $vswhere -latest -version '[17.0,19.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $visualStudio) { throw 'Visual Studio 2022/2026 C++ x64 build tools were not found.' }
    $vcvars = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvarsall.bat'
    $compilerEnvironment = & cmd.exe /d /c ('call "' + $vcvars + '" x64 >nul && set')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the C++ compiler environment.' }
    foreach ($line in $compilerEnvironment) {
        if ($line -match '^(PATH|INCLUDE|LIB|LIBPATH|VCToolsInstallDir|VCToolsRedistDir|WindowsSdkDir|VCINSTALLDIR|VSINSTALLDIR)=(.*)$') {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
        }
    }
    $prefix = $env:PARAMFORGE_MSVC_INCLUDES_PREFIX
    $compilerFolder = Join-Path $env:VCToolsInstallDir 'bin\Hostx64\x64'
    if (Test-Path -LiteralPath (Join-Path $compilerFolder '1033\clui.dll')) {
        $env:VSLANG = '1033'
        if (-not $prefix) { $prefix = 'Note: including file: ' }
    }
    elseif (Test-Path -LiteralPath (Join-Path $compilerFolder '1049\clui.dll')) {
        $env:VSLANG = '1049'
        # CMake/Ninja otherwise misdecode the Russian /showIncludes prefix.
        if (-not $prefix) { $prefix = 'Примечание: включение файла: ' }
    }
    $cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    $ninja = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    if (-not (Test-Path -LiteralPath $cmake)) {
        $cmake = 'C:\Qt\Tools\CMake_64\bin\cmake.exe'
    }
    if (-not (Test-Path -LiteralPath $ninja)) {
        $ninja = 'C:\Qt\Tools\Ninja\ninja.exe'
    }
    if (-not (Test-Path -LiteralPath $cmake) -or -not (Test-Path -LiteralPath $ninja)) {
        throw 'CMake and Ninja are required. Install the Visual Studio C++ CMake tools.'
    }
    $env:PATH = (Join-Path $qtRoot 'bin') + ';' + (Split-Path -Parent $ninja) + ';' + $env:PATH
    $projectRoot = Split-Path -Parent $PSScriptRoot
    return @{ Work = $workRoot; Qt = $qtRoot; CMake = $cmake; Ninja = $ninja; VisualStudio = $visualStudio
        Preset = $Preset; Build = (Join-Path $projectRoot ('build\' + $Preset.Replace('windows-', ''))); IncludesPrefix = $prefix }
}

function Invoke-ParamForgeBuild([hashtable]$Tools, [string]$ProjectRoot) {
    $logDirectory = Join-Path $Tools.Work 'logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $configureLog = Join-Path $logDirectory ($Tools.Preset + '-configure.log')
    $arguments = @('--preset', $Tools.Preset, ('-DPARAMFORGE_QT_ROOT=' + $Tools.Qt),
        ('-DCMAKE_MAKE_PROGRAM=' + $Tools.Ninja))
    if ($Tools.IncludesPrefix) { $arguments += '-DPARAMFORGE_MSVC_INCLUDES_PREFIX=' + $Tools.IncludesPrefix }
    Push-Location -LiteralPath $ProjectRoot
    $savedErrorAction = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 treats native stderr warnings as terminating errors.
        $ErrorActionPreference = 'Continue'
        & $Tools.CMake @arguments *> $configureLog
        if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $configureLog -Tail 35; throw ('CMake failed: ' + $configureLog) }
        $buildLog = Join-Path $logDirectory ($Tools.Preset + '-build.log')
        & $Tools.CMake --build --preset $Tools.Preset *> $buildLog
        if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $buildLog -Tail 60; throw ('Build failed: ' + $buildLog) }
    }
    finally { $ErrorActionPreference = $savedErrorAction; Pop-Location }
    Write-Output ('Build: ' + (Join-Path $Tools.Build 'bin\ParamForge.exe'))
}

function Copy-ParamForgeApplication([hashtable]$Tools, [string]$ProjectRoot, [string]$Destination) {
    if ($Tools.Preset -eq 'windows-debug') { throw 'Use the Release configuration for a standalone application.' }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Tools.Build 'bin\ParamForge.exe') -Destination $Destination
    $log = Join-Path $Tools.Work 'logs\deploy.log'
    & (Join-Path $Tools.Qt 'bin\windeployqt.exe') --release --no-compiler-runtime --no-translations --no-quick-import --skip-plugin-types generic,networkinformation,tls --verbose 1 (Join-Path $Destination 'ParamForge.exe') *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 30; throw ('Qt deployment failed: ' + $log) }
    $translations = Join-Path $Destination 'translations'
    New-Item -ItemType Directory -Path $translations -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $Tools.Qt 'translations\qtbase_ru.qm') -Destination $translations
    # App-local MSVC runtime permits starting without a separate redistributable installation.
    $crt = Get-ChildItem -LiteralPath (Join-Path $env:VCToolsRedistDir 'x64') -Directory -Filter 'Microsoft.VC*.CRT' | Select-Object -First 1
    if (-not $crt) { throw 'MSVC app-local runtime was not found.' }
    Get-ChildItem -LiteralPath $crt.FullName -Filter '*.dll' | Copy-Item -Destination $Destination
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'licenses') -Destination $Destination -Recurse
}
