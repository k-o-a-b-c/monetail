# monetail-legacy

Monetailの**レガシー版**。購入した品物を明細単位で記録し、金額・分類・対象者（複数人）などを管理できるWindows向け家計簿アプリです。

## 現行技術構成

- **VB.NET / Windows Forms / .NET Framework 4.8**
- **SQLite**（`System.Data.SQLite.Core 1.0.113.1`）
- ソリューション: [HouseholdAccountsBeta/HouseholdAccountsBeta.sln](HouseholdAccountsBeta/HouseholdAccountsBeta.sln)
- 業務操作: レシート・振替・残高の入力、分類別／収支／残高の集計など
- **保留・未完成**: マスタ編集画面は作成を見送り。ポイント機能は一部実装のまま、採用範囲は未決定

## ドキュメント

| ファイル | 内容 |
| --- | --- |
| [01 機能一覧](docs/01-features.md) | 画面・機能と実装状態 |
| [02 DB仕様](docs/02-database.md) | 元DDLの**16テーブル・90カラム・17外部キー**と、両移行後95カラム・コード照合 |
| [実DBのDDL](docs/schema/monetail_schema.sql) | SQLiteから抽出したテーブル定義SQL（実データなし） |
| [03 計算仕様](docs/03-calculations.md) | 税・割引・端数・明細の対象者・残高 |
| [04 構成・開発環境](docs/04-architecture.md) | VB.NETプロジェクト構造、起動・DBの接続 |
| [05 未実装・課題](docs/05-issues.md) | 改修候補、データ整合性リスク、要仕様判断 |
| [06 テスト](docs/06-test-cases.md) | 実行済み自動テストと、未実施の手動E2E・実データ検証 |
| [07 ロードマップ](docs/07-roadmap.md) | レガシー版の完成からオンライン版再設計へ |
| [08 改修要件](docs/08-requested-enhancements.md) | 店舗別税端数の改修・オーケーは既存割引額入力を継続 |
| [09 操作ガイド](docs/09-user-guide.md) | 日々の入力・編集・集計操作、画面の色分けと入力補助 |
| [10 DBリレーション・処理フロー](docs/10-data-flow.md) | Mermaid ER図、保存・読込・削除フロー、更新境界 |
| [11 DB検査・バックアップ](docs/11-db-audit.md) | 実データを表示しない集計SQL、整合性検査と復旧リハーサル |
| [12 計算互換性](docs/12-calculation-contract.md) | 税率別集計、収支・按分・残高のコード上の計算契約 |
| [13 用語・意思決定](docs/13-glossary-decisions.md) | 現行用語、未決定事項、変更時の判断記録 |
| [14 確定した運用方針](docs/14-decisions-2026-10-16.md) | 税率別合算・店舗別端数・概算按分・オーケーは従来どおり手入力 |
| [15 レシート単位の税端数・詳細設計](docs/15-shop-tax-rounding-design.md) | 店舗ごとの直近の方式を初期選択、各レシートへ設定を保存 |
| [移行SQL](docs/migrations/20261017_01_shop_tax_rounding.sql) | `m_shop`と`t_receipts`に2列追加。一時DBで検証済み、実DBの適用状況は未確認 |
| [更新版移行SQL](docs/migrations/20261019_01_revision_tokens.sql) | レシート・振替・残高へ更新版3列を追加。税端数移行後に一度適用 |
| [税計算テストデータ](docs/tests/tax-rounding-cases.json) | 税率別合算・切捨／四捨五入の期待値（VB自動テストで利用） |
| [16 文書照合記録](docs/16-documentation-audit.md) | 既存17文書・関連5資料と、取り込み済み実装の照合範囲・検証結果 |
| [17 採番・更新競合の検証](docs/17-concurrent-persistence-validation.md) | PR #9時点の検証履歴 |
| [18 古い編集の上書き防止](docs/18-stale-edit-protection.md) | 更新版照合・競合時の画面・3列追加移行 |
| [19 DB接続設定](docs/19-database-startup-settings.md) | 実行用XML・パス解決・起動前のDBと移行確認 |
| [20 残高推算の日付境界](docs/20-balance-date-boundaries.md) | PR #12時点の修正前の再現記録 |
| [21 日末残高の推算期間](docs/21-end-of-day-balance.md) | 日末残高の翌日から対象日末までの集計・回帰テスト |
| [22 分類・人別集計](docs/22-person-group-statistics.md) | 対象者フィルタ・按分・丸めの現行挙動と残る仕様判断 |
| [23 明細入力の検証](docs/23-detail-input-validation.md) | 数量1以上・値引率0〜100%への自動補正と既存データ互換性 |
| [24 単価・値引額の整数式](docs/24-integer-money-expressions.md) | 加減算・乗算の優先順位、入力補正と保存 |
| [25 金額の桁あふれ](docs/25-receipt-amount-overflow.md) | 明細反映・登録前の確認、警告と入力・データの保持 |
| [26 集計の桁あふれ](docs/26-statistics-amount-overflow.md) | 分類・収支の月年集計の範囲超過表示と復帰 |
| [27 残高の桁あふれ](docs/27-balance-amount-overflow.md) | 推算欄と残高集計の範囲超過表示、転記抑止、表・グラフの復帰 |
| [28 振替の入力チェック](docs/28-transfer-input-validation.md) | 登録・流用の共通検証、不正入力時のモデル・DB保持 |
| [29 残高入力と元データ保持](docs/29-balance-input-state.md) | 全行の検証後に反映、エラー・確認取消と開き直し時の保持 |
| [30 レシートの入力チェック](docs/30-receipt-input-validation.md) | 登録・流用の必須条件の共通化、不備のある入力の確認前中止 |
| [31 集計詳細の金額範囲超過](docs/31-detail-view-amount-overflow.md) | 途中行の消去、明細表示切替による復帰と表示設定の復元 |
| [32 レシート一覧の金額範囲超過](docs/32-receipt-list-amount-overflow.md) | 該当セルだけの範囲超過表示と、絞り込み・編集の継続 |
| [33 Windows先行・Web版入力の優先順位](docs/33-windows-first-web-input-priorities.md) | Windows版を先に進め、必須のWeb化ではレシート入力→振替入力を優先 |
| [34 レシート読込の性能計測](docs/34-receipt-read-performance.md) | Windowsの一時DBで月次・年次のSQL数と読込時間を比較 |
| [35 レシート階層の一括読込](docs/35-batch-receipt-reads.md) | GetReceiptsを1 SELECTへ変更し、階層・順序・絞り込みを維持 |
| [36 支払方法別レシートの一括読込](docs/36-batch-payment-receipt-reads.md) | 残高推算向け読込も1 SELECTへ変更し、期間条件と推算順序を維持 |
| [37 レシートの子再読込とモデル保持](docs/37-receipt-child-reload-state.md) | 途中失敗では元の子・参照を保持し、成功時だけ反映 |
| [38 税率の初期選択](docs/38-tax-default-selection.md) | マスタの並び順に依存せず8%／10%を選択し、保存済み税率を保持 |
| [39 同じ分類の複数税率](docs/39-classification-tax-groups.md) | 分類と税率でグループを区別し、追加・更新・統合と保存を維持 |
| [40 明細税率への統一設計](docs/40-detail-tax-unified-design.md) | 対象外・同分類統合とDecimal計算の採用仕様 |
| [42 明細税率へのWindows版切替](docs/42-detail-tax-application.md) | 明細税設定・正確な計算・期間別初期値と未完了の受入確認 |
| [41 明細税率のコピーDB移行](docs/41-detail-tax-copy-migration.md) | 元DBを保持する移行ツール、旧新金額・キーの記録 |

