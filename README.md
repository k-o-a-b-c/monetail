# monetail-legacy

Monetailの**レガシー版**。購入した品物を明細単位で記録し、金額・分類・対象者（複数人）などを管理できるWindows向け家計簿アプリです。

## 現行技術構成

- **VB.NET / Windows Forms / .NET Framework 4.8**
- **SQLite**（`System.Data.SQLite.Core 1.0.113.1`）
- ソリューション: [HouseholdAccountsBeta/HouseholdAccountsBeta.sln](HouseholdAccountsBeta/HouseholdAccountsBeta.sln)
- 業務操作: レシート・振替・残高の入力、分類別／収支／残高の集計など
- **未完成**: マスタ編集画面、ポイント機能の一部

## ドキュメント

| ファイル | 内容 |
| --- | --- |
| [01 機能一覧](docs/01-features.md) | 画面・機能と実装状態 |
| [02 DB仕様](docs/02-database.md) | **実スキーマから確定した16テーブル**、90カラム・主キー・17外部キー・コード照合 |
| [実DBのDDL](docs/schema/monetail_schema.sql) | SQLiteから抽出したテーブル定義SQL（実データなし） |
| [03 計算仕様](docs/03-calculations.md) | 税・割引・端数・明細の対象者・残高 |
| [04 構成・開発環境](docs/04-architecture.md) | VB.NETプロジェクト構造、起動・DBの接続 |
| [05 未実装・課題](docs/05-issues.md) | 改修候補、データ整合性リスク、要仕様判断 |
| [06 テスト](docs/06-test-cases.md) | 手動E2Eテスト、計算のゴールデンケース |
| [07 ロードマップ](docs/07-roadmap.md) | レガシー版の完成からオンライン版再設計へ |
| [08 改修要件](docs/08-requested-enhancements.md) | 店舗別税端数の改修・オーケーは既存割引額入力を継続 |
| [09 操作ガイド](docs/09-user-guide.md) | 日々の入力・編集・集計操作、画面の色分けと入力補助 |
| [10 DBリレーション・処理フロー](docs/10-data-flow.md) | Mermaid ER図、保存・読込・削除フロー、更新境界 |
| [11 DB検査・バックアップ](docs/11-db-audit.md) | 実データを表示しない集計SQL、整合性検査と復旧リハーサル |
| [12 計算互換性](docs/12-calculation-contract.md) | 税率別集計、収支・按分・残高のコード上の計算契約 |
| [13 用語・意思決定](docs/13-glossary-decisions.md) | 現行用語、未決定事項、変更時の判断記録 |
| [14 確定した運用方針](docs/14-decisions-2026-10-16.md) | 税率別合算・店舗別端数・概算按分・オーケーは従来どおり手入力 |
| [15 レシート単位の税端数・詳細設計](docs/15-shop-tax-rounding-design.md) | 店舗マスタは任意の初期値、各レシートへ設定を保存 |
| [移行SQL](docs/migrations/20261017_01_shop_tax_rounding.sql) | `m_shop`と`t_receipts`に2列追加（未適用） |
| [税計算テストデータ](docs/tests/tax-rounding-cases.json) | 税率別合算・切捨／四捨五入の期待値（VB自動テストで利用） |

### 読む順番

- **通常の操作・現状確認**: [09 操作ガイド](docs/09-user-guide.md) → [01 機能一覧](docs/01-features.md)
- **DBとデータ保全**: [02 DB仕様](docs/02-database.md) → [10 データフロー](docs/10-data-flow.md) → [11 DB検査](docs/11-db-audit.md)
- **計算・改修**: [03 計算仕様](docs/03-calculations.md) → [12 計算互換性](docs/12-calculation-contract.md) → [08 改修要件](docs/08-requested-enhancements.md) → [14 決定事項](docs/14-decisions-2026-10-16.md) → [15 詳細設計](docs/15-shop-tax-rounding-design.md) → [06 テスト](docs/06-test-cases.md)
- **開発の進め方**: [05 課題](docs/05-issues.md) → [13 未決定事項](docs/13-glossary-decisions.md) → [07 ロードマップ](docs/07-roadmap.md)

### ドキュメントの位置付け

**2026-10-16更新。** 最初はソース静的解析でしたが、ユーザー提供のSQLite DDLを照合し、**テーブル定義・宣言型・PK・FKは実DDLで確認済み**です。実レコード、接続時のFK有効状態、データ整合性、Windowsでのビルド・起動テストは未実施。「実装確認」はソースが存在する意味であり、実機動作の保証ではありません。

このリポジトリは**レガシー版の機能完成・現行仕様の確定**が目的です。オンライン化や別言語による新規実装は、その後に検討します。

## 開発を始める場合

1. WindowsにVisual Studioと.NET Framework 4.8向けの開発コンポーネントを用意。
2. 上記`.sln`を開いてNuGetパッケージを復元し、ビルド。
3. `App.config`／`My Project/Settings.settings` にある `ConnectionString` が利用するSQLiteファイルを指しているか確認。**ソースには環境固有の絶対パスが設定されているので、まずバックアップ・テスト用DBに変更すること。**
4. SQLite実データファイルはこのリポジトリに含まれていません。テーブル定義SQLは [docs/schema/monetail_schema.sql](docs/schema/monetail_schema.sql) を参照してください。新規セットアップはマスタ初期データ等の別途準備が必要です。

これらの手順は**実機未検証**です。個人データを含むSQLiteファイルはコミットしないでください。

## レシートごとの税端数処理

税抜入力は税率ごとに合算してから、レシートで選んだ「切り捨て／四捨五入」を適用します。店舗が未登録でも利用できます。登録済み店舗を新規入力で明示選択した場合だけ、設定画面の店舗初期値をコピーします。保存済みレシートや手動変更した選択は、店舗の変更で上書きしません。オーケーの割引はこれまでどおり割引額へ手入力します。

既存DBはバックアップを取り、テスト用コピーに [移行SQL](docs/migrations/20261017_01_shop_tax_rounding.sql) を一度だけ適用してから利用してください。既存行は `floor` となり、保存済みの合計や割当を移行時に変更しません。旧アプリと新アプリでDBを共有しないでください。

[自動テストとWindowsでの検証手順](tests/README.md)を参照してください。Linuxでも実際のVB計算コードとSQLite移行を検証でき、Windowsでは保存・読込・画面操作のテストを実行できます。
