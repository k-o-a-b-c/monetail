# 05 未実装機能・既知の課題・確認待ち

更新日: 2026-10-16 / ソース静的調査とユーザー提供の**実SQLiteスキーマDDL**に基づく課題候補。実機で再現したバグと混同しないこと。

## 判定基準

- **確認済みの実装欠落**: ソース上、動作本体の実装が見当たらない。
- **コード上のリスク**: 不具合を誘発する可能性があるが、実DB／実機で未再現。
- **要仕様判断**: 正しい業務ルールを決める必要がある。
- 優先度: P0=データ保全・整合性、P1=最低限完成に必要、P2=運用改善、P3=将来。「対象外」は追加開発しない方針が確定した履歴項目。

| ID | 優先度 | 状態 | 事項 | 根拠・判定内容 |
| --- | --- | --- | --- | --- |
| LEG-001 | P1 | 実装欠落 | マスタメンテナンス画面が空 | `MasterMaintenanceForm.vb` はプロパティのみ、Designerは空フォーム。分類・支払方法・店・税率の編集機能を実装するか方針確定 |
| LEG-002 | P1 | 一部実装 | ポイント計算・保存が未完成 | **`t_receipt_points` は実DBに存在**。`ReceiptPoint`、ポイントマスタ取得とUI部品もあるが、イベントの一部がコメントアウト、`DataAccess` にレシートポイント読込／保存SQLを確認できない |
| LEG-003 | P0 | コード上のリスク | 同時採番競合 | レシート・振替・残高の `MAX(seq)+1`。複数起動／複数端末の同時書込試験が必要 |
| LEG-004 | P0 | **DDLで制約確認・実機未再現** | DB外部キー制約と削除順が不整合 | 複合FK・親子FKが実DDLで確認済み。DBはCASCADEなし。一方 `DeleteReceiptGroups` はグループ→明細→対象者、`DeleteReceipt` はその前に親をDELETE。**FK ONで失敗する可能性**。アプリ接続の `PRAGMA foreign_keys` と実行テストが必要 |
| LEG-005 | P1 | **DDL確認・実機未検証** | 割引率のDB型・読込 | **`t_receipt_details.discount_rate INTEGER NOT NULL`**、モデルは `Double`、読込は `GetInt32(7)`。SQLiteの型親和性で実値が小数REALになり得るため往復テストが必要 |
| LEG-006 | P1 | 要仕様判断 | 端数処理の不一致 | レシート合計は税率単位で床関数、グループ／明細詳細は別粒度で床関数。総額不一致の例がある |
| LEG-007 | P1 | コード上のリスク | 接続文字列が環境依存 | `App.config` と `Settings.settings` に異なる絶対ドライブパス。初回セットアップ不可の可能性 |
| LEG-008 | P1 | **DDL取得済み・データ未検証** | 既存DBの物理構造・初期データの確認 | ユーザー提供の実DDLを `docs/schema/monetail_schema.sql` に保存。**16テーブル／90列／17FK**を確認。初期データ・既存行・FK有効状態・実インデックスのチェックは残る |
| LEG-009 | P1 | 未確認 | 自動テスト・CIがない | 調査時点のツリーにテストプロジェクト／ワークフローなし。テスト環境整備 |
| LEG-010 | P1 | コード上のリスク | 業務データとWindowsアカウントの結合 | `Environment.UserName` をマスタ候補・更新者に利用。マシン移転や家族共有時に要検証 |
| LEG-011 | P1 | 一部決定・残課題 | 対象者なし・人数按分 | **配分額は参考値、端数は四捨五入**と決定。対象者0人の明細が按分モードで除外される現行動作は別途確認 |
| LEG-012 | P2 | コード上のリスク | 支払メモ保存が一括不可逆 | `RegisterPaymentMemo` は複数行更新するが明示トランザクションなし。途中失敗時に一部のみ更新され得る |
| LEG-013 | P2 | 要仕様判断 | 税率初期選択が固定 | 2019-10-01で分岐し、税率マスタの並び順に依存。将来税率改定で要修正 |
| LEG-014 | P2 | コード上のリスク | 合計カラムの二重管理 | `t_receipts.total` を保存するが取得時は明細再計算。DB上の値と集計値の乖離検証が必要 |
| LEG-015 | P2 | 未確認 | 為替レートの投入・運用 | 読込処理はあるが、取得・登録処理を確認できない。レート欠損時はUSD換算値を非表示 |
| LEG-016 | P2 | コード上のリスク | 更新競合・上書き | 親UPDATE後に子を全削除・再作成する。同一伝票を同時編集した場合、後勝ちとなる可能性 |
| LEG-017 | P2 | 未確認 | 復旧／バックアップ手順 | SQLiteファイル・設定・端末移転・復旧の手順がリポジトリ内で未確認 |
| LEG-018 | P2 | 要仕様判断 | 金額の負数・超過割引・数量0 | 明細入力時の検証が限定的。負額は収入として使うため一律禁止は不可 |

