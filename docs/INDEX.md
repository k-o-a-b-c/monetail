# 文書の入口と更新先

**機能を変えるときは、対応する正本だけを更新する。** README・ロードマップ・決定履歴へ同じ仕様や「ここまで実装した」という文章を転記しない。リンク先が正しければ整合性は保てる。

| 内容 | 正本・更新先 |
| --- | --- |
| 次の優先順位・残る作業 | [07 ロードマップ](07-roadmap.md) |
| 認証設計の入口・旧見出し | [43 認証](43-passkey-authentication-design.md) |
| アカウント・パスキー・登録・全失効と復旧 | [認証・パスキー](auth-passkeys.md) |
| セッション期限・Cookie・CSRF | [セッション保護](auth-session-security.md) |
| WindowsのOAuth・DPAPI・トークン保存 | [Windows認証](auth-windows-oauth.md) |
| 認証・管理APIの入出力と再送 | [認証API](auth-api-contract.md) |
| 家計簿設計の入口・旧リンク | [58 家計簿設計](58-ledger-foundation-design.md) |
| 家計簿共通設計の入口・旧見出し | [共通仕様](ledger-foundation-core.md) |
| 家計簿の前提・確定済みの基本ルール | [共通前提](ledger-core-principles.md) |
| 業務モデル・家計簿DBの構成 | [DB・モデル](ledger-core-schema.md) |
| 共通税率履歴GET・Clientメモリキャッシュ | [共通税履歴](ledger-core-schema.md#共通税率履歴の全件getメモリキャッシュ) |
| レシート4表の列・複合FK・CHECK・索引 | [レシートDB](ledger-receipt-schema.md) |
| レシート一覧・詳細・書込・競合・再送API | [レシートAPI](ledger-receipt-api.md) |
| レシート専用入力・一覧・保存不可時のWinUI | [レシート画面](ledger-receipt-ui.md) |
| 履歴候補検索・店名補完と候補UI | [レシート履歴候補](ledger-receipt-suggestions.md) |
| Codex向け実装工程・参照文書・受入確認 | [レシート実装の入口](ledger-receipt-implementation.md) |
| 家計簿の作成・参照・回数制限 | [家計簿API](ledger-core-api.md) |
| 既存DB移行・後続の確認事項 | [移行と後続](ledger-core-transition.md) |
| 科目のDB・API・管理画面 | [科目管理](ledger-core-schema.md#category-master) |
| 人物仕様・人物画面 | [人物管理](ledger-persons.md) |
| 参加者・人物対応 | [参加者管理](ledger-participants.md) |
| 人物管理API・DBの入口・旧リンク | [人物API](ledger-person-api.md) |
| 人物・参加者のAPIとJSON契約 | [API契約](ledger-person-api-contract.md) |
| 人物・参加者のテーブルと更新版 | [人物DB](ledger-person-db.md) |
| 作成要求の再送と保持条件 | [共通再送](ledger-operation-retries.md) |
| 人物・参加者の競合・権限喪失・後続画面 | [競合と後続](ledger-person-conflicts.md) |
| 登録・ログイン・家計簿ホーム | [新規利用](ledger-onboarding.md) |
| 家計簿への招待・参加 | [招待](ledger-invitations.md) |
| Windows版の接続・画面遷移 | [Windows接続](ledger-windows.md) |
| API起動・設定・DB初期化・復旧の操作 | [42 起動・運用](42-api-foundation.md) |
| 通信・認証・永続化・更新に関する配備共通要件 | [API配備・運用](deployment.md) |
| Git不要のGHCR配布とDockerサーバーの具体的な接続手順 | [Docker配備例](deployment-examples/docker-tailscale.md) |
| 構成の入口・旧見出しリンク | [04 構成](04-architecture.md) |
| 旧VB.NET・Windows Forms・SQLite・旧版の処理 | [旧Windows版](architecture-legacy.md) |
| 旧Windows版と新版の操作・機能差異と未決定事項 | [旧版／新版の比較](legacy-new-comparison.md) |
| 旧版／新版の計算・DB・移行時の値域と非互換点の監査 | [計算・DB・移行境界の照合](legacy-new-data-calculation-audit.md) |
| 旧WinFormsと現WinUI／Clientの実装差・テスト・未接続機能 | [旧版／新WinUIの実コード監査](legacy-new-winui-implementation-audit.md) |
| 旧版全機能と新版の最終対応58項目・実装状況・未決定・検証条件 | [新旧機能横断・最終比較台帳](legacy-new-final-parity-matrix.md) |
| 新Windows版の構成・WinUI 3・Client | [新Windows版](architecture-winui.md) |
| APIの責務・依存・後続分離案 | [API構成](architecture-api.md) |
| リポジトリ配置・CI・関連資料 | [配置とCI](architecture-repository.md) |
| 新Windows版の共通UI・配置・フォント・高DPI基準 | [UI_DESIGN.md](../UI_DESIGN.md) |
| 新Windows版のキー操作・日付と時刻の入力形式・補完 | [59 入力コントロール設計](59-input-controls-design.md) |
| 検証件数・実行方法・採用する結果 | [tests/README](../tests/README.md) |

仕様を変更したら該当の正本、テスト構成や採用結果が変わったらtests/READMEを更新する。優先順位が変わらなければ07を更新しない。重要な新しい仕様判断だけ[13 決定履歴](13-glossary-decisions.md)へ追記し、過去の状態を遡って直さない。新しい改修ごとに番号付き文書を増やさない。

## Windows版を調べる場合

- 操作: [09 ガイド](09-user-guide.md)／[01 機能](01-features.md)
- DB: [02 定義](02-database.md)／[10 データフロー](10-data-flow.md)／[11 検査・バックアップ](11-db-audit.md)
- 計算: [12 計算契約](12-calculation-contract.md)／[03 計算の説明](03-calculations.md)
- 要望・課題: [08 Windows要件](08-requested-enhancements.md)／[05 課題](05-issues.md)
- 個別仕様: 税端数[15](15-shop-tax-rounding-design.md)、入力[23](23-detail-input-validation.md)・[24](24-integer-money-expressions.md)、設定[19](19-database-startup-settings.md)、保存競合[18](18-stale-edit-protection.md)、保存時合計[39](39-receipt-saved-total-comparison.md)、Decimal[40](40-decimal-tax-calculation.md)・[41](41-decimal-discount-calculation.md)。その他は検索で必要な箇所だけ読む。

Windowsの個別仕様も、該当の契約・詳細だけを更新し、概要文書全てへの転記は不要。

## 必要なときだけ読む履歴

[決定履歴13](13-glossary-decisions.md)／[当初の判断14](14-decisions-2026-10-16.md)／[文書照合16](16-documentation-audit.md)／[実装・設計履歴](history.md)／[検証履歴](../tests/history.md)。過去の件数・仕様を現状と混同しない。

旧33・44〜57は互換リンクを残す案内文に置き換えた。元の実装記録や設計は履歴へ保持し、今後の通常の改修では更新しない。
