# 04 アーキテクチャ・開発環境

更新日: 2026-10-28 / 現行Windows版・APIの構成と、新版Windows版の設計を区別する。検証状況は[tests/README.md](../tests/README.md)を参照する。

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
- 現行データ操作: WindowsクライアントからSQLiteへ直接SQL発行。APIへの接続切替やクラウド同期は行っていない。

同じリポジトリの`src/Monetail.Api/`にC#／ASP.NET Core／.NET 10 LTSの独立プロジェクトを置く。Windows版のプロジェクト・モデルは参照しない。API専用SQLiteの初期化と初期管理者・登録コード発行を専用コマンドで行い、通常起動ではDBを自動作成・移行しない。既存Windows版DBの一度きりのSQL引継ぎと、Windows版を先にAPIへ接続する方針は[58](58-ledger-foundation-design.md)を参照する。リポジトリ直下にSDKを固定する`global.json`は置かず、API用CIでは.NET 10 SDK、既存テスト用CIでは従来の.NET 8 SDKを使う。認証の現行契約は[43](43-passkey-authentication-design.md)、起動・DB準備は[42](42-api-foundation.md)を正本とする。

### 新版Windows版の構成（未実装）

新版は**.NET 10 LTS・C#・WinForms**とし、同じリポジトリの`src/Monetail.Windows/`にSDK形式の独立プロジェクトを設ける。ターゲットは`net10.0-windows`。現在のVB.NET／.NET Framework 4.8のプロジェクトは残し、新版から旧版の実行ファイル・プロジェクトを直接参照しない。配置先は設計であり、プロジェクトはまだ作成していない。

