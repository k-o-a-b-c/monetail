# 04 レガシー版 アーキテクチャ・開発環境

更新日: 2026-10-15 / ソース静的調査。ビルド・起動は未検証。

## 構成

```text
Windows デスクトップアプリ
  ViewForm / Register*Form / DetailViewForm（Windows Forms UI）
       ↓
  Receipt・Balance・Transfer・Master（VB.NETデータクラス）
       ↓
  Calculation（計算処理）
       ↓
  DataAccess + DataAccessList（System.Data.SQLite、SQL直書き）
       ↓
  ローカル SQLite ファイル
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
| `.../MasterConfig/` | マスタ編集用と思われるXMLメタデータ（画面CRUDは未実装） |
| `HouseholdAccountsBeta/OriginalControls/` | 独自テキストボックスなどのコントロール |

## 代表的な処理フロー

1. 起動: `ViewForm.New` → `DataAccess(My.Settings.ConnectionString)` → SQLite接続オープン。
2. マスタ: 人／支払方法／店舗／税率等をSELECTして画面の選択肢を生成。マスタの一部は初回読込後に保持。
3. レシート保存: `RegisterForm` が画面入力を `Receipt` と子リストへ反映 → `DataAccess.RegisterReceipt / UpdateReceipt`。
4. レシート参照: `GetReceipts` → `LoadReceiptGroupData` → `LoadReceiptDetailData` → `LoadReceiptDetailUsersData` の順に読み込む。
5. 集計: 画面内で年次データを取得し、`Receipt` の計算プロパティと `Calculation` で集約。
6. 振替／残高: 専用フォームから `DataAccess` の各登録メソッドを呼ぶ。

## 起動・開発上の注意

1. WindowsとVisual Studio（.NET Framework 4.8用の開発機能）が必要。
2. `.sln` を開きNuGetパッケージを復元してビルドする。**手順はソース上の構成から推定したもので、実際のビルド確認は未実施**。
3. `My.Settings.ConnectionString` はSQLiteのファイルパス。`App.config` と `Settings.settings` には**異なるドライブの絶対パス**が入っており、別環境でそのまま起動できない可能性が高い。作業用DBのパスを必ず確認する。
4. レポジトリにSQLiteの実DBファイルとスキーマ初期化DDLは確認できなかった。新規環境はDBの複製またはDDLの用意が必要。
5. 実データはGitHubにコミットしない。設定の汎用化・サンプルDBの匿名化が必要。
6. Windowsログイン名 `Environment.UserName` で個人識別を一部行う。アプリ認証・家計ごとの権限チェックとは異なる。
7. テストプロジェクト／CIワークフローは調査時点のツリーでは確認できない。

## オンライン化に向けた境界

将来の構成案（**現行実装ではない**）:

```text
Web / スマートフォン / デスクトップ
             ↓ 認証付き HTTPS
             API・認可・業務ロジック
             ↓
       Cloudflare D1 等
```

レガシー版のUIから計算ルール・永続化条件を切り離し、[03-calculations.md](03-calculations.md) と [06-test-cases.md](06-test-cases.md) を移植時の契約として使う。

## 根拠

[ソリューション](../HouseholdAccountsBeta/HouseholdAccountsBeta.sln)、[プロジェクト](../HouseholdAccountsBeta/HouseholdAccountsBeta/HouseholdAccountsBeta.vbproj)、[設定](../HouseholdAccountsBeta/HouseholdAccountsBeta/App.config)、[依存パッケージ](../HouseholdAccountsBeta/HouseholdAccountsBeta/packages.config)
