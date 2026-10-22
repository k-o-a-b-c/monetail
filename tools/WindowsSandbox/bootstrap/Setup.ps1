param(
    [ValidateRange(1, 12)] [int] $MaxJobs = 3,
    [ValidateRange(10, 120)] [int] $RunnerMinutes = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# PC本体から誤って起動しないため、Sandbox内の既定ユーザーと共有先を確認する。
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or $PSScriptRoot -ne 'C:\Bootstrap') {
    throw 'Run this script ONLY through Monetail-CI.wsb inside Windows Sandbox.'
}

# Build Toolsの導入に必要な昇格はSandbox内だけで行う。
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = '-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -File C:\Bootstrap\Setup.ps1 -MaxJobs {0} -RunnerMinutes {1}' -f $MaxJobs, $RunnerMinutes
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    return
}

$root = 'C:\CI'
$downloads = Join-Path $root 'downloads'
$repoUrl = 'https://github.com/k-o-a-b-c/monetail-legacy'
$marker = Join-Path $root 'tools-ready.txt'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'monetail-sandbox-bootstrap'; 'Accept' = 'application/vnd.github+json' }

function Save-Download {
    param([string] $Url, [string] $Destination)
    if (-not $Url.StartsWith('https://')) { throw 'HTTPS download required.' }
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing -TimeoutSec 900
}

function Assert-Signature {
    param([string] $Path, [string] $PublisherPattern)
    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
        throw "Invalid or unavailable Authenticode signature: $Path"
    }
    if ($PublisherPattern -and $signature.SignerCertificate.Subject -notmatch $PublisherPattern) {
        throw "Unexpected publisher: $Path"
    }
}

function Assert-Hash {
    param([string] $Path, [string] $Expected, [string] $Algorithm = 'SHA256')
    if ((Get-FileHash -Path $Path -Algorithm $Algorithm).Hash -ine $Expected) {
        throw "Checksum mismatch: $Path"
    }
}

function Get-ReleaseAsset {
    param([string] $Repository, [string] $Pattern)
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers $headers -TimeoutSec 120
    $asset = @($release.assets | Where-Object { $_.name -match $Pattern })
    if ($asset.Count -ne 1) { throw "Expected exactly one release asset: $Repository / $Pattern" }
    return $asset[0]
}

function Save-GitHubAsset {
    param($Asset, [string] $Destination, [switch] $RequireHash)
    Save-Download -Url $Asset.browser_download_url -Destination $Destination
    $digest = $null
    if ($Asset.PSObject.Properties.Name -contains 'digest') { $digest = $Asset.digest }
    if ($digest -and $digest -match '^sha256:([0-9a-fA-F]{64})$') {
        Assert-Hash -Path $Destination -Expected $Matches[1]
    } elseif ($RequireHash) {
        throw 'GitHub did not supply a SHA-256 digest. Stop; do not run an unverified runner archive.'
    }
}

function Run-Installer {
    param([string] $Path, [string[]] $Arguments)
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -PassThru
    if (-not $process.WaitForExit(30 * 60 * 1000)) {
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        throw "Installer exceeded 30 minutes: $Path. Close Sandbox if an installer is still running."
    }
    if ($process.ExitCode -eq 3010) {
        throw 'Installer requires a restart INSIDE Sandbox. Do not close Sandbox. See README.md.'
    }
    if ($process.ExitCode -ne 0) { throw "Installer failed: $Path / exit $($process.ExitCode)" }
}

