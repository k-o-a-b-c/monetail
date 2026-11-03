# テストの構成・実行と結果

件数・実行方法・採用結果の正本。旧VB.NET版の検証と、新版WinUI 3・Clientの検証を区別する。過去の詳細は[CI検証記録](ci-verification-records.md)と凍結済みの[履歴](history.md)、旧Windows版の手動確認は[専用チェックリスト](legacy-manual-checks.md)を参照する。実装のない画面を検証済みとは扱わない。

| 対象 | 件数 |
| --- | ---: |
| 旧版の計算 | 35 |
| SQLite移行（税端数・更新版） | 10 |
| 旧版Windowsの実SQLite・画面・起動設定 | 98 |
| ASP.NET Core API | 446 |
| Windows Client（接続・認証・保存・家計簿・人物・補完） | 226 |
| Windowsの実DPAPI・保存 | 3 |
| WinUI 3の製品起動・画面スモーク（1シナリオ） | 1 |
| CIの実行対象選択・成功結果の再利用 | 41 |
| 合計 | 860 |

## 検証する範囲

- Clientは合成HTTPと一時保存先を使う。接続確認、OAuth/ループバック、セッション復元、更新と失効、別プロセスの認証変更、設定/トークン保存、家計簿の起動先・作成・切替、人物のCRUD/順保存/競合/再送を確認する。日付・時刻は補完期間・世紀選択・空欄・不正入力・全角・区切り・地域設定を検証する。
- StorageはWindowsのCurrentUser DPAPIを実際に使い、暗号化保存と新しいStoreでの復元、破損時の認証破棄と接続設定保持、平文の拒否を確認する。UIへの依存はない。
- WinUI 3は製品EXEを起動し、同じMainWindowのXAML・各partial・ClientConnection・PersonRowを使う別のスモークAppで合成データを検証する。ホーム、左右の人物管理、リサイズとテーマに加え、接続設定・ブラウザー起動の失敗と再開・認証キャンセル・実ループバックからの合成認可応答・保存ログインの確認と失効・家計簿作成の再送と選択・接続先変更・ログアウト成功／失敗を1シナリオとして確認する。製品Appから起動時の接続・復元を開始し、遅い接続での同じWindow内の待機表示・完了後の停止・タイムアウトも確認する。実データや保存済みの接続先/トークンは使用しない。 人物の削除確認では参照拒否・競合・通信失敗/応答喪失・降格/除外・認証失効を確認する。人物編集では保存・再送・競合・権限喪失・ContentDialogの保存／取消と背後の移動抑止、同一アカウントの再ログイン後の入力/版/要求IDの復元、別アカウント・別APIへの非引継ぎも検証する。入力部品は同じハンドラーと実コントロールのフォーカスで確認し、物理キー注入や実IME変換の検証とは区別する。
- APIは実HTTP・合成アカウント・隔離SQLiteを使い、認証・現在の参加/役割・保存失敗の巻き戻し・同時操作・FK・再送・競合・招待の状態と消費・共有回数枠を検証する。CookieとWindows Bearerを区別する。
- 旧VB.NET版の計算・DB・画面・移行・性能は既存のテスト構成を維持する。実データ・利用者環境への適用を自動検証済みとは扱わない。

## 登録モード設定の追加検証（未実行）

`RegistrationSettingsTests`に13ケースを追加した。既定招待制・有効値・不正値・通常API起動時の設定固定・不正値の起動拒否・DB非初期化を対象とする。上表の件数は新規追加分を含む構成数であり、**新規13ケースのCI成功は未確認**。以下の採用済み結果と混同しない。

## 採用結果