画面、API通信、認証情報の保持を分ける。新版はAPI経由で読み書きし、フォームへSQLやSQLite接続を持ち込まない。認証契約は[43](43-passkey-authentication-design.md#windows版のoauth認証dec-051)、起動時の遷移と段階移行は[58](58-ledger-foundation-design.md#windows版のapi対応を先行する未実装)を正本とする。

既存フォーム・独自コントロールは操作と配置の移植元として、必要な機能からC#へ移す。VBからの変換だけでAPI対応や.NETの互換性確認が完了したとは扱わない。グラフ部品の選定、配布方法、対応Windowsバージョンは実装前に具体化する。

## ディレクトリ

| パス | 責務 |
| --- | --- |
| `HouseholdAccountsBeta/HouseholdAccountsBeta/Control/` | 画面、イベント、操作、集計表示 |
| `.../Data/Data/` | レシート・振替・残高などの取引オブジェクト |
| `.../Data/Master/` | 人・分類・支払方法・店舗・税率・ポイントマスタ |
| `.../Data/Statistics/` | 集計用DTO |
| `.../DataAccess/` | SQLite接続、SELECT/INSERT/UPDATE/DELETE、履歴取得 |
| `.../Logic/Calculation.vb` | 税・分類／明細参考額・残高の計算 |
| `.../Logic/IntegerExpression.vb` | 単価・値引額の整数式評価。乗算優先・入力不正や桁あふれは0 |
| `.../Logic/ReceiptAmountValidation.vb` | 既存計算を使う金額範囲の確認。反映候補と選択税設定で確認し、元モデルを変更しない |
| `.../MasterConfig/` | マスタ編集用と思われるXMLメタデータ（画面CRUDは未実装） |
| `HouseholdAccountsBeta/OriginalControls/` | 独自テキストボックスなどのコントロール |
| `src/Monetail.Api/` | ASP.NET Core API。現行契約は[43](43-passkey-authentication-design.md)、家計簿設計は[58](58-ledger-foundation-design.md) |
| `src/Monetail.Api/Authentication/` | 人物に依存しない認証処理と認証情報・発行履歴の保存モデル |
| `src/Monetail.Api/Features/Accounts/` | 家計簿から独立した全体管理者向けアカウント一覧・初回発行・再発行API。登録・起動はProgram.csで構成 |
| `tests/Monetail.Api.Tests/` | APIのHTTP統合テストとDocker起動確認 |
| `Dockerfile` / `compose.yaml` | APIだけのビルド・非root起動とループバックへのポート公開 |
| `tests/` | 実製品ソースを使う計算・Windows永続化／画面テスト、SQLite移行テスト |
| `.github/workflows/tests.yml` | Linux／Windowsのテストと既存ソリューションのWindowsビルド |
| `.github/scripts/` | Git差分・テストの依存先からCI実行対象を選ぶ処理と回帰テスト |

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

更新・削除では、対象者割当→明細→グループ→（削除時のみ）レシート親の順で削除し、既存トランザクションでDB変更を確定・巻き戻す。外部キーON/OFFをテスト用接続で検証しているが、製品の外部キー設定は変更していない。

残高削除は`DataAccess.DeleteBalance`で`t_balance_groups`→`t_balances`の順に行う。既存トランザクション・DDL・画面操作を維持し、FK ON/OFFで成功と親削除失敗時の巻き戻し・同じDataAccessでの再削除を検証する（LEG-027）。

所在地とレシート／グループ／明細タグのNULLは読込時だけ `String.Empty` に変換する。一覧・残高の両ヘッダー読込と子の読込が対象で、読込・取消はDBのNULLを保存し直さない。後から明示的に保存した場合は、従来の保存SQLで空文字を書き込む。

残高・支払方法別残高・振替のタグ3列も6箇所でNULLだけを`String.Empty`へ変換し、非NULL文字列・日時・金額・評価額・支払方法と保存済みNULLを維持する（LEG-028）。

明細の割引率は共通読込・子再読込でGetDecimalによりDecimalモデルへ取得する。SQLiteのINTEGER宣言でも小数はREALとして保存される。旧GetInt32の例外をGetDoubleで修正した後、整数境界の誤差を防ぐため入力・モデル・明細計算をDecimalへ統一した。DB定義・保存SQL・操作は維持する。[41 明細のDecimal計算](41-decimal-discount-calculation.md)。

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

[Testsワークフロー](../.github/workflows/tests.yml)は変更ファイルとテストの参照先に応じて、Linuxの計算・移行、Windowsの実SQLite／画面・既存ビルド、レシート読込性能、API・Dockerの検証を選んで実行する。Markdownだけの変更では重い検証を省略し、未分類の変更は選択処理で停止する。選択規則・自動テストの構成・件数・最新CI結果は[tests/README.md](../tests/README.md)を参照する。

Windowsテストは製品と同じSQLiteプロバイダー、実際の `DataAccess`・`RegisterForm`・独自コントロールを使い、一時DBだけで動作する。税端数の保存・履歴・プレビュー・即時流用、外部キーON/OFFのレシート更新／削除と失敗時DB復元、残高削除の成功・巻き戻しと再削除、レシートのNULL読込・画面取消、小数割引率の新規・更新と3読込経路、支払メモ一括保存の成功・末尾INSERT／UPDATE失敗時の全件復元と同接続での再保存・空リストを検証する。残高・振替のNULLタグ読込と両AutoBalanceの3チェックも成功した。テストの実行方法と手動確認手順は [tests/README.md](../tests/README.md) を参照。

競合の3チェックは、同じ一時ファイルへの2接続で採番の直列化・書込ロック失敗と再試行・古い編集内容の拒否を検証する。親3表の更新版を読込・保存し、競合時は警告と再読込フラグを返す。[保存契約](18-stale-edit-protection.md)参照。

## APIの責務と依存

| 配置 | 責務 |
| --- | --- |
| `Authentication` | パスキー・Cookie／OAuth・セッション・失効と認証用保存モデル。家計簿の機能フォルダを参照しない |
| `Features/Accounts` | 全体のアカウント一覧・初回発行・再発行。認証基盤を使う |
| `Features/Monetail/Ledgers` | 家計簿作成・参加範囲内の一覧／参照と、その保存モデル・制約。人物対応・業務入力は後続 |
| `Persistence` | API全体のDbContext・EF移行・明示初期化と構造検証。認証・家計簿のモデルを構成し、同じ初回定義で管理 |
| `Program.cs` | 認証と各機能の登録・起動・管理コマンドの受付。家計簿の保護経路を認証のHTTP境界へ指定する |

別アプリは独立したアカウントを持ち、流用するのは認証コードとする。アカウント・鍵・セッション・権限の共有やSSOは採用しない。認証ライブラリの別プロジェクト化は必要時に検討する。

現行API契約は[43](43-passkey-authentication-design.md)、起動・運用は[42](42-api-foundation.md)、家計簿のAPIと後続設計は[58](58-ledger-foundation-design.md)。優先順位は[07](07-roadmap.md)だけに記載する。

## 関連する詳細資料

- [09 操作ガイド](09-user-guide.md) — 画面の入出力・利用者の操作
- [10 DBリレーション・データフロー](10-data-flow.md) — ER図、SQL読込・保存・更新・削除
- [11 DB検査・バックアップ](11-db-audit.md) — 調査用SQLと安全な確認手順
- [42 APIの土台](42-api-foundation.md) — API単独の起動・Docker・CIと未実装の境界

## 根拠

[ソリューション](../HouseholdAccountsBeta/HouseholdAccountsBeta.sln)、[プロジェクト](../HouseholdAccountsBeta/HouseholdAccountsBeta/HouseholdAccountsBeta.vbproj)、[設定](../HouseholdAccountsBeta/HouseholdAccountsBeta/App.config)、[依存パッケージ](../HouseholdAccountsBeta/HouseholdAccountsBeta/packages.config)

レシートの`HasRequiredReceiptInput`は両ボタンの状態と`RegisterReceipt`の入口で共用する。店名のTextChangedも状態を更新し、不備のある入力で確認・Seq変更・ヘッダー転記へ進まない。[30 仕様](30-receipt-input-validation.md)。

`DetailViewForm.ShowGroupStatistics`はOverflowExceptionだけを表内表示へ変換し、前回／途中の行を消す。読込・計算をTryで囲み、Finallyで行高・列幅・見出しの自動調整と品名の幅上限を復元する。金額以外の例外は伝播する。[31 仕様](31-detail-view-amount-overflow.md)。

`ViewForm_List.vb`の非公開ReceiptListRowは元Receiptを参照し、TotalのOverflowExceptionだけを表示文字列へ変換する。その他の列と編集先は同じReceiptを参照し、金額を固定コピーしない。Designerのバインド名と正常なN0書式を維持する。[32 仕様](32-receipt-list-amount-overflow.md)。

明細の値引後金額はReceiptDetail.CalculateDiscountedFeeで計算する。Receiptの合計・人別参考額とCalculationの分類／明細参考額は、このDecimalの共通処理を呼び出してから従来の税処理・集計・人数按分を行う。計算結果を明細へ保存・キャッシュしない。
