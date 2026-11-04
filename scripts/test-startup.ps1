param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$OutputDirectory = '',
    [switch]$IsolateRuntime,
    [int]$Monitor = -1
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'workspace.ps1')
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Get-ParamForgeWorkRoot) ('qa\startup-' + [Guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens.Count
$reports = @()
$monitorIndex = if ($Monitor -ge 0) { $Monitor } else { [Math]::Min(1, $screens - 1) }
if ($monitorIndex -ge $screens) { throw 'Requested QA monitor was not found.' }
foreach ($monitor in @($monitorIndex)) {
    $folder = Join-Path $OutputDirectory ('monitor-' + $monitor)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.WorkingDirectory = $OutputDirectory
    $start.UseShellExecute = $false
    # The app must actually show its child windows; a hidden launch misses DPI startup failures.
    $start.Arguments = '--check-startup "' + $folder + '" --monitor ' + $monitor
    if ($IsolateRuntime) {
        $start.EnvironmentVariables['PATH'] = $env:SystemRoot + '\System32;' + $env:SystemRoot
        foreach ($key in @('QT_PLUGIN_PATH', 'QT_QPA_PLATFORM_PLUGIN_PATH', 'QML2_IMPORT_PATH', 'QML_IMPORT_PATH', 'QT_SCALE_FACTOR', 'QT_QPA_PLATFORM')) {
            $start.EnvironmentVariables.Remove($key)
        }
    }
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(45000)) {
            $process.Kill()
            throw ('Startup timed out on monitor ' + $monitor)
        }
        if ($process.ExitCode -ne 0) { throw ('Startup failed on monitor ' + $monitor + '. See ' + $folder) }
        $report = Get-Content -LiteralPath (Join-Path $folder 'startup.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $report.ok) { throw ('Startup report failed: ' + $folder) }
        $reports += $report
        Write-Output ('Startup: monitor {0}, DPI {1}, OK' -f $monitor, $report.dpi)
    }
    finally {
        $process.Dispose()
    }
}
$reports | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'startup-results.json') -Encoding UTF8
