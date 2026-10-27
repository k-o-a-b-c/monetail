# 文書の入口と更新先

**機能を変えるときは、対応する正本だけを更新する。** README・ロードマップ・決定履歴へ同じ仕様や「ここまで実装した」という文章を転記しない。リンク先が正しければ整合性は保てる。

| 内容 | 正本・更新先 |
| --- | --- |
| 次の優先順位・残る作業 | [07 ロードマップ](07-roadmap.md) |
| 認証・全体アカウント管理の現行契約 | [43 認証](43-passkey-authentication-design.md) |
| 家計簿・新規導入の仕様、技術案、未決定 | [58 家計簿設計](58-ledger-foundation-design.md) |
| API起動・設定・移行・復旧の操作 | [42 起動・運用](42-api-foundation.md) |
| コードの責務・配置 | [04 構成](04-architecture.md) |
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
