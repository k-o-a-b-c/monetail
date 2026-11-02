# テストの構成・実行と結果

件数・実行方法・採用結果の正本。旧VB.NET版の検証と、新版WinUI 3・Clientの検証を区別する。過去の検証履歴は[履歴](history.md)と各コミット・CIの記録を参照し、実装のない画面を検証済みとは扱わない。

| 対象 | 件数 |
| --- | ---: |
| 旧版の計算 | 35 |
| SQLite移行（税端数・更新版） | 10 |
| 旧版Windowsの実SQLite・画面・起動設定 | 98 |
| ASP.NET Core API | 430 |
| Windows Client（接続・認証・保存・家計簿・人物・補完） | 218 |
| Windowsの実DPAPI・保存 | 3 |
| WinUI 3の製品起動・画面スモーク（1シナリオ） | 1 |
| CIの実行対象選択 | 20 |
| 合計 | 815 |

## 検証する範囲

- Clientは合成HTTPと一時保存先を使う。接続確認、OAuth/ループバック、セッション復元、更新と失効、別プロセスの認証変更、設定/トークン保存、家計簿の起動先・作成・切替、人物のCRUD/順保存/競合/再送を確認する。日付・時刻は補完期間・世紀選択・空欄・不正入力・全角・区切り・地域設定を検証する。
- StorageはWindowsのCurrentUser DPAPIを実際に使い、暗号化保存と新しいStoreでの復元、破損時の認証破棄と接続設定保持、平文の拒否を確認する。UIへの依存はない。
- WinUI 3は製品EXEを起動し、同じMainWindowのXAML・各partial・ClientConnection・PersonRowを使う別のスモークAppで合成データを検証する。ホーム、左右の人物管理、リサイズとテーマに加え、接続設定・ブラウザー起動の失敗と再開・認証キャンセル・実ループバックからの合成認可応答・保存ログインの確認と失効・家計簿作成の再送と選択・接続先変更・ログアウト成功／失敗を1シナリオとして確認する。製品Appから起動時の接続・復元を開始し、遅い接続での同じWindow内の待機表示・完了後の停止・タイムアウトも確認する。実データや保存済みの接続先/トークンは使用しない。
- APIは実HTTP・合成アカウント・隔離SQLiteを使い、認証・現在の参加/役割・保存失敗の巻き戻し・同時操作・FK・再送・競合・招待の状態と消費・共有回数枠を検証する。CookieとWindows Bearerを区別する。
- 旧VB.NET版の計算・DB・画面・移行・性能は既存のテスト構成を維持する。実データ・利用者環境への適用を自動検証済みとは扱わない。

## 採用結果

