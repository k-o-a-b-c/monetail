# Monetail: Windows SandboxでセルフホストCIを起動する

この一式は、k-o-a-b-c/monetail-legacy用のセットアップ例です。ホストPCのランナーを置き換えるため、Windows Sandbox内へツールを導入します。アプリ本体やリポジトリは変更しません。Windows上での実行確認は未実施です。

## 1. 最初の一度だけ

Windows Proの「Windowsの機能の有効化または無効化」で「Windows サンドボックス」を有効にし、指示があればPCを再起動します。仮想化機能が必要です。

ZIPを展開し、次の配置にしてください。フォルダーが二重にならないよう注意します。

```text
C:\MonetailSandbox\
  Monetail-CI.wsb
  README.md
  bootstrap\
    Setup.ps1
```

bootstrapフォルダーにはセットアップ用ファイルだけを置きます。家計簿DB、個人ファイル、SSH鍵、PAT、GitHubの登録トークンは置かないでください。このフォルダーのみ読み取り専用でSandboxへ公開されます。

WSBのMemoryInMBは6144（6GB）です。PC全体のRAMが16GB以上ある構成を推奨します。RAMが少ない場合は4096等へ変更できますが、Build Tools導入やテストがメモリ不足で失敗する場合があります。ツール・展開先のため、ホストのシステムドライブに十分な空き容量も必要です。

## 2. GitHubの作業ブランチでworkflowを変更する

mainへ直接コミットせず、ci/use-self-hosted-windows等の作業ブランチで `.github/workflows/tests.yml` を編集します。

3ジョブすべてを以下の形にします。既存stepsは維持します。

```yaml
jobs:
  receipt-read-performance:
    runs-on: [self-hosted, Windows, X64, sandbox]
    timeout-minutes: 30
    steps:
      # 既存のstepsを残す
```

同様にcalculations-and-migration、windows-persistence-and-uiも変更します。sandboxラベルは、他のWindowsランナーと区別するために必須です。

各actions/setup-dotnet@v4は次の形にします。SDKとランタイムの導入先をセッション内で共通化します。

```yaml
      - uses: actions/setup-dotnet@v4
        env:
          DOTNET_INSTALL_DIR: C:\CI\dotnet
        with:
          dotnet-version: 8.0.x
```

Pythonの実行行はWindows向けのpythonを使います。

```yaml
      - run: python -m unittest discover -s tests -p 'test_*migration.py' -v
```

変更を作業ブランチへコミットします。ホストPCのrun.cmdは停止し、以後はSandbox専用のランナーへ割り当てます。各ジョブのstepsは上の短い説明例で置き換えないでください。

## 3. 毎回の起動

1. ホストPCのランナーをCtrl+Cで停止します。
2. `C:\MonetailSandbox\Monetail-CI.wsb` をダブルクリックします。
3. Sandbox内でGit、Python、Visual Studio 2022 Build Toolsと.NET Framework 4.8 targeting packが導入されます。署名またはSHA-256を確認してから使用します。環境・回線によって数十分と数GB以上のダウンロードが必要です。Sandbox内での昇格確認が出た場合はSandboxのウィンドウ内で対応します。
4. ジョブ数の入力が出たら通常はEnterで3件です。pushとpull_requestの両方で同じコミットを実行する場合は6件などにします。
5. 準備が終わってから、ホスト側ブラウザーでGitHubのリポジトリ設定を開きます。
   `Settings → Actions → Runners → New self-hosted runner → Windows / X64`
6. 表示される `config.cmd ... --token TOKEN` のTOKEN部分だけをSandboxへ手入力します。クリップボード共有は無効です。入力は画面へ表示されません。GitHubのパスワード、PAT、コマンド全体は入力しません。登録トークンは1時間で期限切れになるため、ツール導入後に取得してください。
7. 待機中の新しいworkflow、または再実行したworkflowがrunnerを使います。GitHubのジョブ詳細で `monetail-sandbox-...` というRunner名を確認します。

登録画面: https://github.com/k-o-a-b-c/monetail-legacy/settings/actions/runners

ランナーは1ジョブごとのephemeral登録です。1件処理するとGitHubから登録解除され、次のジョブ用の新しいランナーを登録します。同じSandboxセッション内でツールやOSは共有されるので、ジョブごとにOSが初期化される意味ではありません。

SDK 8はworkflowのactions/setup-dotnetが自動導入します。NuGetとMSBuildのPATH設定も既存workflowのアクションを利用します。Runnerが終了してもテスト成功とは限らないため、最終的な成否はGitHub Actionsで確認します。

## 4. 終了と停止

必要なジョブを実行したらSandboxを閉じます。内部のツール・チェックアウト・一時DBは破棄され、ホストPCに開発ツールは導入されません。

途中で止める場合はGitHubでCancel workflowを押し、Sandboxを閉じます。各インストーラーと各ジョブには30分の上限、各runner待機・実行には45分の上限があります。VMの隔離はCPU負荷がゼロになる保証ではありません。

予定よりジョブ数が少ないと、次のrunnerが待機を続ける場合があります。必要なジョブが終わったらそのままSandboxを閉じてください。待機状態で閉じたephemeral runnerはGitHub側に一時的にOfflineとして残る場合があります。

## トラブル時

- インストーラーが再起動を要求した場合はCIを開始せず停止します。Windows 11 22H2以降はSandbox内部での再起動にデータ保持の対応がありますが、Sandboxを閉じると消えます。内部の再起動後、必要に応じてWindows PowerShellで次を実行してください。

  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Bootstrap\Setup.ps1
  ```

  tools-ready.txtは全ツールの導入成功後にだけ作られます。再起動要求時点で未作成なら、導入を再試行して状態を確認します。
- 登録トークンが期限切れならSandboxを閉じずに同じコマンドで再起動し、新しい登録トークンを入力します。ツール導入済みなら再導入を省きます。
- GitHub APIのレート制限、署名確認失敗、ダウンロードエラー、Build Toolsの導入失敗では停止します。検証を省略して続行しないでください。
- Runner配布ZIPのGitHub API metadataにSHA-256 digestがない場合も停止します。手動でハッシュ確認するか、公式に配布情報が提供されるまで待ちます。

## 隔離の範囲

- ホストから共有するのはbootstrapフォルダーだけで、読み取り専用です。共有フォルダー内の内容はCIコードからも読めるため、秘密情報を置かないでください。
- ホストのクリップボード、音声入力、映像入力、プリンター、vGPUは無効です。LogonCommandの実行許可はSandbox内のPowerShellプロセスだけに作用します。
- GitHubやパッケージ取得に必要なネットワークは有効です。これは家庭内LANへの到達を自動的に禁止する設定ではありません。ホストへの影響を絶対にゼロにする保証もありません。
- Windows Sandbox用の端末にGitHubアカウントでブラウザーログインする必要はありません。GitHub操作はホストのブラウザーで行います。
- このスクリプトはSandbox内でBuild Toolsの導入権限を使います。PC本体での実行を防ぐため、既定ユーザー名と配置先を確認してから動きます。
- self-hostedの実行時間料金は無料でも、GitHub Actionsのartifact保存等には別途無料枠・制限があります。

## 公式資料

- Windows Sandbox設定: https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file
- Build Tools component IDs: https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools?view=vs-2022
- PythonのWindows installer: https://docs.python.org/3/using/windows.html
- Gitの無人導入: https://github.com/git-for-windows/git-for-windows.github.io/blob/main/content/silent-or-unattended-installation.md
- ランナー登録: https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners
- Ephemeral runners: https://docs.github.com/en/actions/reference/runners/self-hosted-runners
