# 04 レガシー版 アーキテクチャ・開発環境

> 次の構造変更は[40 明細税率への統一設計](40-detail-tax-unified-design.md)。[41の独立したコピーDB移行ツール](41-detail-tax-copy-migration.md)を実装した。Windowsアプリの切替は未完了で、本文は現行アプリの説明。

更新日: 2026-10-19 / 現在のソース・DDL・自動テストに照合。Windows CIで既存ソリューションのビルドと対象画面・永続化テストを確認済み。実運用DBでの起動・手動確認は未実施。

## 構成

```text
Windows デスクトップアプリ
  ViewForm / Register*Form / DetailViewForm（Windows Forms UI）
       ├── Receipt・Balance・Transfer・Master（VB.NETデータクラス）
       │      └── Receiptの計算プロパティ + Calculation（税・割引・残高）
       └── DataAccess + DataAccessList（System.Data.SQLite、SQL直書き）
                  └── ローカル SQLite ファイル
```

- 言語: **VB.NET**、UI: **Windows Forms**、ターゲット: **.NET Framework 4.8**
- ソリューション: `HouseholdAccountsBeta/HouseholdAccountsBeta.sln`
- プロジェクト: `HouseholdAccountsBeta` と `OriginalControls`
- DBライブラリ: `System.Data.SQLite.Core 1.0.113.1`（`packages.config`）、参照は `System.Data.SQLite 1.0.113.0` として記載。
- チャート: `System.Windows.Forms.DataVisualization.Charting`
- 起動フォーム: `ViewForm`（`My Project/Application.Designer.vb`）
- 現行データ操作: クライアントからSQLiteへ直接SQL発行。Web APIやクラウド同期はない。

## ディレクトリ

| パス | 責務 |
| --- | --- |
| `HouseholdAccountsBeta/HouseholdAccountsBeta/Control/` | 画面、イベント、操作、集計表示 |
| `.../Data/Data/` | レシート・振替・残高などの取引オブジェクト |
| `.../Data/Master/` | 人・分類・支払方法・店舗・税率・ポイントマスタ |
| `.../Data/Statistics/` | 集計用DTO |
| `.../DataAccess/` | SQLite接続、SELECT/INSERT/UPDATE/DELETE、履歴取得 |
| `.../Logic/Calculation.vb` | 税・割引・残高の計算 |
| `.../Logic/IntegerExpression.vb` | 単価・値引額の整数式評価。乗算優先・入力不正や桁あふれは0 |
| `.../Logic/ReceiptAmountValidation.vb` | 既存計算を使う金額範囲の確認。反映候補と選択税設定で確認し、元モデルを変更しない |
| `.../MasterConfig/` | マスタ編集用と思われるXMLメタデータ（画面CRUDは未実装） |
| `HouseholdAccountsBeta/OriginalControls/` | 独自テキストボックスなどのコントロール |
| `tests/` | 実製品ソースを使う計算・Windows永続化／画面テスト、SQLite移行テスト |
| `.github/workflows/tests.yml` | Linux／Windowsのテストと既存ソリューションのWindowsビルド |

残高推算の期間判定・加減算は`Calculation.AutoBalance`の内部3引数オーバーロードに集約する。従来の公開2引数版は共有`Calculation.DataAccess`、`DataAccess.GetAutoBalance`は自身のインスタンスを渡す。SQL読込は渡されたDataAccessが担い、インスタンス側の推算で共有接続を切り替えない。

## 代表的な処理フロー

1. 起動: `ViewForm.New`で画面を構築し、`ViewForm_Load` → `DatabaseStartup.TryOpenConfiguredDatabase` → `DataAccess`で設定・既存ファイル・必要なDB構造を確認する。成功後に一覧を読み込み、設定・DBの問題では案内して終了する。[19 接続設定](19-database-startup-settings.md)参照。
2. マスタ: 人／支払方法／店舗／税率等をSELECTして画面の選択肢を生成。マスタの一部は初回読込後に保持。
3. レシート編集: グループ・明細は既存どおり `Receipt` の子リストを編集する。店名等と内税／外税・端数方式はコントロールに保持し、合計表示は `Receipt.GetTotal(taxType, taxRoundingMode)` で `Receipt` の税設定を変えずに計算する。必要な登録確認後にヘッダーへ転記 → `DataAccess.RegisterReceipt / UpdateReceipt`。レシート全体のディープコピーは導入していない。
4. レシート参照: `GetReceipts`と`GetPaymentReceipts`はLEFT JOINの1 SELECTと共通の`ReadReceiptHierarchy`でモデルへ組み立てる。一覧の日時降順と支払方法別のSeq昇順、各入口の期間条件を維持する（[35](35-batch-receipt-reads.md)、[36](36-batch-payment-receipt-reads.md)）。`LoadReceiptChildData`は従来の子SQLを一時領域へ読み、成功時だけ元のGroupsへ反映する（[37 途中失敗の保持](37-receipt-child-reload-state.md)）。内部は`LoadReceiptGroupData` → `LoadReceiptDetailData` → `LoadReceiptDetailUsersData`を維持する。SQL数と読込時間はWindowsの一時DBで[計測する](34-receipt-read-performance.md)。
5. 集計: 画面内で年次データを取得し、`Receipt` の計算プロパティと `Calculation` で集約。
6. 振替／残高: 専用フォームから `DataAccess` の各登録メソッドを呼ぶ。
7. 支払メモ: `RegisterPaymentMemoForm`から全行を`RegisterPaymentMemo`へ渡す。既存のUPSERTループ全体を1つのトランザクションで処理し、全件成功時にCommit、例外時にRollback・再送出し、Finallyで破棄する。空リストはトランザクション開始前に戻る。