人物の共通順設定と認証後の復元はソース`de2e0aebec793c5d8f5d60e90c69295bedf217e2`を[CI 41605779743](https://github.com/k-o-a-b-c/monetail/actions/runs/41605779743)で確認。判定41件とWinUIスモーク1シナリオが成功し、製品/スモークのReleaseビルドは警告・エラー0。共通順と通常の本人先頭表示、移動と取消の通信なし、全IDの一括保存、競合時の最新一覧への直接置換、通信失敗/応答喪失、権限変更を検証した。さらに同じ本人での再ログイン後の未保存順・元の全体版の維持と非自動保存、認証待ち中の人物増減、降格/除外後の保存禁止、未参加時のGET抑止、別アカウント/接続先への非引き継ぎも成功。API・Client・Storage・旧版等は変更がなく再実行していない。構成合計847件は変わらず、スモーク内の確認項目を個別のテスト件数に加算しない。実キー・IME・実端末DPI・実ブラウザーのパスキーは未検証。

### 過去の確認（索引）

以下は**当時の個別検証**であり、現在のテスト件数や全機能の成功を意味しない。コミットの完全なSHA、再実行の範囲、失敗・未検証事項を含む原文は[過去のCI検証記録](ci-verification-records.md)に保持する。

| 対象・詳細 | 当時のソース | CI | 特記事項 |
| --- | --- | --- | --- |
| [人物の共通順（初期操作）](ci-verification-records.md#ci-41598552778) | `04c962a1b3` | [41598552778](https://github.com/k-o-a-b-c/monetail/actions/runs/41598552778) | 当時は並び順保存中の認証失効後復帰が未実装（後続で実装） |
| [人物削除のWindows画面](ci-verification-records.md#ci-41593840818) | `1e320297c3` | [41593840818](https://github.com/k-o-a-b-c/monetail/actions/runs/41593840818) | 人物削除・競合・権限変化のスモーク確認 |
| [NuGetキャッシュと復元](ci-verification-records.md#ci-41586922999) | `c44a148d49` | [41586922999](https://github.com/k-o-a-b-c/monetail/actions/runs/41586922999) | 復元ヒット・再テスト抑制の実測、時間短縮率は保証しない |
| [WinUI構成整理前・画面撮影](ci-verification-records.md#ci-41418236183) | `c44a148d49` | [41418236183](https://github.com/k-o-a-b-c/monetail/actions/runs/41418236183)・[41418289696](https://github.com/k-o-a-b-c/monetail/actions/runs/41418289696) | 撮影は96DPI・合成データ |
| [WinUI構成整理・全体検証](ci-verification-records.md#ci-41424095869) | `8592ed02b4` | [41424095869](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095869)・[41424095689](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095689) | 当時の構成815件 |
| [ホーム上部1行化](ci-verification-records.md#ci-41433846188) | `3b61079676` | [41433846188](https://github.com/k-o-a-b-c/monetail/actions/runs/41433846188)・[41433992670](https://github.com/k-o-a-b-c/monetail/actions/runs/41433992670) | 1行ヘッダーと単発撮影 |
| [ホームの表示・ボタン調整](ci-verification-records.md#ci-41513466165) | `21d3925fb4` | [41513466165](https://github.com/k-o-a-b-c/monetail/actions/runs/41513466165)・[41513601351](https://github.com/k-o-a-b-c/monetail/actions/runs/41513601351) | ライト／ダークと登録表記 |
| [接続・認証・家計簿操作画面](ci-verification-records.md#ci-41519000314) | `b11ecc5b81` | [41519000314](https://github.com/k-o-a-b-c/monetail/actions/runs/41519000314) | 当時は製品起動からの自動復元が未接続 |
| [起動・ログイン復元・待機表示](ci-verification-records.md#ci-41521479704) | `1386b5e460` | [41521479704](https://github.com/k-o-a-b-c/monetail/actions/runs/41521479704)・[41522184140](https://github.com/k-o-a-b-c/monetail/actions/runs/41522184140) | 製品起動、待機表示、タイムアウト、撮影 |
| [人物追加・編集・入力・認証復帰](ci-verification-records.md#ci-41532331268) | `41193cdd94` | [41532331268](https://github.com/k-o-a-b-c/monetail/actions/runs/41532331268)・[41532322990](https://github.com/k-o-a-b-c/monetail/actions/runs/41532322990) | 当時の構成826件、更新後一覧と入力復元 |
| [人物画面の単発撮影](ci-verification-records.md#ci-41532325889) | `41193cdd94` | [41532325889](https://github.com/k-o-a-b-c/monetail/actions/runs/41532325889) | 96DPI画像で表示領域を確認 |
| [人物追加・編集のContentDialog化](ci-verification-records.md#ci-41541869671) | `eeea52d32a` | [41541869671](https://github.com/k-o-a-b-c/monetail/actions/runs/41541869671)・[41541869672](https://github.com/k-o-a-b-c/monetail/actions/runs/41541869672) | ContentDialogの操作・保存・復帰 |
| [ContentDialogの単発撮影](ci-verification-records.md#ci-41541866024) | `eeea52d32a` | [41541866024](https://github.com/k-o-a-b-c/monetail/actions/runs/41541866024) | RenderTargetBitmap撮影（背後は画像外） |
| [CIの選択・成功記録再利用](ci-verification-records.md#ci-41581065430) | `8b82a4ae61` | [41581065430](https://github.com/k-o-a-b-c/monetail/actions/runs/41581065430) | 構成847件をCIで確認 |
| [CIの手動撮影](ci-verification-records.md#ci-41581145112) | `8b82a4ae61` | [41581145112](https://github.com/k-o-a-b-c/monetail/actions/runs/41581145112) | 撮影専用で成功記録を保存しない |
| [成功記録11種類の照合](ci-verification-records.md#ci-41582333257) | `a8b256e852` | [41582333257](https://github.com/k-o-a-b-c/monetail/actions/runs/41582333257) | 判定41件のみ再実行、他は成功記録を再利用 |

## 実行方法（リポジトリ直下）

```sh
# .NET 10 / LinuxまたはWindows
dotnet test tests/Monetail.Api.Tests/Monetail.Api.Tests.csproj --configuration Release
dotnet test tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj --configuration Release
# .NET 10 / Windows
dotnet test tests/Monetail.Windows.Storage.Tests/Monetail.Windows.Storage.Tests.csproj --configuration Release
dotnet build src/Monetail.WinUI/Monetail.WinUI.csproj --configuration Release --runtime win-x64 -p:Platform=x64
# 旧版の.NET 8検証
dotnet run --project tests/CalculationTests/CalculationTests.vbproj
python3 -m unittest discover -s tests -p 'test_*migration.py' -v
# CI選択
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

WinUI 3の製品起動と画面スモークは[専用ワークフロー](../.github/workflows/winui.yml)と[run.ps1](Monetail.WinUI.Smoke/run.ps1)で確認する。通常CIでは画像を取らない。撮影はActionsの「WinUI 3 foundation」を手動実行し、対象ブランチと`capture=true`を指定する。撮影専用ブランチへのpushは不要。

旧Windowsでは`dotnet run --project tests/PersistenceTests/PersistenceTests.vbproj`で画面・実SQLiteを確認する。性能計測は同コマンドに`--measure-receipt-reads CSVのパス`を渡し、通常テストと区別する（[測定条件](../docs/34-receipt-read-performance.md)）。APIのDocker確認は[起動・運用](../docs/42-api-foundation.md)を参照する。

## 必要なCIだけを実行する

自動起動はPRの作成・更新と`main`へのpushだけとする。作業ブランチのpushとPRで同じ検証を二重起動しない。WinUI 3の通常検証も[Tests](../.github/workflows/tests.yml)から[再利用ワークフロー](../.github/workflows/winui.yml)を呼び出し、独立したpush/PR起動は行わない。PR更新時は同じPRの古い実行を中止する。

[select_tests.py](../.github/scripts/select_tests.py)が変更と参照プロジェクトから検証対象を選び、[ci_plan.py](../.github/scripts/ci_plan.py)が同じ入力の成功記録を確認する。PRは分岐点以降の全差分を対象にし、最後のコミットが文書だけでも、PR内の未検証・失敗した変更を取り落とさない。改名・削除も扱い、未分類の変更は失敗させる。

| 変更 | 確認対象（同じ入力の成功記録があれば再利用） |
| --- | --- |
| 文書だけ | 軽量なCI判定。PR内に未検証・失敗した変更が残る場合は、その検証を実行 |
| APIソース／テスト | API、製品ソースではDockerも確認 |
| Clientソース | Client・Windowsの実DPAPI・WinUI 3 |
| Clientテストだけ | Client |
| Storageテストだけ | Windowsの実DPAPI |
| WinUI 3／スモーク | WinUI 3のビルド・製品起動・画面スモーク |
| Dockerfile等／Compose | Docker／Compose |
| 旧Windows・計算・SQL等 | 依存するテスト、ビルド、必要なら性能計測 |
| 共通CI・判定処理・SDK／ビルド設定 | 全対象。WinUI専用ワークフローの変更はWinUIのみ |

再利用には、各検証に影響するファイルのパス・Gitオブジェクト・実行属性から求めた指紋を使う。製品だけでなくテスト、参照先、CI定義、共通ビルド設定を含め、チェックアウトしたPRのマージ後ツリーで照合する。入力が変われば別の記録となる。既存の未分類ファイルは指紋から黙って落とさず、保守的に全検証へ含める。

[成功記録用アクション](../.github/actions/ci-proof/action.yml)は、各検証が成功した後にだけ、指紋と元のCIへのリンクをActions cacheへ保存する。中断・失敗を成功として保存しない。復元は完全一致だけとし、記録なし・不一致・破損・キャッシュ障害は再実行する。Windowsで保存した記録もLinuxの判定ジョブで照合する。キャッシュは最適化であり、GitHub側で消去されたら検証し直す。再利用の有無と元の成功CIは実行概要へ表示し、再実行していないものを今回の検証件数へ加算しない。

`main`では現在の全対象の成功記録を確認し、以前のpushが未検証でも次のpushで取り落とさない。PR専用キャッシュを`main`の成功記録として流用せず、GitHubのキャッシュ公開範囲に従う。main取り込み後の確認はPRとの二重起動とは区別する。

NuGetパッケージは[共通アクション](../.github/actions/nuget-cache/action.yml)でキャッシュする。OS・アーキテクチャ・ジョブ・実SDK版・プロジェクト/依存設定でキーを分け、並列ジョブが部分的なパッケージ群を同じキーへ保存しないようにする。旧版の`packages.config`用フォルダーも含める。依存設定が変わった場合は既存パッケージを種として復元できるが、NuGetの依存解決とテスト/ビルドは省略しない。成功結果の再利用とは別の仕組みで、キャッシュ障害時も通常の復元へ戻る。

撮影は必要時の手動実行だけで行い、撮影結果を通常検証の成功記録として登録しない。全検証をやり直したい場合はActionsの「Tests」を`force=true`で手動実行する。成功済みの入力でも再実行できる。

## 新版Windowsの手動確認（未実施）

製品起動から接続・ログイン復元・家計簿の行き先へ進む処理を画面へ接続した。人物追加・編集・削除確認・日付入力と共通Enter操作は接続済み。共通順の設定と同一アカウントでの認証後復元も接続済み。時刻入力のUIは未接続。実装後は[43](../docs/43-passkey-authentication-design.md)・[58](../docs/58-ledger-foundation-design.md)・[59](../docs/59-input-controls-design.md)の契約に沿って確認する。ARM64、100／125／150／200%の物理端末、モニター間移動・ハイコントラスト・日本語IME・実キー入力、実ブラウザーのパスキー、本番配備・実DB移行は未検証。

## 旧Windowsで画面を手動確認する手順（利用者環境では未実施）

手順は[旧Windows版の手動確認チェックリスト](legacy-manual-checks.md)を参照する。**利用者環境での実施は未確認**。必ずDBのバックアップ・テスト用コピーを使い、旧アプリと新アプリで同じDBを同時利用しない。
