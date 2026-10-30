# テストの構成・実行と結果

件数・実行方法・最新の採用結果の正本。過去の詳細は[検証履歴](history.md)へ保存し、通常の改修では読み直さない。実データ・利用者環境への適用を自動検証済みとは扱わない。

| 対象 | 件数 |
| --- | ---: |
| 計算 | 35 |
| SQLite移行（税端数・更新版） | 10 |
| Windowsの実SQLite・画面・起動設定 | 98 |
| ASP.NET Core API | 374 |
| 新版Windowsクライアント（接続・認証・保存・家計簿） | 116 |
| Windowsの実DPAPI・新版画面 | 10 |
| CIの実行対象選択 | 18 |
| 合計（新規Windows画面7件はCI確認待ち） | 661 |

変更前の採用結果は[CI 40853317101](https://github.com/k-o-a-b-c/monetail/actions/runs/40853317101)（ソース`821683bd1ef3de610a9428d68c8ba717d349a8a0`）。旧Windows・計算・Python移行・性能のソースは今回変更せず、この成功結果を再利用する。

家計簿の開く操作と最終選択の更新を一本化し、ローカルでAPI372件と追加の失敗時保持・同時実行2件、クライアント116件、CI選択18件が成功した。新版Windowsと画面テストはReleaseビルドを確認し、新規画面7件の実行はWindows CIで確認する。

APIは合成アカウント・実HTTP・隔離SQLiteで、現在の参加、他アカウントへの非公開、失効・参加喪失時の巻き戻し、保存失敗時の前回選択保持、同時に開く操作、Bearer・Cookieの境界と回数制限を検証する。クライアントは合成HTTPで起動先・作成の再送・認証更新を確認する。Windowsでは実DPAPIに加え、所属0／1／複数の表示、同名家計簿のID選択、作成の通信再試行、別ウィンドウによるアカウント／接続先の変更時に操作を止めて新しい認証を保持することを検証する。

物理端末のパスキー、実ブラウザーを含む手動操作、本番配備、一度きりのSQL移行は未確認。過去の採用結果は[履歴](history.md)を参照する。

## 実行方法（リポジトリ直下）

```sh
# .NET 10
dotnet test tests/Monetail.Api.Tests/Monetail.Api.Tests.csproj --configuration Release
dotnet test tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj --configuration Release
# Windows / .NET 10
dotnet build src/Monetail.Windows/Monetail.Windows.csproj --configuration Release
dotnet test tests/Monetail.Windows.Tests/Monetail.Windows.Tests.csproj --configuration Release
dotnet run --project src/Monetail.Windows/Monetail.Windows.csproj
# .NET 8
dotnet run --project tests/CalculationTests/CalculationTests.vbproj
python3 -m unittest discover -s tests -p 'test_*migration.py' -v
# CI選択（Python標準ライブラリ）
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

Windowsでは`dotnet run --project tests/PersistenceTests/PersistenceTests.vbproj`で画面・実SQLiteを確認する。製品変更時は既存WindowsソリューションのNuGet復元・ビルドもCIで行う。性能計測は同コマンドへ`--measure-receipt-reads CSVのパス`を付け、通常テストと区別する（[測定条件](../docs/34-receipt-read-performance.md)）。APIのDocker確認は[起動・運用](../docs/42-api-foundation.md)を参照する。

## 必要なCIだけを実行する

[select_tests.py](../.github/scripts/select_tests.py)がGit差分とプロジェクトの入力依存から選ぶ。PRは分岐点以降、pushは更新前後を対象とし、改名・削除も確認する。未分類の変更は失敗させ、推測で全実行しない。

| 変更 | 確認対象 |
| --- | --- |
| 文書だけ | CI選択処理。API・Docker・Windows等は省略 |
| APIソース | APIテスト・Dockerビルドと起動 |
| APIテストだけ／Docker確認スクリプトだけ | 各対象だけ |
| 新版WindowsのClient／WinForms画面 | クライアントテスト＋新版Windowsビルド・実DPAPI／新版Windowsビルド・実DPAPIだけ |
| 新版Windowsの各テストだけ | 対応するテスト。API・旧Windowsは起動しない |
| Dockerfile等／Compose | Docker確認／Compose設定 |
| Windows・計算・SQL等 | 依存するテスト、製品ビルド、必要なら性能計測 |
| CI・共通SDK／ビルド設定 | 全対象 |

同じ製品ソースの成功CIは再利用し、新しい変更・失敗・懸念があるときだけ再実行する。省略を検証成功として数えない。[Actions](https://github.com/k-o-a-b-c/monetail/actions/workflows/tests.yml)ではコミットと選択された対象を確認する。

## 新版Windowsの手動確認（未実施）

テスト用Windowsユーザーと、[42の準備](../docs/42-api-foundation.md)を済ませたHTTPSのテストAPI・合成アカウントを使う。旧Windows版のDB・設定は使わない。家計簿履歴表は未リリースAPIの初回定義へ追加したため、以前の開発DBを更新する互換処理はない。新定義の隔離DBを準備する。

1. 新版を上記コマンドで起動する。初回はURL入力、不正URLは保存しないこと、接続確認成功後だけログインへ進むことを確認する。
2. ログインから外部ブラウザーを開き、パスキー認証後にアプリの枠へ進む。ブラウザーがログイン済みなら再認証を挟まないこと、タブ終了とアプリ前面復帰が制限されても操作できることを確認する。
3. ブラウザーを閉じて「ブラウザーを開く」で再表示する。「キャンセル」はログインへ戻り、再試行できることを確認する。
4. 再起動時は保存済み認証でログインを省略する。同一URLの正規化表記を保存し直しても認証を維持し、別のテストAPIへ変更した場合はログインが必要になることを確認する。
5. API停止時の起動・再試行では認証ファイルを失わず、復旧後に復元する。接続確認が遅い場合の1秒後の小窓、10秒での接続エラーと3つの操作を確認する。
6. テストセッションの失効・トークン期限切れと更新、破損した認証ファイルでは、URLを残してログインへ戻ることを確認する。ログアウトはWindowsセッションだけを終了し、ブラウザーのセッションは維持する。
7. `%LOCALAPPDATA%\Monetail\settings.json`にトークンがなく、`authentication.dat`と残存する一時ファイルに平文トークンがないことを確認する。トークン値は記録・共有しない。

家計簿は、所属0件の作成、履歴なし1件の自動表示、複数件の選択、別端末の最終選択の復元を確認する。家計簿名のメニューで切替・追加し、名前不正・作成失敗後の再試行、参加喪失、開く処理の保存失敗時に前回選択が残ることも確認する。レシート等の業務データはまだ表示しない。

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
