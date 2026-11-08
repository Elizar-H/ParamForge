param([string]$OutputDirectory = '', [switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

function Get-ReleaseCommit {
    $gitRoot = & git -C $projectRoot rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot) -ne [IO.Path]::GetFullPath($projectRoot)) {
        throw 'Package releases from the ParamForge Git checkout. Building a source ZIP does not require Git.'
    }
    $changes = & git -C $projectRoot status --porcelain=v1 --untracked-files=all
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the source checkout.' }
    if ($changes) { throw 'Commit the source changes before packaging a release.' }
    $commit = & git -C $projectRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the source commit.' }
    return $commit
}

$sourceCommit = Get-ReleaseCommit
if (-not $SkipChecks) { & (Join-Path $PSScriptRoot 'check.ps1') -Ui }
. (Join-Path $PSScriptRoot 'native-tools.ps1')
$tools = Initialize-ParamForgeNativeTools
Invoke-ParamForgeBuild $tools $projectRoot
$version = [regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'CMakeLists.txt') -Raw), 'project\(ParamForge VERSION ([\d.]+)').Groups[1].Value
if (-not $version) { throw 'Cannot read the application version.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $tools.Work 'packages' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $tools.Work ('staging\package-' + [Guid]::NewGuid().ToString('N'))
$appName = 'ParamForge-' + $version + '-win-x64'
$appPath = Join-Path $stage $appName
Copy-ParamForgeApplication $tools $projectRoot $appPath
@"
ParamForge $version

Распакуйте архив целиком и запустите ParamForge.exe.
Windows x64.
Установка Qt и Visual Studio не требуется.

https://github.com/Elizar-H/ParamForge
"@ | Set-Content -LiteralPath (Join-Path $appPath 'Прочитать.txt') -Encoding UTF8
$applicationZip = Join-Path $OutputDirectory ($appName + '.zip')
Compress-Archive -LiteralPath $appPath -DestinationPath $applicationZip -Force
$unpacked = Join-Path $stage 'unpacked archive'
Expand-Archive -LiteralPath $applicationZip -DestinationPath $unpacked
& (Join-Path $PSScriptRoot 'test-startup.ps1') -Executable (Join-Path $unpacked ($appName + '\ParamForge.exe')) -OutputDirectory (Join-Path $stage 'startup-checks') -IsolateRuntime
$sourceName = 'ParamForge-' + $version + '-source'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceTemporaryZip = Join-Path $stage ($sourceName + '.zip')
& git -C $projectRoot archive --format=zip ('--prefix=' + $sourceName + '/') ('--output=' + $sourceTemporaryZip) $sourceCommit
if ($LASTEXITCODE -ne 0) { throw 'Cannot archive the source commit.' }
$sourceZip = Join-Path $OutputDirectory ($sourceName + '.zip')
Move-Item -LiteralPath $sourceTemporaryZip -Destination $sourceZip -Force
$sourceArchive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
try {
    foreach ($entry in @('CMakeLists.txt', 'CMakePresets.json', '.github/workflows/check.yml', 'src/resources/paramforge.ico', 'src/resources/paramforge.rc', 'tests/ui_tests.cpp')) {
        if (-not $sourceArchive.GetEntry($sourceName + '/' + $entry)) { throw ('Source archive is incomplete: ' + $entry) }
    }
    foreach ($entry in $sourceArchive.Entries) {
        $relativePath = $entry.FullName.Substring($sourceName.Length + 1)
        if ($relativePath -match '(^|/)(build|app|\.vs|\.git)/|\.(aps|user|suo)$|(^|/)CMakeUserPresets\.json$') {
            throw ('Local build data was committed to the source archive: ' + $relativePath)
        }
    }
}
finally { $sourceArchive.Dispose() }
if ((Get-ReleaseCommit) -ne $sourceCommit) { throw 'The source commit changed during packaging.' }
$checksums = foreach ($archive in @($applicationZip, $sourceZip)) {
    $hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)
}
$checksums | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Output ('Application: ' + $applicationZip)
Write-Output ('Source: ' + $sourceZip)
Write-Output ('Extracted checks: ' + (Join-Path $stage 'startup-checks'))
Write-Output ('Source commit: ' + $sourceCommit)
