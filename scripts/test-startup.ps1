param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$OutputDirectory = '',
    [switch]$IsolateRuntime
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) ('qa\startup-' + [Guid]::NewGuid().ToString('N'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens.Count
$reports = @()
for ($monitor = 0; $monitor -lt $screens; $monitor++) {
    $folder = Join-Path $OutputDirectory ('monitor-' + $monitor)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.WorkingDirectory = $OutputDirectory
    $start.UseShellExecute = $false
    # The app must actually show its child windows; a hidden launch misses DPI startup failures.
    $start.Arguments = '--check-startup "' + $folder + '" --monitor ' + $monitor
    if ($IsolateRuntime) {
        $emptyRuntime = Join-Path $OutputDirectory 'no-installed-runtime'
        New-Item -ItemType Directory -Path $emptyRuntime -Force | Out-Null
        $start.EnvironmentVariables['DOTNET_ROOT'] = $emptyRuntime
        $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $emptyRuntime
        $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        $start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $folder 'runtime-cache'
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