function Install-Python {
    # 3.14系列で、公式Windowsインストーラーが存在する最新の安定版を選ぶ。
    $index = (Invoke-WebRequest -Uri 'https://www.python.org/ftp/python/' -UseBasicParsing -TimeoutSec 120).Content
    $versions = @([regex]::Matches($index, 'href="(3\.14\.\d+)/"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object { [version] $_ } -Descending -Unique)
    if ($versions.Count -eq 0) { throw 'Python 3.14 release list unavailable.' }
    $pythonUrl = $null
    foreach ($version in $versions) {
        $candidate = "https://www.python.org/ftp/python/$version/python-$version-amd64.exe"
        try {
            Invoke-WebRequest -Uri $candidate -Method Head -UseBasicParsing -TimeoutSec 120 | Out-Null
            $pythonUrl = $candidate
            break
        } catch {
            $response = $_.Exception.Response
            if ($null -eq $response -or [int] $response.StatusCode -ne 404) { throw }
        }
    }
    if (-not $pythonUrl) { throw 'No stable Python Windows installer found.' }
    $installer = Join-Path $downloads 'python.exe'
    Save-Download -Url $pythonUrl -Destination $installer
    Assert-Signature -Path $installer -PublisherPattern 'Python Software Foundation'
    Run-Installer -Path $installer -Arguments @('/quiet', 'InstallAllUsers=0', 'TargetDir=C:\CI\Python', 'PrependPath=0', 'Include_launcher=0', 'Include_pip=0', 'Include_test=0', 'Include_doc=0', 'AssociateFiles=0', 'Shortcuts=0')
}

function Install-Git {
    $asset = Get-ReleaseAsset -Repository 'git-for-windows/git' -Pattern '^Git-.*-64-bit\.exe$'
    $installer = Join-Path $downloads 'git.exe'
    Save-GitHubAsset -Asset $asset -Destination $installer
    Assert-Signature -Path $installer -PublisherPattern ''
    Run-Installer -Path $installer -Arguments @('/VERYSILENT', '/NORESTART', '/SP-', '/SUPPRESSMSGBOXES', '/DIR=C:\CI\Git', '/o:PathOption=Cmd', '/o:UseCredentialManager=Disabled')
}

function Install-BuildTools {
    $installer = Join-Path $downloads 'vs_buildtools.exe'
    Save-Download -Url 'https://aka.ms/vs/17/release/vs_buildtools.exe' -Destination $installer
    Assert-Signature -Path $installer -PublisherPattern 'Microsoft Corporation'
    Run-Installer -Path $installer -Arguments @('--quiet', '--wait', '--norestart', '--nocache', '--installPath', 'C:\CI\BuildTools', '--add', 'Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools', '--includeRecommended', '--add', 'Microsoft.Net.Component.4.8.TargetingPack')
}

function Read-RegistrationToken {
    Write-Host ''
    Write-Host 'On the HOST browser: Settings > Actions > Runners > New self-hosted runner > Windows / X64.'
    Write-Host 'Enter ONLY the short-lived token after --token in config.cmd. Do not enter a PAT or password.'
    Write-Host 'Clipboard sharing is disabled; type the token in this Sandbox window.'
    $secret = Read-Host 'Registration token (hidden)' -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ([string]::IsNullOrWhiteSpace($plain) -or $plain -match '\s') { throw 'Enter the token only, without spaces.' }
        return $plain
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        $secret.Dispose()
    }
}

try {
    Write-Host 'Windows Sandbox only: installing CI prerequisites. This can take tens of minutes.'
    if (-not (Test-Path $marker)) {
        Install-Git
        Install-Python
        Install-BuildTools
        Set-Content -Path $marker -Value 'Installed inside Windows Sandbox.' -Encoding ASCII
    }

    # 起動したランナーへ引き継ぐ。ホストPCの環境変数や実行ポリシーは変更しない。
    $env:Path = "C:\CI\Git\cmd;C:\CI\Python;C:\CI\BuildTools\MSBuild\Current\Bin;" + $env:Path
    $env:DOTNET_INSTALL_DIR = 'C:\CI\dotnet'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force
    & git --version
    if ($LASTEXITCODE -ne 0) { throw 'Git validation failed.' }
    & python -c 'import sqlite3, unittest, sys; print(sys.version); print(sqlite3.sqlite_version)'
    if ($LASTEXITCODE -ne 0) { throw 'Python validation failed.' }
    if (-not (Test-Path 'C:\CI\BuildTools\MSBuild\Current\Bin\MSBuild.exe')) { throw 'MSBuild not installed.' }
    $framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll'
    if (-not (Test-Path $framework)) { throw '.NET Framework 4.8 targeting pack not installed.' }
    Write-Host '.NET 8 SDK will be installed automatically by actions/setup-dotnet in the workflow.'

    $runnerAsset = Get-ReleaseAsset -Repository 'actions/runner' -Pattern '^actions-runner-win-x64-[0-9.]+\.zip$'
    $runnerZip = Join-Path $downloads 'actions-runner.zip'
    Save-GitHubAsset -Asset $runnerAsset -Destination $runnerZip -RequireHash

    Write-Host ''
    Write-Host 'Workflow MUST use runs-on: [self-hosted, Windows, X64, sandbox] and timeout-minutes: 30.'
    Write-Host 'Stop the runner on the host PC. This setup only registers the dedicated sandbox label.'
    $jobInput = Read-Host "Maximum jobs this session (1-12; Enter=$MaxJobs; push+PR may require 6)"
    if ($jobInput) {
        $count = 0
        if (-not [int]::TryParse($jobInput, [ref] $count) -or $count -lt 1 -or $count -gt 12) { throw 'Job count must be 1-12.' }
        $MaxJobs = $count
    }

    $token = Read-RegistrationToken
    $session = [guid]::NewGuid().ToString('N').Substring(0, 8)
    for ($jobNumber = 1; $jobNumber -le $MaxJobs; $jobNumber++) {
        $runnerPath = Join-Path $root "runner-$session-$jobNumber"
        Expand-Archive -Path $runnerZip -DestinationPath $runnerPath
        Push-Location $runnerPath
        try {
            Write-Host "Registering disposable runner $jobNumber / $MaxJobs ..."
            & .\config.cmd --unattended --ephemeral --url $repoUrl --token $token --name "monetail-sandbox-$session-$jobNumber" --labels sandbox --work _work
            if ($LASTEXITCODE -ne 0) { throw 'Runner registration failed. If the token expired, rerun Setup.ps1 inside Sandbox and get a new token.' }
            Write-Host 'Waiting for one job. You may close Windows Sandbox to stop the entire session.'
            $process = Start-Process -FilePath $env:ComSpec -ArgumentList @('/d', '/c', (Join-Path $runnerPath 'run.cmd')) -NoNewWindow -PassThru
            if (-not $process.WaitForExit($RunnerMinutes * 60 * 1000)) {
                & taskkill.exe /PID $process.Id /T /F | Out-Null
                throw "Runner exceeded $RunnerMinutes minutes including queue wait. Cancel the workflow and close Sandbox."
            }
            if ($process.ExitCode -ne 0) { throw "Runner stopped with exit $($process.ExitCode)." }
        } finally {
            Pop-Location
        }
    }
    Write-Host 'Runner batch finished. Check GitHub Actions for actual test/build results, then CLOSE Windows Sandbox.'
} catch {
    Write-Host ''
    Write-Host ('STOPPED: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Do not apply Sandbox installation commands or execution-policy changes to the host PC.'
} finally {
    $token = $null
}
