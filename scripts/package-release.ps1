param([string]$OutputDirectory = '', [switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
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

Распакуйте архив и запустите ParamForge.exe.
Windows x64.

https://github.com/Elizar-H/ParamForge
"@ | Set-Content -LiteralPath (Join-Path $appPath 'Прочитать.txt') -Encoding UTF8
$applicationZip = Join-Path $OutputDirectory ($appName + '.zip')
Compress-Archive -LiteralPath $appPath -DestinationPath $applicationZip -Force
$unpacked = Join-Path $stage 'unpacked archive'
Expand-Archive -LiteralPath $applicationZip -DestinationPath $unpacked
& (Join-Path $PSScriptRoot 'test-startup.ps1') -Executable (Join-Path $unpacked ($appName + '\ParamForge.exe')) -OutputDirectory (Join-Path $stage 'startup-checks') -IsolateRuntime
$sourceName = 'ParamForge-' + $version + '-source'
$sourcePath = Join-Path $stage $sourceName
New-Item -ItemType Directory -Path $sourcePath -Force | Out-Null
foreach ($entry in @('.github', '.clang-format', '.editorconfig', '.gitignore', 'AGENTS.md',
        'CMakeLists.txt', 'CMakePresets.json', 'CONTRIBUTING.md', 'README.md', 'docs', 'licenses', 'scripts', 'src', 'tests', 'Запустить.cmd')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $entry) -Destination $sourcePath -Recurse -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceTemporaryZip = Join-Path $stage ($sourceName + '.zip')
$sourceOutput = [IO.Compression.ZipFile]::Open($sourceTemporaryZip, [IO.Compression.ZipArchiveMode]::Create, [Text.Encoding]::UTF8)
try {
    foreach ($file in Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force) {
        # .NET Framework otherwise writes backslashes in ZIP entry names on Windows PowerShell 5.1.
        $relativePath = $file.FullName.Substring($sourcePath.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceOutput, $file.FullName,
            ($sourceName + '/' + $relativePath), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $sourceOutput.Dispose() }
$sourceZip = Join-Path $OutputDirectory ($sourceName + '.zip')
Move-Item -LiteralPath $sourceTemporaryZip -Destination $sourceZip -Force
$sourceArchive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
try {
    foreach ($entry in @('CMakeLists.txt', 'CMakePresets.json', '.github/workflows/check.yml', 'src/resources/paramforge.ico', 'src/resources/paramforge.rc', 'tests/ui_tests.cpp')) {
        if (-not $sourceArchive.GetEntry($sourceName + '/' + $entry)) { throw ('Source archive is incomplete: ' + $entry) }
    }
}
finally { $sourceArchive.Dispose() }
$checksums = foreach ($archive in @($applicationZip, $sourceZip)) {
    $hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)
}
$checksums | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Output ('Application: ' + $applicationZip)
Write-Output ('Source: ' + $sourceZip)
Write-Output ('Extracted checks: ' + (Join-Path $stage 'startup-checks'))
