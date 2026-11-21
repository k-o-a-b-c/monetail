# 文書整理の一次棚卸し・移行計画（一時文書）

対象: `main` の `7703f6500f69`（2026-11-22）。`docs/`内のMarkdown **103件、計1,837,800バイト**。外部の`AGENTS.md`・`UI_DESIGN.md`・`tests/`・Skillはこの103件に含めない。

**目的**: ファイル数の削減自体より、Codexが現行の正本だけを最小の参照量で見つけられ、旧仕様・実装履歴・未決定を混同しないようにする。このファイル自体は移行の作業台帳であり、整理完了後に削除する。**既存の業務仕様やAPI・DB・認証・セキュリティの契約を変更する提案ではない。**

## 分類と調査の範囲

| 一次分類 | 件数 | 意味 |
| --- | ---: | --- |
| 仕様・契約 | 57 | 旧版または新版の現在参照すべき仕様、契約、構成。内容の全行が現行仕様とは限らない |
| 入口・運用 | 18 | 索引、実装工程、手順、ロードマップ、テスト・画面台帳 |
| 履歴・比較 | 12 | 決定経緯、当時の検証記録、旧新比較。確定事項の根拠も含みうる |
| 互換案内 | 16 | 移動先だけを案内する旧パスの文書。削除承認ではない |

分類はリポジトリのファイル一覧、[INDEX.md](INDEX.md)、主要文書の本文・冒頭、既知の移動案内を基にした**一次判定**。全103件の各本文・全リンクを精査して統合可否を確定したものではない。以下の「維持」「候補」は仕様上の採否・移行の承認を意味しない。

## 全103件の分類

### 仕様・契約（57件）

