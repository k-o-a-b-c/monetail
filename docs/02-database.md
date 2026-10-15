# 02 現行SQLiteデータベース仕様（コードから復元）

更新日: 2026-10-15 / 対象: レガシー版 / **実DBのDDL未照合**

> **確定範囲**: `DataAccess.vb`、`DataAccessList.vb` のSQLとデータクラスから読み取れる**参照・更新カラムと論理リレーション**。SQLite本体（`sqlite_master`、`PRAGMA table_info`／`foreign_key_list`／`index_list`）がリポジトリにないため、物理型・NOT NULL・PK・FK・インデックス・初期データは**推定しない**。以下の「論理キー」はコード上の識別方法であり、実際のDB制約とは限らない。

## 論理テーブル一覧（15テーブル）

| テーブル | 用途 | ソース中で確認した列 |
| --- | --- | --- |
| `m_user` | 人 | `code, name, account_name, seq` |
| `m_group_classification` | 費目分類 | `code, name, seq, default_tax_target, reduced_tax_target` |
| `m_payment_classification` | 支払方法・口座 | `code, name, seq, default_point_code, user_code` |
| `m_point_classification` | ポイント種別 | `code, name, seq, default_rate` |
| `m_shop` | 店舗 | `code, name, seq, default_point_code, default_group_classification_code, user_code` |
| `m_tax_rate` | 税率 | `code, rate, start_date` |
| `t_receipts` | レシート親 | `seq, date, shop_name, shop_location, tax_type, payment_classification_code, tag, update_user, update_datetime, total` |
| `t_receipt_groups` | 費目グループ | `receipt_seq, seq, group_classification_code, tax, tag` |
| `t_receipt_details` | 品目明細 | `receipt_seq, group_seq, seq, name, fee, quantity, discount_fee, discount_rate, tax_target, tag` |
| `t_receipt_detail_users` | 明細対象者 | `receipt_seq, group_seq, detail_seq, user_code` |
| `t_transfers` | 口座間振替 | `seq, date, name, fee, from_payment_classification_code, to_payment_classification_code, tag, update_user, update_datetime` |
| `t_balances` | 残高記録ヘッダー | `seq, date, tag, update_user, update_datetime` |
| `t_balance_groups` | 口座別残高 | `balance_seq, payment_classification_code, balance, valuation_amount, tag` |
| `t_payment_memo` | 支払方法チェック日・メモ | `payment_classification_code, check_date, tag` |
| `t_exchange_rates` | 通貨ペア別の為替 | `rate_date, base_currency, quote_currency, middle_rate` |

`m_*` はマスタ、`t_*` はトランザクション／履歴データを表す既存命名。

## 主要リレーション（論理）

`m_user.code` ← `m_payment_classification.user_code` ／ `m_shop.user_code` ／ `t_receipt_detail_users.user_code`

`t_receipts.seq` → `t_receipt_groups.receipt_seq` → `t_receipt_details.(receipt_seq, group_seq)` → `t_receipt_detail_users.(receipt_seq, group_seq, detail_seq)`

`m_group_classification.code` ← `t_receipt_groups.group_classification_code`

`m_tax_rate.code` ← `t_receipt_groups.tax`

`m_payment_classification.code` ← `t_receipts.payment_classification_code`、`t_transfers` の移動元／先、`t_balance_groups.payment_classification_code`、`t_payment_memo.payment_classification_code`

`t_balances.seq` → `t_balance_groups.balance_seq`

### アプリ上の識別子

| レコード | コード上の論理キー |
| --- | --- |
| レシート／振替／残高ヘッダー | `seq` |
| レシートグループ | `(receipt_seq, seq)` |
| レシート明細 | `(receipt_seq, group_seq, seq)` |
| 明細対象者 | `(receipt_seq, group_seq, detail_seq, user_code)` |
| 口座別残高 | `(balance_seq, payment_classification_code)` |
| 支払メモ | `payment_classification_code`（`ON CONFLICT` に使用） |
| マスタ | 多くのマスタで `code` |
| 為替 | 通貨ペア・日付で検索するが物理キーは未確認 |

上記は**物理的なPRIMARY KEY宣言を確認したものではない**。

## 登録・編集・削除の動作

- レシート・振替・残高の親IDは `SELECT max(seq)+1`。複数プロセス同時入力では衝突可能性があり、拡張前に改修が必要。
- 新規レシート: `t_receipts` を作成し、グループ・明細・対象者を順番に追加する。これらは同じ `SQLiteTransaction` 内。
- レシート更新: 親更新後、子データをすべてDELETEして再INSERT。変更のない明細の `seq` も再設定される場合がある。
- レシート削除: 親と子の個別DELETEをトランザクション内で実行。外部キーの設定・削除順に関する整合性は**実DBで要検証**。
- 残高更新: 親更新後、子の残高明細を全削除・再登録する。
- 支払メモ: `ON CONFLICT(payment_classification_code) DO UPDATE` で上書き。登録ループには明示的なトランザクションがない。
- 為替: `t_exchange_rates` を参照するコードのみ確認。投入・更新処理は未確認。

## 列の値と注意点

- `t_receipts.tax_type` はコード上、`0=税込`、`1=税抜`。
- 明細の `fee` は単価、`quantity` は数量、`discount_fee` は単価から差し引く金額、`discount_rate` は率（%）、`tax_target` は課税対象フラグ。
- レシート合計は `t_receipts.total` に保存するが、読み出し時の `Receipt.Total` は明細から再計算する（SELECTで保存合計列は取得していない）。
- 金額はVBの `Integer` を利用する箇所が多い。`discount_rate` はオブジェクト上 `Double` だがDB読込が `GetInt32(7)` のため、小数率の復元が要検証。
- `update_user` は `Environment.UserName`、`update_datetime` は `DateTime.Now` を保存。認証ユーザーID・UTCとは異なる。
- `m_tax_rate` は税率コード（`code`）を参照する。税率初期選択に日付境界のハードコードがある。
- `m_shop` はユーザー所有／共通のマスタを参照し、過去のレシートから店舗候補を抽出する。
- 設計上、支払方法と「人」の関係を厳密に検証する権限制御はコードから確認できない。

## 実DBで追加採取すべき情報

バックアップした既存DBの**ローカルコピー**で以下を実行し、結果を別ファイルとして保存（個人情報や取引明細をGitHubへ登録しない）。

```sql
SELECT type, name, tbl_name, sql
FROM sqlite_master
WHERE type IN ('table', 'index', 'trigger', 'view')
ORDER BY type, name;
PRAGMA table_info('t_receipts');
PRAGMA foreign_key_list('t_receipts');
PRAGMA index_list('t_receipts');
PRAGMA integrity_check;
```

全テーブルについて `table_info / foreign_key_list / index_list` を採取する。データ件数、最小／最大日付、NULLや孤児レコードの有無を匿名集計し、論理仕様との差異を記録する。

## 根拠

[DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、[DataAccessList.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccessList.vb)、[データクラス](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data)、[マスタ画面設定XML](../HouseholdAccountsBeta/HouseholdAccountsBeta/MasterConfig)
