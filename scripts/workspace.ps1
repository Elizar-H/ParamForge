function Get-ParamForgeWorkRoot {
    if ($env:PARAMFORGE_WORK_ROOT) {
        $path = $env:PARAMFORGE_WORK_ROOT
    }
    else {
        $path = Join-Path (Split-Path -Parent $PSScriptRoot) 'build'
    }
    $resolved = [IO.Path]::GetFullPath($path)
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
    return $resolved
}