| 文書 | 整理方針（一次案） |
| --- | --- |
| [`01-features.md`](01-features.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`02-database.md`](02-database.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`03-calculations.md`](03-calculations.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`10-data-flow.md`](10-data-flow.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`12-calculation-contract.md`](12-calculation-contract.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`15-shop-tax-rounding-design.md`](15-shop-tax-rounding-design.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`18-stale-edit-protection.md`](18-stale-edit-protection.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`19-database-startup-settings.md`](19-database-startup-settings.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`21-end-of-day-balance.md`](21-end-of-day-balance.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`22-person-group-statistics.md`](22-person-group-statistics.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`23-detail-input-validation.md`](23-detail-input-validation.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`24-integer-money-expressions.md`](24-integer-money-expressions.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`25-receipt-amount-overflow.md`](25-receipt-amount-overflow.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`26-statistics-amount-overflow.md`](26-statistics-amount-overflow.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`27-balance-amount-overflow.md`](27-balance-amount-overflow.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`28-transfer-input-validation.md`](28-transfer-input-validation.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`29-balance-input-state.md`](29-balance-input-state.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`30-receipt-input-validation.md`](30-receipt-input-validation.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`31-detail-view-amount-overflow.md`](31-detail-view-amount-overflow.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`32-receipt-list-amount-overflow.md`](32-receipt-list-amount-overflow.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`35-batch-receipt-reads.md`](35-batch-receipt-reads.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`36-batch-payment-receipt-reads.md`](36-batch-payment-receipt-reads.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`37-receipt-child-reload-state.md`](37-receipt-child-reload-state.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`38-tax-default-selection.md`](38-tax-default-selection.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`39-receipt-saved-total-comparison.md`](39-receipt-saved-total-comparison.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`40-decimal-tax-calculation.md`](40-decimal-tax-calculation.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`41-decimal-discount-calculation.md`](41-decimal-discount-calculation.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`42-api-foundation.md`](42-api-foundation.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`59-input-controls-design.md`](59-input-controls-design.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`architecture-api.md`](architecture-api.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`architecture-legacy.md`](architecture-legacy.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`architecture-winui.md`](architecture-winui.md) | 最優先: 現在の責務・構成とPRごとの作業経緯を分離。契約・例外条件を維持 |
| [`auth-api-contract.md`](auth-api-contract.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`auth-passkeys.md`](auth-passkeys.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`auth-session-security.md`](auth-session-security.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`auth-windows-oauth.md`](auth-windows-oauth.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-balance-api.md`](ledger-balance-api.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-core-api.md`](ledger-core-api.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-core-principles.md`](ledger-core-principles.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-core-schema.md`](ledger-core-schema.md) | 優先: 科目/税率/DB仕様の重複・実装履歴を抽出。項目・制約・確定状態は保持 |
| [`ledger-core-transition.md`](ledger-core-transition.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-invitations.md`](ledger-invitations.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-onboarding.md`](ledger-onboarding.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-operation-retries.md`](ledger-operation-retries.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-participants.md`](ledger-participants.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-person-api-contract.md`](ledger-person-api-contract.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-person-conflicts.md`](ledger-person-conflicts.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-person-db.md`](ledger-person-db.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-persons.md`](ledger-persons.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-receipt-api.md`](ledger-receipt-api.md) | 優先: 現行HTTP契約・エラー・再送から実装経緯を分離 |
| [`ledger-receipt-schema.md`](ledger-receipt-schema.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-receipt-suggestions.md`](ledger-receipt-suggestions.md) | 優先: 確定した候補API/補完挙動と未採用案を分離 |
| [`ledger-receipt-ui.md`](ledger-receipt-ui.md) | 優先: 現行入力契約と工程/PRメモを区別。DEC・画面操作・401復帰を維持 |
| [`ledger-transfer-api.md`](ledger-transfer-api.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-transfer-db.md`](ledger-transfer-db.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-transfer-ui.md`](ledger-transfer-ui.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |
| [`ledger-windows.md`](ledger-windows.md) | 維持。重複・旧案・実装ログの有無を本文精査後に判断 |

### 入口・運用（18件）

| 文書 | 整理方針（一次案） |
| --- | --- |
| [`04-architecture.md`](04-architecture.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`05-issues.md`](05-issues.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`06-test-cases.md`](06-test-cases.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`07-roadmap.md`](07-roadmap.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`08-requested-enhancements.md`](08-requested-enhancements.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`09-user-guide.md`](09-user-guide.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`11-db-audit.md`](11-db-audit.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`43-passkey-authentication-design.md`](43-passkey-authentication-design.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`58-ledger-foundation-design.md`](58-ledger-foundation-design.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`architecture-repository.md`](architecture-repository.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`deployment-examples/docker-tailscale.md`](deployment-examples/docker-tailscale.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`deployment.md`](deployment.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`INDEX.md`](INDEX.md) | 正本への最短の入口として維持。リダイレクトでなく正本を優先して案内 |
| [`ledger-foundation-core.md`](ledger-foundation-core.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`ledger-person-api.md`](ledger-person-api.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`ledger-receipt-implementation.md`](ledger-receipt-implementation.md) | 優先: 実施済み工程の詳細記録を整理。必要な工程別正本への入口は維持 |
| [`ledger-transfer.md`](ledger-transfer.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |
| [`screen-inventory.md`](screen-inventory.md) | 維持。参照先の一本化、更新履歴の縮約を検討 |

### 履歴・比較（12件）

| 文書 | 整理方針（一次案） |
| --- | --- |
| [`13-glossary-decisions.md`](13-glossary-decisions.md) | 最優先: 用語集・決定履歴・未決定を分ける案を検討。DEC番号と参照アンカーを維持 |
| [`14-decisions-2026-10-16.md`](14-decisions-2026-10-16.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`16-documentation-audit.md`](16-documentation-audit.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`17-concurrent-persistence-validation.md`](17-concurrent-persistence-validation.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`20-balance-date-boundaries.md`](20-balance-date-boundaries.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`34-receipt-read-performance.md`](34-receipt-read-performance.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`history.md`](history.md) | 凍結済みのため内容は改変しない |
| [`ledger-transfer-legacy.md`](ledger-transfer-legacy.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`legacy-new-comparison.md`](legacy-new-comparison.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`legacy-new-data-calculation-audit.md`](legacy-new-data-calculation-audit.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`legacy-new-final-parity-matrix.md`](legacy-new-final-parity-matrix.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |
| [`legacy-new-winui-implementation-audit.md`](legacy-new-winui-implementation-audit.md) | 参照専用を原則。現行仕様の根拠・未決定事項との混在を点検 |

### 互換案内（16件）

| 文書 | 整理方針（一次案） |
| --- | --- |
| [`33-windows-first-web-input-priorities.md`](33-windows-first-web-input-priorities.md) | 履歴・決定記録からの参照あり。削除可否はリンク監査後 |
| [`44-auth-database-foundation.md`](44-auth-database-foundation.md) | 履歴・決定記録・テスト履歴からの参照あり。削除可否はリンク監査後 |
| [`45-initial-admin-enrollment.md`](45-initial-admin-enrollment.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`46-passkey-enrollment.md`](46-passkey-enrollment.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`47-passkey-login-sessions.md`](47-passkey-login-sessions.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`48-passkey-reauthentication.md`](48-passkey-reauthentication.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`49-authentication-revocation-recovery.md`](49-authentication-revocation-recovery.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`50-self-service-passkeys.md`](50-self-service-passkeys.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`51-admin-account-list.md`](51-admin-account-list.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`52-reusable-authentication-boundaries.md`](52-reusable-authentication-boundaries.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`53-account-person-lookup.md`](53-account-person-lookup.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`54-admin-account-creation.md`](54-admin-account-creation.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`55-admin-enrollment-code-reissue.md`](55-admin-enrollment-code-reissue.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`56-new-user-onboarding-policy.md`](56-new-user-onboarding-policy.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`57-ledger-data-ownership.md`](57-ledger-data-ownership.md) | 削除候補（未決定）。内部参照・外部アンカー・移動先を先に確認 |
| [`deployment-docker-nas.md`](deployment-docker-nas.md) | テスト資料からの参照あり。更新とリンク検査前に削除しない |

## リンク・根拠の初期確認

- `33-windows-first-web-input-priorities.md` は `docs/history.md`・`docs/13-glossary-decisions.md` から、`44-auth-database-foundation.md` はそれらに加え `tests/history.md` から参照が見つかった。
- `deployment-docker-nas.md` には `tests/README.md` からの参照がある。
- `13-glossary-decisions.md` は `AGENTS.md`・`docs/INDEX.md`・複数の機能別正本から参照される。単純な履歴ファイルとして削除できない。
- 上記はGitHubのコード検索による**部分照合**であり、網羅的な参照・アンカー検査や外部リンクの確認ではない。

## 実施順序と完了条件（各段階を独立PRにする）

1. **参照と正本の確定**：対象領域のMarkdown相対リンク、見出しアンカー、明示的な`<a id>`、`DEC-`参照、`AGENTS.md`・Skill・テスト・CI・ソースからの参照を検索する。仕様の確定・未決定・旧版・新版を混同しない。外部からの直接リンクは網羅できないため、影響が不明なら互換案内を維持する。
2. **互換案内の整理**：16件について参照元を移動先の正本へ置換し、内部リンクとアンカーを検証する。削除はリンク影響・互換要件を個別に判断してから行う。全件一括削除しない。
3. **WinUI構成と工程記録の整理**：`architecture-winui.md`を現行の構成・責務・生成/寿命・契約中心にし、PRごとの作業経緯を判別する。履歴の削除はGit履歴・PR・テスト記録だけで根拠が追えることを確認した範囲に限定する。凍結済みの`docs/history.md`を編集しない。
4. **家計簿・レシートの大容量正本の整理**：`ledger-core-schema.md`、`ledger-receipt-ui.md`、`ledger-receipt-api.md`、`ledger-receipt-suggestions.md`、実装入口を対象に、未採用案・実装経緯と現行契約を分ける。DB列・API入出力・エラー・再送・税計算・UI操作・401復帰・未保存保護は維持し、必要な領域別分割は崩さない。
5. **決定履歴と旧版比較の整頓**：`13-glossary-decisions.md`の用語集・採用済み決定・決定経緯を切り分け、DEC番号と承認根拠を保つ。新旧比較・監査類は検証済み事実と現行仕様を区別し、正本への重複記述を減らす。根拠を失う削除はしない。
6. **入口の最終簡潔化**：`docs/INDEX.md`や関連入口を実際に残る正本へつなぎ直す。この一次棚卸しは作業完了後に削除する。

各PRでは、(a) 移動前後の仕様・確定/未決定区分とDEC参照の照合、(b) 内部リンク・アンカーの整合確認、(c) 対象外のコード/API/DB変更なし、(d) 削除・圧縮した文書量と根拠を報告する。**新しい機能仕様は承認なく決定しない**。コード・スキーマはこの整理作業で変更しない。