### 読む順番

- **通常の操作・現状確認**: [09 操作ガイド](docs/09-user-guide.md) → [01 機能一覧](docs/01-features.md)
- **DBとデータ保全**: [02 DB仕様](docs/02-database.md) → [10 データフロー](docs/10-data-flow.md) → [11 DB検査](docs/11-db-audit.md)
- **計算・改修**: [03 計算仕様](docs/03-calculations.md) → [12 計算互換性](docs/12-calculation-contract.md) → [08 改修要件](docs/08-requested-enhancements.md) → [14 決定事項](docs/14-decisions-2026-10-16.md) → [15 詳細設計](docs/15-shop-tax-rounding-design.md) → [06 テスト](docs/06-test-cases.md)
- **開発の進め方**: [05 課題](docs/05-issues.md) → [13 未決定事項](docs/13-glossary-decisions.md) → [07 ロードマップ](docs/07-roadmap.md)

### ドキュメントの位置付け

**2026-10-19更新。** ソース、ユーザー提供の元DDL、移行SQL、自動テストを照合しています。元DDLは16テーブル・90カラム・17FKで、税端数・更新版の両移行後は95カラムです。検証範囲・CI・実行方法は[自動テスト](tests/README.md)を参照してください。利用者のWindows環境での手動操作・実レシート照合・実DBの適用状況／整合性は未確認です。

このリポジトリでは**まずWindows版を進め、現行仕様と計算・保存の互換性を整えます。後続のWeb化は必須**とし、Web版はレシート入力、次に振替入力を優先します。技術スタック・認証・DB製品等は未決定です。[33 進め方](docs/33-windows-first-web-input-priorities.md)を参照してください。

## 開発を始める場合

