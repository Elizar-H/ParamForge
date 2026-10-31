param(
    [string]$OutputDirectory = '',
    [switch]$SkipChecks
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$buildProperties = [xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)
$version = $buildProperties.Project.PropertyGroup.Version
$commit = & git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Для архива исходников требуется Git-репозиторий.' }
$changes = & git status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Сначала сохраните изменения в коммит: исходники и программа должны соответствовать одной версии.' }
if (-not $SkipChecks) {
    & (Join-Path $PSScriptRoot 'check.ps1')
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'release\packages' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stage = Join-Path $projectRoot ('release\stage-' + [Guid]::NewGuid().ToString('N'))
$appName = 'ParamForge-' + $version + '-win-x64'
$appPath = Join-Path $stage $appName
New-Item -ItemType Directory -Path $appPath -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& dotnet publish src/Kontur.Desktop/Kontur.Desktop.csproj -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json -p:NuGetAudit=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $appPath
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать автономную версию Windows x64.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'examples') -Destination (Join-Path $appPath 'examples') -Recurse
@"
ParamForge $version

Запуск: распакуйте архив в обычную папку и откройте ParamForge.exe.
Windows x64. Отдельная установка .NET, Python или Word не нужна.

Программа рассчитывает втулку с буртиком и формирует три файла:
маршрутную карту Word, операционную карту Word и УП выбранного ЧПУ.
В examples есть варианты для открытия через меню «Файл».
invalid-wall.json специально содержит ошибку для проверки диагностики.

Траектория восстановлена из УП. Это статический просмотр операции 010.
Прогоны в SinuTrain и на станке пока не выполнены.

Исходники и описание: https://github.com/Elizar-H/ParamForge
Версия исходников: $commit
"@ | Set-Content -LiteralPath (Join-Path $appPath 'Прочитать.txt') -Encoding utf8BOM
$applicationZip = Join-Path $OutputDirectory ($appName + '.zip')
$sourceZip = Join-Path $OutputDirectory ('ParamForge-' + $version + '-source.zip')
Compress-Archive -LiteralPath $appPath -DestinationPath $applicationZip -Force
& git archive --format=zip ('--prefix=ParamForge-' + $version + '-source/') ('--output=' + $sourceZip) $commit
if ($LASTEXITCODE -ne 0) { throw 'Не удалось подготовить архив исходников.' }
@($applicationZip, $sourceZip) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_)
} | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Output ('Commit: ' + $commit)
Write-Output ('Application: ' + $applicationZip)
Write-Output ('Source: ' + $sourceZip)
Write-Output ('Staging: ' + $appPath)
