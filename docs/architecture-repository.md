# リポジトリ内の配置・CI・関連資料

[← 04 全体構成の入口](04-architecture.md)

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
| `src/Monetail.Calculations/` | API/Windows Clientが同じ実装を使う.NET 10のレシート計算・入力検証・結果モデル。BCL以外に依存しない |
| `src/Monetail.WinUI/` | Windows 11向けアプリ。ホーム・左ナビゲーションと右側の人物一覧。ClientConnectionで既存Clientを構成し、MainWindowの接続・認証・家計簿操作を分担する |
| `tests/Monetail.WinUI.Smoke/` | 製品の起動と、同じXAML・コードを使った合成データの画面スモーク。単発画像の取得も担当 |
| `src/Monetail.Windows.Client/` | UIから独立したHTTPS通信・OAuth・セッション復元・設定とDPAPI保存・家計簿操作と起動先判定・人物管理の通信・画面に依存しない日付／時刻の補完 |
| `tests/Monetail.Windows.Client.Tests/` / `tests/Monetail.Windows.Storage.Tests/` | クライアントの合成HTTP・ループバック・保存テストと、Windows上の実DPAPI検証 |
| `src/Monetail.Api/Authentication/` | 人物に依存しない認証処理と認証情報・発行履歴の保存モデル |
| `src/Monetail.Api/Features/Accounts/` | 家計簿から独立した全体管理者向けアカウント一覧・初回発行・再発行API。登録・起動はProgram.csで構成 |
| `tests/Monetail.Api.Tests/` | APIのHTTP統合テストとDocker起動確認 |
| `Dockerfile` / `compose.yaml` | APIだけのビルド・非root起動とループバックへのポート公開 |
| `tests/` | 実製品ソースを使う計算・Windows永続化／画面テスト、SQLite移行テスト |
| `.github/workflows/tests.yml` | Linux／Windowsのテストと既存ソリューションのWindowsビルド |
| `.github/scripts/` | Git差分・テストの依存先からCI実行対象を選ぶ処理と回帰テスト |


## 自動検証

[Testsワークフロー](../.github/workflows/tests.yml)は変更ファイルとテストの参照先に応じて、Linuxの計算・移行、Windowsの実SQLite／画面・既存ビルド、レシート読込性能、API・Dockerの検証を選んで実行する。Markdownだけの変更では重い検証を省略し、未分類の変更は選択処理で停止する。選択規則・自動テストの構成・件数・最新CI結果は[tests/README.md](../tests/README.md)を参照する。

旧Windows版の検証対象の詳細は[旧Windowsの構成](architecture-legacy.md#旧windows版の自動検証の詳細)を参照する。


## 関連する詳細資料

- [09 操作ガイド](09-user-guide.md) — 画面の入出力・利用者の操作
- [10 DBリレーション・データフロー](10-data-flow.md) — ER図、SQL読込・保存・更新・削除
- [11 DB検査・バックアップ](11-db-audit.md) — 調査用SQLと安全な確認手順
- [42 APIの土台](42-api-foundation.md) — API単独の起動・Docker・CIと未実装の境界

