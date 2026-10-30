param([switch]$Ui)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot

& dotnet build Kontur.sln -c Release --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Сборка завершилась с ошибкой.' }

& dotnet format whitespace Kontur.sln --no-restore --verify-no-changes --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Форматирование отличается от .editorconfig. Выполните dotnet format whitespace Kontur.sln --no-restore.' }

& dotnet format style Kontur.sln --no-restore --verify-no-changes --diagnostics IDE0011 IDE0005 IDE0040 --severity info --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Проверьте скобки, модификаторы доступа и неиспользуемые using.' }

& dotnet run --project src/Kontur.Tests -c Release --no-build -- --report qa/csharp-test-results.json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Проверки ядра завершились с ошибкой.' }
$coreReport = Get-Content -LiteralPath qa/csharp-test-results.json -Raw | ConvertFrom-Json
Write-Output ('Core: {0}/{1}' -f $coreReport.passedTests, $coreReport.totalTests)

if ($Ui) {
    $runName = 'ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $outputDirectory = Join-Path $projectRoot ('qa\' + $runName)
    & dotnet run --project src/Kontur.Desktop.Tests -c Release --no-build -- --smoke-output $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw ('Проверки интерфейса завершились с ошибкой: ' + $outputDirectory) }
    $uiReport = Get-Content -LiteralPath (Join-Path $outputDirectory 'ui-smoke.json') -Raw | ConvertFrom-Json -AsHashtable
    Write-Output ('UI: {0}/{1}' -f ($uiReport.checks | Where-Object passed).Count, $uiReport.checks.Count)
    Write-Output $outputDirectory
}
