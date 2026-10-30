param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_NOLOGO = '1'
$desktopProject = Join-Path $projectRoot 'src\Kontur.Desktop\Kontur.Desktop.csproj'
$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)
$releasePath = Join-Path $projectRoot ('release\ParamForge-' + $buildProperties.Project.PropertyGroup.Version)
& dotnet build $desktopProject -c Release --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать приложение.' }
if (-not $SkipTests) {
    & dotnet run --project src/Kontur.Tests -c Release -- --report qa/csharp-test-results.json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Проверки завершились с ошибкой.' }
    $testReport = Get-Content -LiteralPath qa/csharp-test-results.json -Raw | ConvertFrom-Json
    Write-Output ("Tests: {0}/{1}" -f $testReport.passedTests,$testReport.totalTests)
}
& dotnet publish $desktopProject -c Release --no-restore --no-self-contained -o $releasePath
if ($LASTEXITCODE -ne 0) { throw 'Не удалось подготовить локальный комплект.' }
Write-Output (Join-Path $releasePath 'ParamForge.exe')