構成整理前のWinUI 3は[CI 41418236183](https://github.com/k-o-a-b-c/monetail/actions/runs/41418236183)（表示ソース47f4054）で製品・スモークのビルドが警告・エラー0、製品起動と画面スモークが成功。[単発撮影CI 41418289696](https://github.com/k-o-a-b-c/monetail/actions/runs/41418289696)ではアプリ名を置かず家計簿名を表示したホーム、左メニューと右の人物一覧、コンパクト表示、標準ダークテーマを96DPIで確認した。撮影時だけNoto Sans CJK JPを使い、通常起動へサンプルや撮影引数を入れていない。

現在の採用結果は対象ソース`8592ed02b4423ac47aeb3afc93f65fb150f013a7`。[通常CI 41424095869](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095869)でClient218件・実DPAPI3件・API430件・旧版の計算35件／SQLite移行10件／画面・永続化98件・CI選択20件が失敗・スキップ0で成功し、Dockerの起動確認・性能計測・旧ソリューションのビルドも成功した。[WinUI CI 41424095689](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095689)では製品・スモークのReleaseビルドが警告・エラー0、製品EXEの起動と画面スモーク1シナリオが成功。今回は構成合計815件を実行した。Client21ファイルの内容・保存形式・API契約と旧VB.NET版のソースは変更せず、配置・参照・検証構成を整理した。

ホーム上部の1行化はソース`3b610796769c0de2bdb8e961239871c80d7d4670`を[WinUI CI 41433846188](https://github.com/k-o-a-b-c/monetail/actions/runs/41433846188)で確認し、製品・スモークのビルドが警告・エラー0、製品起動と既存の画面スモークが成功。[単発撮影CI 41433992670](https://github.com/k-o-a-b-c/monetail/actions/runs/41433992670)でも、家計簿名が左端にある1行ヘッダーを目視確認した。Client・Storage・API・旧版等は変更がなく、上記の成功結果を再利用する。

ホームの文字色・選択表示・設定ボタン・登録表記の調整はソース`21d3925fb4fc19389cf8c3a1b51b6ac6b036a29e`を[WinUI CI 41513466165](https://github.com/k-o-a-b-c/monetail/actions/runs/41513466165)で確認し、製品・スモークのビルドが警告・エラー0、製品起動と既存スモークが成功。[単発撮影CI 41513601351](https://github.com/k-o-a-b-c/monetail/actions/runs/41513601351)ではライト・ダーク両方のホームを目視確認し、通常色の家計簿名、記録の選択線、枠なしの管理・設定、3種類とも「＋ 登録」を確認した。ダークのホーム画像取得は撮影用ブランチだけに追加。操作可否・Client・保存仕様・旧版は変更せず、その他の検証は上記の成功結果を再利用する。

接続・認証・家計簿操作のWinUI画面はソース`b11ecc5b815649422d46d88946ae8b74b118895b`を[WinUI CI 41519000314](https://github.com/k-o-a-b-c/monetail/actions/runs/41519000314)（job125529850776）で確認し、製品・スモークのReleaseビルドは警告・エラー0、製品起動と拡充した画面スモーク1シナリオが成功した。合成HTTPと実ループバックで上記の接続・認証・保存・家計簿の失敗と再試行を確認した。当時は製品Appからの自動復元開始が未接続で、実ブラウザーのパスキー認証を検証した結果ではない。既存Client218件は変更前にローカルでも成功。API・Client・保存形式・旧版は変更せず、その他の検証は上記の成功結果を再利用する。今回の画面は撮影していない。

製品起動からの接続・復元と1ウィンドウの待機表示はソース`1386b5e460bed8f5713525a0761b91b27c7afd0a`を[WinUI CI 41521479704](https://github.com/k-o-a-b-c/monetail/actions/runs/41521479704)（job125538323541）で確認し、製品・スモークのReleaseビルドは警告・エラー0、製品EXEの起動と画面スモーク1シナリオが成功した。待機表示なしでの完了後に遅れて表示しないこと、1秒後の有効なProgressRingと状態文、完了時の停止、10秒タイムアウトでの認証保持とエラー表示、起動先の要求拒否からの再試行を含む。[単発撮影CI 41522184140](https://github.com/k-o-a-b-c/monetail/actions/runs/41522184140)（撮影ブランチc4bcc97）でも成功し、接続先設定と待機表示を96DPIの静止画で目視確認した。製品のXAML・コードは1386b5eと同一で、撮影ブランチだけアニメーションの描画を待って画像を取得した。API・Client・保存形式・旧版は変更しておらず、その他は上記の成功結果を再利用する。

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

WinUI 3の製品起動と画面スモークは[専用ワークフロー](../.github/workflows/winui.yml)と[run.ps1](Monetail.WinUI.Smoke/run.ps1)で確認する。通常CIで画像は取らず、`preview/winui-*`ブランチでのみ単発撮影する。

旧Windowsでは`dotnet run --project tests/PersistenceTests/PersistenceTests.vbproj`で画面・実SQLiteを確認する。性能計測は同コマンドに`--measure-receipt-reads CSVのパス`を渡し、通常テストと区別する（[測定条件](../docs/34-receipt-read-performance.md)）。APIのDocker確認は[起動・運用](../docs/42-api-foundation.md)を参照する。

## 必要なCIだけを実行する

[select_tests.py](../.github/scripts/select_tests.py)が差分と参照プロジェクトから実行対象を選ぶ。PRは分岐点以降、pushは更新前後を対象にする。改名・削除も扱い、未分類の変更は失敗させる。

| 変更 | 確認対象 |
| --- | --- |
| 文書だけ | CI選択。API・Docker・Windows等は省略 |
| APIソース／テスト | API、製品ソースではDockerも確認 |
| Clientソース | ClientとWindowsの実DPAPI。専用ワークフローでWinUI 3のビルド・起動も確認 |
| Clientテストだけ | Client |
| Storageテストだけ | Windowsの実DPAPI |
| WinUI 3／スモーク | 専用ワークフローのビルド・製品起動・画面スモーク |
| Dockerfile等／Compose | Docker／Compose |
| 旧Windows・計算・SQL等 | 依存するテスト、ビルド、必要なら性能計測 |
| 共通CI・SDK／ビルド設定 | 全対象 |

同じ製品ソースの成功CIは再利用し、新しい変更・失敗・未解決の懸念がある場合だけ再実行する。省略を検証成功として数えない。

## 新版Windowsの手動確認（未実施）

製品起動から接続・ログイン復元・家計簿の行き先へ進む処理を画面へ接続した。人物の保存・共通キー操作/IMEは未実装。実装後は[43](../docs/43-passkey-authentication-design.md)・[58](../docs/58-ledger-foundation-design.md)・[59](../docs/59-input-controls-design.md)の契約に沿って確認する。ARM64、100／125／150／200%の物理端末、モニター間移動・ハイコントラスト・日本語IME・実キー入力、実ブラウザーのパスキー、本番配備・実DB移行は未検証。

## 旧Windowsで画面を手動確認する手順（利用者環境では未実施）

1. 確認対象の作業ブランチ（mainへの取り込み後は `main`）をチェックアウトし、READMEの開発手順で既存ソリューションをビルドします。
2. DBをバックアップしてテスト用コピーを作ります。税端数の2列が未追加なら`docs/migrations/20261017_01_shop_tax_rounding.sql`を一度適用します。次にレシート・振替・残高の3表を`PRAGMA table_info`で確認し、`revision_token`が未追加なら`docs/migrations/20261019_01_revision_tokens.sql`を一度適用します。適用済みの移行は再実行しません。`App.config`の接続先をそのコピーに変更して起動します。
3. 未登録の店舗名で新規レシートを作成し、「外税」、税率10%、19円の明細2件を入力します。「税端数」のラジオボタンを切り替え、切り捨て41円／四捨五入42円を確認します。端数を切り替えても「外税」が選択されたままで、「内税」に切り替えても端数の選択が保持されることを確認します。
4. 四捨五入で保存して開き直し、方式と42円が保持されることを確認します。店名を変えても方式が変わらないことを確認します。「内税」と別の端数方式へ変更して保存せず閉じ、同じレシートを再度開くと元の「外税」と方式・合計に戻ることを確認します。「内税」から「外税」への取消も同様です。保存した場合には変更後の設定が保持されます。編集中の選択は画面側だけで保持され、Receiptの両設定は登録確認後に転記されます。
5. 同じ店舗の新規レシートを開き、候補を選ぶか店名を入力して別の欄へ移動すると、直近の四捨五入が初期選択されることを確認します。その店舗で切り捨てのレシートを後の日付で保存すると、次の新規入力は切り捨てになります。履歴のない店舗は切り捨てです。レシートで方式を手動選択した後は、店舗を変えても選択を保持します。
6. 既存レシートを開き、必要なら画面上の税端数を変更して「流用」を押します。従来の確認後に新しいレシートが保存され、画面が閉じることを確認します。「登録」の押し直しは不要です。元レシートのDB上の内容は変わらず、流用先には画面上の端数方式が保存されます。流用確認で「いいえ」を選ぶと、画面は開いたままで追加保存されません。
7. 税込入力で方式を変えても合計が変わらないことと、既存レシートの金額・オーケーの手入力割引に変化がないことを確認します。
8. 外税8%、単価103円・単価値引3円・数量2・値引率2.5%で保存し、開き直して率と合計（切り捨て210円／四捨五入211円）が保持されることを確認します。率を2.6%へ更新して再表示し、切り捨て209円／四捨五入210円を確認します。0%・3%の整数率も従来どおり保存・再表示できることを確認します。

旧アプリと新アプリで同じDBを同時に使わないでください。

税率の初期選択では、境界日・分類・並び順・コードと率の違い・余分な候補・該当なしを含め、WindowsのFK ON/OFFで実画面の日付／分類変更、該当なしでの追加抑止と手動選択後の復帰、保存済みグループの税率復元と取消を確認し、DB全値・型・スキーマ・PRAGMA・書込0回と保存済み合計の保持を検証する。[38 仕様](../docs/38-tax-default-selection.md)。

保存時合計比較は実SQLite・FK ON/OFF・両端数方式で、一覧／年次／支払方法別の読込と子再読込、NULL・0・SQLite INTEGERの両端、不一致の正負、合計セルと選択時の色、en-US／de-DEのツールチップ、既存の行色と範囲超過、表示中モデルの変更・フィルター・月の切替後の復帰を検証する。全保存値・型・スキーマ・版・PRAGMAとtotal／NULLの保持、書込0回も比較する。[39 仕様](../docs/39-receipt-saved-total-comparison.md)。

Decimal税計算は、正負の整数境界・税率別合算・両端数方式・税込／税抜・大きなDecimal戻り値、収入／支出・分類／明細参考額・プレビュー・保存済み合計保持を固定値で検証する。WindowsではFK ON/OFFで旧floorの保存値−111円を残し、再計算−110円との差額+1円が表示されることを確認する。値引・人数按分の既存回帰も継続する。[40 仕様](../docs/40-decimal-tax-calculation.md)。

率値引Decimal化は、3,000円・1.1%の境界、負額と切り捨て方向、手入力値引・数量の順序、0／100%・既存の100%超・小数率、明細ごとの整数化と税率別合算を検証する。Windowsではen-US／de-DEの率入力、INTEGER／REALの新規・更新・3読込経路（追加率1.1／2.2／2.51%）、旧保存値2,966円を保った再計算2,967円と差額+1円を確認する。閲覧の書込0回・保存済みtotal／率・全行／型／版／スキーマの保持も比較する。[41 仕様](../docs/41-decimal-discount-calculation.md)。

明細計算共通化では、既存の整数境界テストでReceiptDetail.CalculateDiscountedFeeの戻り値も固定期待値と比較する。Int32を超える税処理前金額をDecimalで返せること、数量変更後に最新値で再計算することを確認し、合計・分類／明細／人別参考額、保存・表示の既存回帰を継続する。
