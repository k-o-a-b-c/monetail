param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SmokePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Capture,
    [switch]$RepeatParticipantRecovery
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

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
foreach ($suite in @('transfer-editor', 'existing')) {
    $suiteOutput = if ($suite -eq 'existing') { $OutputPath } else { Join-Path $OutputPath $suite }
    New-Item -ItemType Directory -Force -Path $suiteOutput | Out-Null
    $arguments = @('--output', ('"' + $suiteOutput + '"'))
    if ($suite -eq 'transfer-editor') { $arguments += '--transfer-editor-only' }
    if ($Capture -and $suite -eq 'existing') { $arguments += '--capture' }
    if ($RepeatParticipantRecovery -and $suite -eq 'existing') { $arguments += '--repeat-participant-recovery' }
    $smoke = Start-Process -FilePath $SmokePath -ArgumentList $arguments -PassThru -RedirectStandardError "$suiteOutput/runtime-error.txt"
    if (-not $smoke.WaitForExit(120000)) {
        $smoke.Kill()
        throw "WinUIスモークテストが完了しませんでした: $suite"
    }
    if ($smoke.ExitCode -ne 0) {
        if (Test-Path "$suiteOutput/smoke-error.txt") { Get-Content "$suiteOutput/smoke-error.txt" }
        if (Test-Path "$suiteOutput/runtime-error.txt") { Get-Content "$suiteOutput/runtime-error.txt" }
        throw "WinUIスモークテストに失敗しました: $suite / $($smoke.ExitCode)"
    }
    $result = Get-Content "$suiteOutput/smoke-result.json" -Raw | ConvertFrom-Json
    if (-not $result.passed) { throw "WinUIスモークテストの成功結果がありません: $suite" }
    Write-Host "WinUI suite OK: $suite"
    $result.checks | ForEach-Object { Write-Host "OK: $_" }
}