| LEG-019 | P1 | **仕様確定・未実装** | レシート単位の税端数処理 | **税率ごとに合算**後に切捨／四捨五入。**各レシートで設定・保存**し、店舗マスタは任意の初期値。マスタ登録や店コード追加は不要。[15 詳細設計](15-shop-tax-rounding-design.md) |
| LEG-020 | 対象外 | **運用確定・追加開発不要** | オーケー3/103割引 | 各商品の端数が分からないため自動計算はしない。**既存の明細「割引額（discount_fee）」へ手入力**。数量を掛ける前に控除されるため、割引合計を入れるまとめ明細は原則数量1。新規入力欄・DB変更はしない。[決定](14-decisions-2026-10-16.md) |

| LEG-021 | P1 | **DDL確認・データ未検証** | ポイント明細のFK・採番構造 | `t_receipt_points` の**PKは `seq` 単独**。`receipt_seq` はNOT NULLだがレシートへのFKはない。孤児ポイント・採番・残存データの扱いを確認 |
| LEG-022 | P2 | **DDL確認・データ未検証** | 税率コードの参照整合性 | `t_receipt_groups.tax` は **DEFAULT 8**、`m_tax_rate.code` をソースは参照するが物理FKがない。実データの税率コード対応を検査 |
| LEG-023 | P2 | **DDL確認・機能未確認** | 支払方法の有効期間 | `m_payment_classification` に `valid_from DATE` と `valid_to DATE` がある。既存UI・DBアクセスでの利用は未確認 |

| LEG-024 | P1 | **DDL確認・コード上のリスク** | NULL許容の文字列と`GetString`の不整合 | `t_receipts.shop_location/tag`, `t_receipt_groups.tag`, `t_receipt_details.tag` はNULL可。一方 `DataAccess.GetReceipts` 系では `reader.GetString` をNULL判定なしで呼ぶ箇所がある。実データのNULL件数と例外を検証 |
| LEG-025 | P2 | **コード確認** | 残高推算ロジックの重複 | `Calculation.AutoBalance` と `DataAccess.GetAutoBalance` にほぼ同じ処理がある。ルール変更時に計算の食い違いが生じるリスク |
| LEG-026 | P2 | **コード上の性能リスク** | レシート階層のN+1 SELECT | `GetReceipts` 後にグループ→明細→対象者を個別問い合わせ。年次集計で件数増加時にSQL発行回数・応答時間を計測する |

## 2026-10-16に合意した前提

- **既存データの外部キー整合性は保たれている前提**で作業を進める（検査済みとは表現しない）。削除順がFKに適合するかは別のコード課題。
- 消費税は**税率ごとに合算**。店舗別に切捨／四捨五入を指定。
- 人別の費用配分は**参考値**。端数四捨五入、合計額への強制一致なし。
- オーケーは**既存の明細「割引額」に手入力**する現行運用を継続。専用欄や自動計算は追加しない。[詳細](14-decisions-2026-10-16.md)。

## 推奨する最小機能完成の定義（提案）

1. レシートの新規・変更・削除・流用登録、複数明細と対象者、収入・支出の登録がテストを通る。
2. 振替・残高・支払メモと既存の分類／収支／残高集計が業務期待値に一致。
3. 主要マスタの参照・編集ができる、または**明示的に編集対象から除外**してCSV／DBメンテ手順がある。
4. SQLiteバックアップ・復元手順、初回セットアップ、エラー時のデータ保全手順がある。
5. 計算・丸め・按分仕様のゴールデンケースを確定し、自動テストで固定する。

## 次に必要な情報

- 実DDLの提供は完了。残りは **アプリ接続の `PRAGMA foreign_keys`、コピーDBの `PRAGMA foreign_key_check`、税率／分類のコード一覧**。
- 実運用で困っている機能、最低限追加したい機能、ポイントをMVPに含めるかどうか。
- 残高・収支の期待値サンプルと、月末・返金・按分の具体例。

## 根拠

[DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、
[Receipt.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)、
[RegisterForm.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)、
[MasterMaintenanceForm.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/MasterMaintenanceForm.vb)、
[ViewForm_BalanceStatistics.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_BalanceStatistics.vb)