税端数はレシートの `TaxRoundingMode`（`floor` / `half_up`）が正本で、同税率の明細を合算してから処理する。新規画面の初期選択だけを、同店・現在のWindows利用者が所有する支払方法の直近レシートから取得する。店舗マスタの端数値は参照せず、マスタメンテナンス画面も追加していない。流用は従来どおり確認直後の新規保存・画面終了で、画面上の税設定を保存する。

グループ税率の初期選択は、2019年10月1日以降の通常分類（未選択を含む）が10%、それ以外が8%。表示順ではなく`TaxRate.Rate`で探し、同率ならコード昇順で選ぶ。該当なしは未選択とし、手動選択まで追加／更新を無効にする。分類コード＋税率コードでグループを区別し、既存の組合せへの変更は統合する。税率未選択でも分類変更の統合先が一意なら継続でき、複数候補の場合は税率を選ぶ（[39 詳細](39-classification-tax-groups.md)）。初期選択と一覧の標準税率の判定は`GetInitialGroupTaxRate`で共用し、異なる税率だけ分類名に付記する。日付変更時は行名だけを更新して選択・入力を保持する。既存グループの選択時は保存済みの税率を表示する（[38 詳細](38-tax-default-selection.md)）。

更新・削除では、対象者割当→明細→グループ→（削除時のみ）レシート親の順で削除し、既存トランザクションでDB変更を確定・巻き戻す。外部キーON/OFFをテスト用接続で検証しているが、製品の外部キー設定は変更していない。

残高削除は`DataAccess.DeleteBalance`で`t_balance_groups`→`t_balances`の順に行う。既存トランザクション・DDL・画面操作を維持し、FK ON/OFFで成功と親削除失敗時の巻き戻し・同じDataAccessでの再削除を検証する（LEG-027）。

所在地とレシート／グループ／明細タグのNULLは読込時だけ `String.Empty` に変換する。一覧・残高の両ヘッダー読込と子の読込が対象で、読込・取消はDBのNULLを保存し直さない。後から明示的に保存した場合は、従来の保存SQLで空文字を書き込む。

残高・支払方法別残高・振替のタグ3列も6箇所でNULLだけを`String.Empty`へ変換し、非NULL文字列・日時・金額・評価額・支払方法と保存済みNULLを維持する（LEG-028）。

明細の割引率は共通の`LoadReceiptDetailData`で`GetDouble`により読み込む。SQLiteの`INTEGER`宣言でも小数はREALとして保存され、旧`GetInt32`は読込例外となった。読込型だけをモデルの`Double`へ合わせ、保存・計算・画面操作は変更していない。

`ViewForm_Statistics.vb`は分類・収支集計の表示処理を共用する。範囲超過時は途中の行を消してエラー行を表示し、表示設定はFinallyで戻す。集計式・DataAccessは変更しない。[26 仕様](26-statistics-amount-overflow.md)。

`BalanceDisplay.vb`は登録・支払メモの推算表示でOverflowExceptionだけを表示文字列へ変換する。残高集計は共通の`UpdateStatistics`を使い、表とグラフの途中結果を消す。モデル・計算・DataAccessは維持する。[27 仕様](27-balance-amount-overflow.md)。

振替の`TryGetTransferInput`はボタン状態と保存処理の入口で共用する。確認前に入力と整数変換を検証し、流用のSeq変更後にParseで失敗する経路をなくす。[28 仕様](28-transfer-input-validation.md)。

残高登録は画面から`pendingGroups`を作り、全行の検証と確認が済んでからBalanceへ転記する。エラーで途中のリストを親一覧のモデルへ残さない。[29 仕様](29-balance-input-state.md)。

## 起動・開発上の注意

