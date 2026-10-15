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
| [08 改修要件](docs/08-requested-enhancements.md) | 店舗別の端数処理と3/103割引（要件案・未実装） |

### ドキュメントの位置付け

**2026-10-16更新。** 最初はソース静的解析でしたが、ユーザー提供のSQLite DDLを照合し、**テーブル定義・宣言型・PK・FKは実DDLで確認済み**です。実レコード、接続時のFK有効状態、データ整合性、Windowsでのビルド・起動テストは未実施。「実装確認」はソースが存在する意味であり、実機動作の保証ではありません。

このリポジトリは**レガシー版の機能完成・現行仕様の確定**が目的です。オンライン化や別言語による新規実装は、その後に検討します。

## 開発を始める場合

1. WindowsにVisual Studioと.NET Framework 4.8向けの開発コンポーネントを用意。
2. 上記`.sln`を開いてNuGetパッケージを復元し、ビルド。
3. `App.config`／`My Project/Settings.settings` にある `ConnectionString` が利用するSQLiteファイルを指しているか確認。**ソースには環境固有の絶対パスが設定されているので、まずバックアップ・テスト用DBに変更すること。**
4. SQLite実データファイルはこのリポジトリに含まれていません。テーブル定義SQLは [docs/schema/monetail_schema.sql](docs/schema/monetail_schema.sql) を参照してください。新規セットアップはマスタ初期データ等の別途準備が必要です。

これらの手順は**実機未検証**です。個人データを含むSQLiteファイルはコミットしないでください。
