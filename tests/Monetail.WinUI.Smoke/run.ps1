param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$SmokePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Capture
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

$arguments = @('--output', ('"' + $OutputPath + '"'))
if ($Capture) { $arguments += '--capture' }
$smoke = Start-Process -FilePath $SmokePath -ArgumentList $arguments -PassThru -RedirectStandardError "$OutputPath/runtime-error.txt"
if (-not $smoke.WaitForExit(60000)) {
    $smoke.Kill()
    throw 'WinUIスモークテストが完了しませんでした。'
}
if ($smoke.ExitCode -ne 0) {
    if (Test-Path "$OutputPath/smoke-error.txt") { Get-Content "$OutputPath/smoke-error.txt" }
    if (Test-Path "$OutputPath/runtime-error.txt") { Get-Content "$OutputPath/runtime-error.txt" }
    throw "WinUIスモークテストに失敗しました: $($smoke.ExitCode)"
}
$result = Get-Content "$OutputPath/smoke-result.json" -Raw | ConvertFrom-Json
if (-not $result.passed) { throw 'WinUIスモークテストの成功結果がありません。' }
$result.checks | ForEach-Object { Write-Host "OK: $_" }
