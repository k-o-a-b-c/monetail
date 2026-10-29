# テストの構成・実行と結果

件数・実行方法・最新の採用結果の正本。過去の詳細は[検証履歴](history.md)へ保存し、通常の改修では読み直さない。実データ・利用者環境への適用を自動検証済みとは扱わない。

| 対象 | 件数 |
| --- | ---: |
| 計算 | 35 |
| SQLite移行（税端数・更新版） | 10 |
| Windowsの実SQLite・画面・起動設定 | 98 |
| ASP.NET Core API | 354 |
| 新版Windowsクライアント（接続・認証・保存） | 98 |
| Windowsの実DPAPI | 3 |
| CIの実行対象選択 | 18 |
| 合計（未実行の新Windows固有3件を含む） | 616 |

変更前のAPI全346件・Dockerの採用結果は[CI 40764853423](https://github.com/k-o-a-b-c/monetail/actions/runs/40764853423)（ソース`fd836eedfdb1fde178650b90038a391c83bf02dc`）。認証APIのDB準備確認と管理コマンドの設定エラーを修正し、Cookie／Bearerでの拒否・正常化後の復帰2件、不正な接続設定でのコマンド終了6件を追加した。変更後のAPI・Dockerの検証は[CI 40830448904](https://github.com/k-o-a-b-c/monetail/actions/runs/40830448904)（ソース`8508e42738eb69f44e7e0172e5ae140111e4c680`）を参照する。API全354件（失敗・スキップ0）、CI選択15件とDockerのビルド・起動が成功した。ローカルでもAPI全354件が成功し、認証情報の応答漏れ・DBの書換えがないこと、終了コード1・秘密値や例外スタックの非表示を確認した。

新版Windowsの接続・認証基盤は、.NET 10 SDKでReleaseビルド、クライアント98件（失敗・スキップ0）とCI選択18件をローカル確認した。クライアントは合成HTTP、実ループバック、合成暗号化保存を使い、PKCE・state・不正コールバック・キャンセル・トークン更新・失効と通信障害の区別・接続先への紐付けを検証する。Windows専用の実DPAPI3件は追加済みで、ローカルLinuxでは実行していない。

既存Windows・API・計算・Python移行・性能の製品ソースは今回の変更対象外。物理端末のパスキー、実ブラウザー・画面の手動操作、本番配備、一度きりのSQL移行は未確認。過去の採用結果は[履歴](history.md)を参照する。

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

## Windowsで画面を手動確認する手順（利用者環境では未実施）

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
