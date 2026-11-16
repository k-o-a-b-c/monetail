param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SmokePath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$selectionPath = Join-Path $OutputPath 'selection'
& "$PSScriptRoot/run.ps1" -AppPath $AppPath -SmokePath $SmokePath -OutputPath $selectionPath -ListScenarios
$catalog = @(Get-Content "$selectionPath/scenario-list.json" -Raw | ConvertFrom-Json)
if ($catalog.Count -eq 0 -or @($catalog.name | Select-Object -Unique).Count -ne $catalog.Count) { throw 'シナリオ一覧が空または重複しています。' }
# 通常CIの全件結果が、一覧に登録されたシナリオを順番どおり全て実行していることを確認する。
$completed = @()
foreach ($suitePath in @("$OutputPath/transfer-editor", $OutputPath)) {
    $result = Get-Content "$suitePath/smoke-result.json" -Raw | ConvertFrom-Json
    if (-not $result.passed -or @($result.scenarios | Where-Object { -not $_.passed -or -not [double]::IsFinite($_.elapsedMilliseconds) -or $_.elapsedMilliseconds -lt 0 }).Count) { throw '全件結果の成功/計測が不正です。' }
    if (($result.scenarios | Measure-Object checks -Sum).Sum -ne @($result.checks).Count) { throw '確認件数とシナリオ集計が一致しません。' }
    $completed += $result.scenarios.name
}
if (($catalog.name -join ',') -cne ($completed -join ',')) { throw '通常CIのシナリオが欠落または並び替えられています。' }
foreach ($name in @('input-controls', 'transfer-reauthentication')) {
    $path = Join-Path $selectionPath $name
    & "$PSScriptRoot/run.ps1" -AppPath $AppPath -SmokePath $SmokePath -OutputPath $path -Scenario $name
    $result = Get-Content "$path/selected/smoke-result.json" -Raw | ConvertFrom-Json
    if (-not $result.passed -or @($result.scenarios).Count -ne 1 -or $result.scenarios[0].name -cne $name -or
        -not [double]::IsFinite($result.scenarios[0].elapsedMilliseconds) -or $result.scenarios[0].elapsedMilliseconds -lt 0) { throw '個別実行または計測結果が不正です。' }
}
$invalidPath = Join-Path $selectionPath 'invalid'
New-Item -ItemType Directory -Force -Path $invalidPath | Out-Null
$invalid = Start-Process -FilePath $SmokePath -ArgumentList @('--output', ('"' + $invalidPath + '"'), '--scenario', 'not-a-scenario') -PassThru -RedirectStandardError "$invalidPath/runtime-error.txt"
if (-not $invalid.WaitForExit(120000)) { $invalid.Kill(); throw '不正指定の拒否を確認できませんでした。' }
$result = Get-Content "$invalidPath/smoke-result.json" -Raw | ConvertFrom-Json
if ($invalid.ExitCode -eq 0 -or $result.passed -or @($result.scenarios).Count -ne 0 -or @($result.checks).Count -ne 0) { throw '不正指定でシナリオが実行されたか、成功扱いされています。' }
Write-Host 'Scenario listing, full coverage, individual execution, rejection and timing: OK'