1. WindowsとVisual Studio（.NET Framework 4.8用の開発機能）が必要。
2. `.sln` を開きNuGetパッケージを復元してビルドする。GitHub Actionsでは `nuget restore` と `msbuild`（Debug / Any CPU）による既存ソリューションのビルドを確認済み。
3. `My.Settings.ConnectionString`の既定値は`App.config`・`Settings.settings`・生成コードでEドライブへ統一。配布先はexe横のXMLを編集する。相対パスはexe基準。不在時は自動作成せず、構造・移行不足でも起動を中止する。利用するDBの指定は必要。[設定手順](19-database-startup-settings.md)参照。
4. SQLiteの実データファイルはレポジトリにない。ユーザー提供の実DDLを [docs/schema/monetail_schema.sql](schema/monetail_schema.sql) に保存済み。これは移行前の16テーブル・90列・17外部キーの定義であり、現行アプリには[税端数の移行SQL](migrations/20261017_01_shop_tax_rounding.sql)による2列追加（合計92列）が必要。バックアップしたDBのコピーへ一度だけ適用して確認し、新旧アプリで同じDBを共有しない。新規環境で必要な**マスタ初期データ**は別途用意する。
5. 実データはGitHubにコミットしない。設定の汎用化・サンプルDBの匿名化が必要。
6. Windowsログイン名 `Environment.UserName` で個人識別を一部行う。アプリ認証・家計ごとの権限チェックとは異なる。
7. 利用者の実DBへのマイグレーション適用状況は未確認。CI成功は、既存データ全件の整合性や運用環境での画面表示を検証したことを意味しない。

## 自動検証

[Testsワークフロー](../.github/workflows/tests.yml)は、Linuxの計算・移行テストとWindowsの実SQLite／画面テスト・既存ビルドを実行する。別のWindowsジョブでレシート読込のSQL数・時間を計測する。自動テストの構成・件数・最新CI結果は[tests/README.md](../tests/README.md)を参照する。

Windowsテストは製品と同じSQLiteプロバイダー、実際の `DataAccess`・`RegisterForm`・独自コントロールを使い、一時DBだけで動作する。税端数の保存・履歴・プレビュー・即時流用、外部キーON/OFFのレシート更新／削除と失敗時DB復元、残高削除の成功・巻き戻しと再削除、レシートのNULL読込・画面取消、小数割引率の新規・更新と3読込経路、支払メモ一括保存の成功・末尾INSERT／UPDATE失敗時の全件復元と同接続での再保存・空リストを検証する。残高・振替のNULLタグ読込と両AutoBalanceの3チェックも成功した。テストの実行方法と手動確認手順は [tests/README.md](../tests/README.md) を参照。

競合の3チェックは、同じ一時ファイルへの2接続で採番の直列化・書込ロック失敗と再試行・古い編集内容の拒否を検証する。親3表の更新版を読込・保存し、競合時は警告と再読込フラグを返す。[保存契約](18-stale-edit-protection.md)参照。

## オンライン化に向けた境界

まずWindows版を進め、後続のWeb化は必須とする。Web版はレシート入力→振替入力の順に優先する（[33 方針](33-windows-first-web-input-priorities.md)）。以下は後続の構成案で、**現行実装ではない**。言語・認証・DB製品等は未決定。

```text
Web / スマートフォン / デスクトップ
             ↓ 認証付き HTTPS
             API・認可・業務ロジック
             ↓
       DB（製品は未決定）
```

レガシー版のUIから計算ルール・永続化条件を切り離し、[03-calculations.md](03-calculations.md) と [06-test-cases.md](06-test-cases.md) を移植時の契約として使う。

## 関連する詳細資料

- [09 操作ガイド](09-user-guide.md) — 画面の入出力・利用者の操作
- [10 DBリレーション・データフロー](10-data-flow.md) — ER図、SQL読込・保存・更新・削除
- [11 DB検査・バックアップ](11-db-audit.md) — 調査用SQLと安全な確認手順

## 根拠

[ソリューション](../HouseholdAccountsBeta/HouseholdAccountsBeta.sln)、[プロジェクト](../HouseholdAccountsBeta/HouseholdAccountsBeta/HouseholdAccountsBeta.vbproj)、[設定](../HouseholdAccountsBeta/HouseholdAccountsBeta/App.config)、[依存パッケージ](../HouseholdAccountsBeta/HouseholdAccountsBeta/packages.config)

レシートの`HasRequiredReceiptInput`は両ボタンの状態と`RegisterReceipt`の入口で共用する。店名のTextChangedも状態を更新し、不備のある入力で確認・Seq変更・ヘッダー転記へ進まない。[30 仕様](30-receipt-input-validation.md)。

`DetailViewForm.ShowGroupStatistics`はOverflowExceptionだけを表内表示へ変換し、前回／途中の行を消す。読込・計算をTryで囲み、Finallyで行高・列幅・見出しの自動調整と品名の幅上限を復元する。金額以外の例外は伝播する。[31 仕様](31-detail-view-amount-overflow.md)。

`ViewForm_List.vb`の非公開ReceiptListRowは元Receiptを参照し、TotalのOverflowExceptionだけを表示文字列へ変換する。その他の列と編集先は同じReceiptを参照し、金額を固定コピーしない。Designerのバインド名と正常なN0書式を維持する。[32 仕様](32-receipt-list-amount-overflow.md)。
