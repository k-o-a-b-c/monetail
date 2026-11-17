param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SmokePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Capture,
    [switch]$RepeatParticipantRecovery,
    [string]$Scenario,
    [switch]$ListScenarios
)
$ErrorActionPreference = 'Stop'
if ($PSBoundParameters.ContainsKey('Scenario') -and [string]::IsNullOrWhiteSpace($Scenario)) { throw 'シナリオ名を指定してください。' }
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

if ($ListScenarios) {
    if ($Scenario) { throw '-ListScenariosと-Scenarioは併用できません。' }
    $listPath = Join-Path $OutputPath 'scenario-list.json'
    Remove-Item $listPath -ErrorAction SilentlyContinue
    $list = Start-Process -FilePath $SmokePath -ArgumentList @('--output', ('"' + $OutputPath + '"'), '--list-scenarios') -PassThru -RedirectStandardError "$OutputPath/runtime-error.txt"
    if (-not $list.WaitForExit(120000)) { $list.Kill(); throw 'シナリオ一覧の取得が完了しませんでした。' }
    if ($list.ExitCode -ne 0) { throw "シナリオ一覧を取得できませんでした: $($list.ExitCode)" }
    Get-Content $listPath -Raw | ConvertFrom-Json | Format-Table name, suite
    return
}

# 製品のエントリーポイントと自己完結した配置で起動することも確認する。
$app = Start-Process -FilePath $AppPath -PassThru -RedirectStandardError "$OutputPath/app-error.txt"
try {
    $until = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 100
        $app.Refresh()
        if ($app.HasExited) { throw "製品アプリが起動直後に終了しました: $($app.ExitCode)" }
    } until ($app.MainWindowHandle -ne 0 -or [DateTime]::UtcNow -gt $until)
    if ($app.MainWindowHandle -eq 0) { throw '製品のウィンドウを確認できませんでした。' }
    Write-Host "Product startup OK: $($app.MainWindowTitle)"
} finally {
    if (-not $app.HasExited) {
        $app.CloseMainWindow() | Out-Null
        if (-not $app.WaitForExit(5000)) { $app.Kill() }
    }
}

# 新規入力の検証と既存全画面の検証を分離し、各プロセスの上限と全確認を維持する。
$suites = if ($Scenario) { @('selected') } else { @('transfer-editor', 'existing') }
foreach ($suite in $suites) {
    $suiteOutput = if ($suite -eq 'existing') { $OutputPath } else { Join-Path $OutputPath $suite }
    New-Item -ItemType Directory -Force -Path $suiteOutput | Out-Null
    $arguments = @('--output', ('"' + $suiteOutput + '"'))
    if ($Scenario) { $arguments += @('--scenario', $Scenario) }
    elseif ($suite -eq 'transfer-editor') { $arguments += '--transfer-editor-only' }
    else { $arguments += '--existing-only' }
    if ($Capture -and $suite -ne 'transfer-editor') { $arguments += '--capture' }
    if ($RepeatParticipantRecovery -and $suite -ne 'transfer-editor') { $arguments += '--repeat-participant-recovery' }
    $resultPath = Join-Path $suiteOutput 'smoke-result.json'
    Remove-Item $resultPath, "$suiteOutput/smoke-error.txt" -ErrorAction SilentlyContinue
    $smoke = Start-Process -FilePath $SmokePath -ArgumentList $arguments -PassThru -RedirectStandardError "$suiteOutput/runtime-error.txt"
    if (-not $smoke.WaitForExit(120000)) {
        $smoke.Kill()
        throw "WinUIスモークテストが完了しませんでした: $suite"
    }
    $result = if (Test-Path $resultPath) { Get-Content $resultPath -Raw | ConvertFrom-Json } else { $null }
    if ($result) {
        foreach ($timing in $result.scenarios) {
            $status = if ($timing.passed) { 'PASS' } else { 'FAIL' }
            Write-Host ("Scenario {0}: {1}, {2:F3} ms, {3} checks" -f $timing.name, $status, $timing.elapsedMilliseconds, $timing.checks)
        }
    }
    if ($smoke.ExitCode -ne 0) {
        if (Test-Path "$suiteOutput/smoke-error.txt") { Get-Content "$suiteOutput/smoke-error.txt" }
        if (Test-Path "$suiteOutput/runtime-error.txt") { Get-Content "$suiteOutput/runtime-error.txt" }
        throw "WinUIスモークテストに失敗しました: $suite / $($smoke.ExitCode)"
    }
    if (-not $result -or -not $result.passed) { throw "WinUIスモークテストの成功結果がありません: $suite" }
    Write-Host "WinUI suite OK: $suite"
    $result.checks | ForEach-Object { Write-Host "OK: $_" }
}