1. WindowsにVisual Studioと.NET Framework 4.8向けの開発コンポーネントを用意。
2. 上記`.sln`を開いてNuGetパッケージを復元し、ビルド。
3. 開発時は`App.config`、配布先はexe横の`HouseholdAccountsBeta.exe.config`の`ConnectionString`を、バックアップしたテスト用SQLiteへ設定する。既定はEドライブ。相対パスはexe基準で解決し、不在・不正DB・移行不足では起動を中止する。[設定手順](docs/19-database-startup-settings.md)を参照。
4. SQLite実データファイルはこのリポジトリに含まれていません。テーブル定義SQLは [docs/schema/monetail_schema.sql](docs/schema/monetail_schema.sql) を参照してください。新規セットアップはマスタ初期データ等の別途準備が必要です。

NuGet復元・ビルドと一時DBでの画面自動テストはWindows CIで確認済みです。利用者の端末・実DBでのセットアップは未確認です。個人データを含むSQLiteファイルはコミットしないでください。

## レシートごとの税端数処理

税抜入力は税率ごとに合算してから、レシートのラジオボタンで選んだ「切り捨て／四捨五入」を適用します。店舗が未登録でも利用できます。新規入力で店舗を選ぶか店名を入力して確定すると、その店舗で現在の利用者が使った直近レシートの方式を初期選択します。履歴がない店舗は切り捨てです。マスタメンテナンス画面は今回実装しません。保存済みレシートや手動変更した選択は、店舗の変更で上書きしません。内税／外税と端数方式は編集中の画面で保持し、登録確認後にだけReceiptへ反映します。合計表示は画面の選択値で即時再計算し、キャンセル時はReceiptの両設定を変更しません。オーケーの割引はこれまでどおり割引額へ手入力します。

既存DBはバックアップを取り、テスト用コピーに [移行SQL](docs/migrations/20261017_01_shop_tax_rounding.sql) を一度だけ適用してから利用してください。既存行は `floor` となり、保存済みの合計や割当を移行時に変更しません。旧アプリと新アプリでDBを共有しないでください。

[自動テストとWindowsでの検証手順](tests/README.md)を参照してください。Linuxでも実際のVB計算コードとSQLite移行を検証でき、Windowsでは保存・読込・画面操作のテストを実行できます。

## 採番・更新競合の検証

ローカル一時DBの2接続では、レシート・振替・残高の採番は直列化され、ロック失敗後も保存内容を保ったまま再試行できました。古い編集は更新版を照合して更新・削除を拒否し、競合時は画面と入力を保持します。閉じた後は一覧を読み直します。[保存契約・移行手順](docs/18-stale-edit-protection.md)を参照してください。

## 更新・削除とNULL文字列

レシート更新では対象者割当→明細→グループの順に子を削除して再登録し、削除操作では最後にレシート親を削除します。既存のトランザクションを維持し、途中失敗時はDBの変更全体を戻します。テスト用接続のFK ON/OFFで確認済みですが、製品の接続設定やDB定義は変更していません。

残高削除も、支払方法別の子（`t_balance_groups`）→親（`t_balances`）の順へ修正しました。既存トランザクションと画面操作を維持し、FK ON/OFFで削除成功（子0／2件）・親削除失敗時の巻き戻しと同じDataAccessでの再削除（子2件）、他の残高と無関係な全テーブルの保持を自動検証しています（LEG-027）。

所在地とレシート・グループ・明細のタグがSQL NULLでも、一覧・残高・編集画面では空欄として読み込めます。読込・画面取消では保存済みNULLや金額を変更しません。明示保存した場合は既存の保存処理で空文字を保存します。必須の店名・明細名等の読込条件は変更していません。

残高・支払方法別残高・振替の任意タグ3列も、6箇所の読込でSQL NULLだけを空文字として読み込めます（LEG-028）。空文字・通常の文字・日本語・引用符・空白、日時・金額・評価額・支払方法と保存済みNULLを保持し、DB移行・保存SQL・計算式・画面操作は変更しません。

取り込み済みの変更は[税端数処理（PR #1）](https://github.com/k-o-a-b-c/monetail-legacy/pull/1)、[外部キー対応（PR #2）](https://github.com/k-o-a-b-c/monetail-legacy/pull/2)、[NULL文字列対応（PR #3）](https://github.com/k-o-a-b-c/monetail-legacy/pull/3)、[文書整備（PR #4）](https://github.com/k-o-a-b-c/monetail-legacy/pull/4)です。

この版では、[小数割引率の読込（PR #5）](https://github.com/k-o-a-b-c/monetail-legacy/pull/5)も修正しています。明細の読込型をモデルと同じ`Double`へ合わせ、保存した小数率を読み込めるようにしました。DB定義・保存処理・割引計算式・画面操作は従来どおりです。

[支払メモの一括保存（PR #6）](https://github.com/k-o-a-b-c/monetail-legacy/pull/6)は、渡された全行の追加・更新を1つのトランザクションで行います。全件成功時だけ確定し、途中失敗時は先行する行の変更も巻き戻して例外を呼出元へ伝えます。空リストでは何も保存しません。入力・終了時の確認操作、保存順、日付・メモの扱いは従来どおりです。残る課題は[05 課題一覧](docs/05-issues.md)を参照してください。
