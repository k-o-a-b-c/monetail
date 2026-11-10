# レシート機能：Codex実装の入口

本書は**実装範囲と文書の読み分け**を示す作業ガイドであり、既存の業務契約を再定義しない。仕様が「確定」「提案」「未決定」のどれかを見極め、未承認部分を推測で固定しない（[AGENTS.md](../AGENTS.md)）。

## 参照先（必要な工程だけ読む）

| 実装対象 | 主な正本 | 追加で確認 |
| --- | --- | --- |
| DB4表・SQLiteモデル | [レシートDB](ledger-receipt-schema.md) | [既存の初期化方式](42-api-foundation.md#db初期化登録復旧コマンド)、[科目・支払方法モデル](ledger-core-schema.md) |
| 計算・保存/読取API | [レシートAPI](ledger-receipt-api.md) | [計算契約](12-calculation-contract.md)、[再送契約](ledger-operation-retries.md) |
| 一覧GET・カーソル | [レシートAPI](ledger-receipt-api.md) | [画面の一覧操作](ledger-receipt-ui.md) |
| 候補GET・店名補完 | [履歴候補](ledger-receipt-suggestions.md) | [レシートAPI](ledger-receipt-api.md) |
| Windows Client・WinUI | [レシート画面](ledger-receipt-ui.md) | [共通UI](../UI_DESIGN.md)、[入力コントロール](59-input-controls-design.md)、[履歴候補](ledger-receipt-suggestions.md) |

**重要：DBの運用境界。正式リリース前はEF Core Migrationを追加・適用せず、`initialize-db`が現在のEFモデルから新規・空DBへ最新スキーマを一括作成する。既存API DBを通常起動で自動改造しない。正式リリース後はMigrationを導入する（初回移行方式・運用手順はリリース準備時に確定）。** 正本は[API起動・DB初期化](42-api-foundation.md)。

## 実装PRの推奨分割

| PR単位（原則独立） | 対象範囲 | 最小の受入確認 |
| --- | --- | --- |
| 1. DBモデル・初期化 | レシート4表、複合FK/索引/CHECK、`ApiDbContext`・`DatabaseSchema`の初期化／構造検証 | 空DBの初期化と再検証、FK／CHECK、家計簿間参照拒否、**Migration未生成** |
| 2. 計算と検証 | 税率別合算、値引、Decimal計算、Int32境界、保存条件 | 正負・非課税/0%・複数税率・端数方式の固定期待値、範囲外拒否 |
| 3. 詳細GET・書込 | 詳細GET、POST/PUT/DELETE、版、`requestId`の再送保証、権限 | 原子的ロールバック、既存子ID維持、409の`latest`、DELETE再送、403/404 |
| 4. 一覧GET | 月/全期間・支払者/支払方法・店名、7フィールド、100件カーソル | 同日時ID順・ページ境界・条件不一致の`400 invalid_cursor`・家計簿分離 |
| 5. 履歴候補・補完API | `receipt-suggestions`・`receipt-autofill` | 店名完全一致／候補部分一致の区別、表記ゆれ、更新UTC順、入力・権限・エラー |
| 6. Windows Client・WinUI | 一覧、入力、候補、保存、保存不可・認証復帰 | キーボード操作、未保存保護、競合、再ログイン、無効カーソルからの先頭再取得 |

作業をまとめて1PRにせず、**既存実装のDI/DB初期化/権限構造に合わせて、工程ごとの最小テストを通してから次へ進む**。具体的な実装ファイルやテスト実行範囲は[tests/README](../tests/README.md)を参照。CI成功と実Windows画面・実運用DBでの検証は区別する。

## 未決定・後続検証（独断で確定しない）

- 物理設計文書で「提案」と記載した列・索引・制約の採用可否。DB実装に必要な未承認事項は当該工程で確認する。
- 人物別の参考額の細かな端数期待値、人物別レシート／期間合計の集計境界。
- 履歴検索のSQL性能・索引要否とURLバイト長上限。画面上の標準コントロール差異やIME／フォーカスの実機挙動。
- 旧Windows版DBの実データ移行（`total=NULL`等）。**新API DBの現行モデル初期化**と**旧Windows版からの業務データ移行**は別工程。
- リリース後のMigrationの初回導入・アップグレード／ロールバック方針はリリース準備時に設計し、正式リリース前の実装PRへ先取りしない。
