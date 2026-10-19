# 02 現行SQLiteデータベース仕様（実DDL照合済み）

更新日: 2026-10-19。対象: `monetail-legacy`、VB.NET / .NET Framework 4.8 / SQLite。

> **一次資料**: 利用者が実SQLiteから抽出した `monetail_schema.sql`。[元のDDL](schema/monetail_schema.sql) を保存した。ここで「確定」とするのは提出されたDDLに記載された宣言である。**実レコード、実行時PRAGMA、データ整合性、SQLiteの物理ファイル内の追加構造は未検証**。宣言型と実値の格納型はSQLiteでは必ずしも同じではない。

> **提出時DDLと実装後の区別**: 第1～3節は提出時のDDLスナップショット（16表・90列・17外部キー）を記録しており、原本は変更していない。現在のアプリには税端数2列と更新版3列が必要で、両移行後のテストDBは16表・95列・17外部キーとなる。追加列は第6節以降と[保存契約](18-stale-edit-protection.md)に記載する。利用者の実DBへの適用状況は未確認。

## 1. 全体

- テーブル: **16**（マスタ6、取引・履歴10）。ソース解析のみで以前把握した15表から、`t_receipt_points` が追加確認された。
- DDLに宣言された外部キー制約: **17件**（複合FKは2件）。
- 明示的な `CREATE INDEX`、`CREATE VIEW`、`CREATE TRIGGER` は提出SQLに含まれていない（主キーの内部インデックスを除く）。
- 外部キーには `ON DELETE CASCADE` 等の宣言がなく、デフォルトの `NO ACTION`。ただし、実際に制約が検証されるかは**接続ごとの `PRAGMA foreign_keys` に依存**する。
- 日付の宣言型は `TEXT` と `DATE` が混在する。DDLには利用データが一切含まれない。

## 2. テーブル一覧

| テーブル | 用途 | 主キー | 外部キー数 |
| --- | --- | --- | ---: |
| `t_receipts` | レシート | `seq` | 1 |
| `m_payment_classification` | 支払方法・口座マスタ | `code` | 2 |
| `m_point_classification` | ポイントマスタ | `code` | 0 |
| `t_receipt_points` | レシートポイント（コード未使用部分あり） | `seq` | 1 |
| `t_transfers` | 口座間振替 | `seq` | 2 |
| `m_group_classification` | 費目分類マスタ | `code` | 0 |
| `t_receipt_groups` | 費目グループ | `receipt_seq, seq` | 2 |
| `t_receipt_details` | 購入商品明細 | `receipt_seq, group_seq, seq` | 1 |
| `m_shop` | 店マスタ | `code` | 3 |
| `m_tax_rate` | 消費税率マスタ | `code` | 0 |
| `t_balances` | 残高スナップショット | `seq` | 0 |
| `t_balance_groups` | 支払方法別の残高明細・評価額 | `balance_seq, payment_classification_code` | 2 |
| `m_user` | 人物マスタ | `code` | 0 |
| `t_receipt_detail_users` | 購入明細の対象者 | `receipt_seq, group_seq, detail_seq, user_code` | 2 |
| `t_payment_memo` | 支払方法チェック日・メモ | `payment_classification_code` | 1 |
| `t_exchange_rates` | 通貨ペア別為替レート | `rate_date, base_currency, quote_currency` | 0 |

## 3. カラム、NULL、既定値、外部キー

「必須」は `NOT NULL` 宣言の有無であり、主キーとは別に表示する。型はDDLの綴りを大文字に正規化したもの。既定値欄の「—」はSQL側でDEFAULT指定なし。

### `t_receipts`（レシート）

**主キー**: `seq`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `seq` | `INTEGER` | はい | — | 1 |
| `date` | `TEXT` | はい | — | — |
| `shop_name` | `TEXT` | はい | — | — |
| `shop_location` | `TEXT` | いいえ | — | — |
| `tax_type` | `INTEGER` | はい | — | — |
| `payment_classification_code` | `INTEGER` | はい | — | — |
| `tag` | `TEXT` | いいえ | — | — |
| `update_user` | `TEXT` | はい | — | — |
| `update_datetime` | `TEXT` | はい | — | — |
| `total` | `INTEGER` | いいえ | — | — |

**宣言された外部キー:** 

- `(payment_classification_code)` → `m_payment_classification(code)`

### `m_payment_classification`（支払方法・口座マスタ）

**主キー**: `code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `code` | `INTEGER` | はい | — | 1 |
| `name` | `TEXT` | はい | — | — |
| `seq` | `INTEGER` | いいえ | — | — |
| `default_point_code` | `INTEGER` | いいえ | — | — |
| `user_code` | `INTEGER` | いいえ | — | — |
| `valid_from` | `DATE` | いいえ | — | — |
| `valid_to` | `DATE` | いいえ | — | — |

**宣言された外部キー:** 

- `(user_code)` → `m_user(code)`
- `(default_point_code)` → `m_point_classification(code)`

### `m_point_classification`（ポイントマスタ）

**主キー**: `code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `code` | `INTEGER` | はい | — | 1 |
| `name` | `TEXT` | はい | — | — |
| `seq` | `INTEGER` | いいえ | — | — |
| `default_rate` | `REAL` | いいえ | — | — |

**宣言された外部キー:** なし

### `t_receipt_points`（レシートポイント（コード未使用部分あり））

**主キー**: `seq`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `receipt_seq` | `INTEGER` | はい | — | — |
| `seq` | `INTEGER` | はい | — | 1 |
| `code` | `INTEGER` | はい | — | — |
| `rate` | `REAL` | はい | — | — |
| `fixed_points` | `INTEGER` | はい | — | — |
| `total_points` | `NUMBER` | いいえ | — | — |

**宣言された外部キー:** 

- `(code)` → `m_point_classification(code)`

### `t_transfers`（口座間振替）

**主キー**: `seq`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `seq` | `INTEGER` | はい | — | 1 |
| `date` | `TEXT` | はい | — | — |
| `name` | `TEXT` | いいえ | — | — |
| `fee` | `INTEGER` | はい | — | — |
| `from_payment_classification_code` | `INTEGER` | はい | — | — |
| `to_payment_classification_code` | `INTEGER` | はい | — | — |
| `tag` | `TEXT` | いいえ | — | — |
| `update_user` | `TEXT` | はい | — | — |
| `update_datetime` | `TEXT` | はい | — | — |

**宣言された外部キー:** 

- `(from_payment_classification_code)` → `m_payment_classification(code)`
- `(to_payment_classification_code)` → `m_payment_classification(code)`

### `m_group_classification`（費目分類マスタ）

**主キー**: `code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `code` | `INTEGER` | はい | — | 1 |
| `name` | `TEXT` | はい | — | — |
| `seq` | `INTEGER` | いいえ | — | — |
| `default_tax_target` | `INTEGER` | はい | — | — |
| `reduced_tax_target` | `INTEGER` | はい | `0` | — |

**宣言された外部キー:** なし

### `t_receipt_groups`（費目グループ）

**主キー**: `receipt_seq, seq`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `receipt_seq` | `INTEGER` | はい | — | 1 |
| `seq` | `INTEGER` | はい | — | 2 |
| `group_classification_code` | `INTEGER` | はい | — | — |
| `tag` | `TEXT` | いいえ | — | — |
| `tax` | `INTEGER` | はい | `8` | — |

**宣言された外部キー:** 

- `(receipt_seq)` → `t_receipts(seq)`
- `(group_classification_code)` → `m_group_classification(code)`

### `t_receipt_details`（購入商品明細）

**主キー**: `receipt_seq, group_seq, seq`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `receipt_seq` | `INTEGER` | はい | — | 1 |
| `group_seq` | `INTEGER` | はい | — | 2 |
| `seq` | `INTEGER` | はい | — | 3 |
| `name` | `TEXT` | はい | — | — |
| `fee` | `INTEGER` | はい | — | — |
| `quantity` | `INTEGER` | はい | — | — |
| `discount_fee` | `INTEGER` | はい | — | — |
| `discount_rate` | `INTEGER` | はい | — | — |
| `tax_target` | `INTEGER` | はい | — | — |
| `tag` | `TEXT` | いいえ | — | — |

**宣言された外部キー:** 

- `(receipt_seq, group_seq)` → `t_receipt_groups(receipt_seq, seq)`

### `m_shop`（店マスタ）

**主キー**: `code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `code` | `INTEGER` | はい | — | 1 |
| `name` | `TEXT` | はい | — | — |
| `seq` | `INTEGER` | いいえ | — | — |
| `default_point_code` | `INTEGER` | いいえ | — | — |
| `default_group_classification_code` | `INTEGER` | いいえ | — | — |
| `user_code` | `INTEGER` | いいえ | — | — |

**宣言された外部キー:** 

- `(user_code)` → `m_user(code)`
- `(default_point_code)` → `m_point_classification(code)`
- `(default_group_classification_code)` → `m_group_classification(code)`

### `m_tax_rate`（消費税率マスタ）

**主キー**: `code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `code` | `INTEGER` | はい | — | 1 |
| `rate` | `INTEGER` | はい | — | — |
| `start_date` | `DATE` | いいえ | — | — |

**宣言された外部キー:** なし

### `t_balances`（残高スナップショット）

**主キー**: `seq`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `seq` | `INTEGER` | はい | — | 1 |
| `date` | `TEXT` | はい | — | — |
| `tag` | `TEXT` | いいえ | — | — |
| `update_user` | `TEXT` | はい | — | — |
| `update_datetime` | `TEXT` | はい | — | — |

**宣言された外部キー:** なし

### `t_balance_groups`（支払方法別の残高明細・評価額）

**主キー**: `balance_seq, payment_classification_code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `balance_seq` | `INTEGER` | はい | — | 1 |
| `payment_classification_code` | `INTEGER` | はい | — | 2 |
| `balance` | `INTEGER` | はい | — | — |
| `tag` | `TEXT` | いいえ | — | — |
| `valuation_amount` | `INTEGER` | いいえ | — | — |

**宣言された外部キー:** 

- `(balance_seq)` → `t_balances(seq)`
- `(payment_classification_code)` → `m_payment_classification(code)`

### `m_user`（人物マスタ）

**主キー**: `code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `code` | `INTEGER` | はい | — | 1 |
| `name` | `TEXT` | はい | — | — |
| `account_name` | `TEXT` | はい | — | — |
| `seq` | `INTEGER` | いいえ | — | — |

**宣言された外部キー:** なし

### `t_receipt_detail_users`（購入明細の対象者）

**主キー**: `receipt_seq, group_seq, detail_seq, user_code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `receipt_seq` | `INTEGER` | はい | — | 1 |
| `group_seq` | `INTEGER` | はい | — | 2 |
| `detail_seq` | `INTEGER` | はい | — | 3 |
| `user_code` | `INTEGER` | はい | — | 4 |

**宣言された外部キー:** 

- `(receipt_seq, group_seq, detail_seq)` → `t_receipt_details(receipt_seq, group_seq, seq)`
- `(user_code)` → `m_user(code)`

### `t_payment_memo`（支払方法チェック日・メモ）

**主キー**: `payment_classification_code`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `payment_classification_code` | `INTEGER` | はい | — | 1 |
| `check_date` | `DATE` | いいえ | — | — |
| `tag` | `TEXT` | いいえ | — | — |

**宣言された外部キー:** 

- `(payment_classification_code)` → `m_payment_classification(code)`

### `t_exchange_rates`（通貨ペア別為替レート）

**主キー**: `rate_date, base_currency, quote_currency`

| カラム | 型 | 必須 | 既定値 | PK順 |
| --- | --- | :---: | --- | :---: |
| `rate_date` | `TEXT` | はい | — | 1 |
| `base_currency` | `TEXT` | はい | — | 2 |
| `quote_currency` | `TEXT` | はい | — | 3 |
| `middle_rate` | `REAL` | はい | — | — |

**宣言された外部キー:** なし


## 4. レガシーコード解析との差分（重要）

| 対象 | DDLで確定した事実 | 影響・検証事項 |
| --- | --- | --- |
| 総数 | 15表 → **16表** | `t_receipt_points` は提出DDLに存在する。`ReceiptPoint` モデルもあるが、DataAccessでの保存・読込は未実装 |
| `t_receipt_points` | `receipt_seq` はNOT NULL、**主キーは `seq` 単独**、ポイント種別へのFKのみ | **`t_receipts(seq)` へのFKなし**。孤児行・連番使用方法を調べる |
| `m_payment_classification` | `valid_from DATE`、`valid_to DATE` はNULL許容 | 支払い方法の有効期間機能が使われているか確認する |
| `t_receipt_details.discount_rate` | **INTEGER NOT NULL** | VBの `ReceiptDetail.DiscountRate` は `Double`。小数はSQLiteのREALとして保存される。共通読込を`GetInt32(7)`から`GetDouble(7)`へ修正し、宣言型・保存処理は維持（LEG-005） |
| `t_receipt_groups.tax` | **INTEGER NOT NULL DEFAULT 8**、`m_tax_rate` へのFKなし | VB側は税率マスタの**コード**として解釈。実データのコード対応を確認 |
| `t_receipts` | 店名・店舗は文字列。`shop_code` なし。`total INTEGER` はNULL許容 | **丸め方式をレシート自体へ保存するため店舗マスタへのFKは不要**。保存合計と明細再計算の一致も検証 |
| 削除の整合性 | レシート→グループ→明細→対象者、残高→支払方法別残高にFKがあるがCASCADEなし | レシート更新・削除（LEG-004）と残高削除（LEG-027）を子から削除する順序へ修正。FK ON/OFFで成功・失敗時の巻き戻しを自動テスト |
| インデックス | DDLに追加インデックスの定義なし | 複合主キーインデックスはあるが、月別・支払方法別検索の効率を実データで検証 |

## 5. 現行コードとの整合性・リスク

- レシート・振替・残高の`seq`は、`BEGIN IMMEDIATE`の後に`MAX(seq)+1`で採番する。ローカル2接続での採番の直列化とロック失敗後の全行保持・再試行は[PR #9の検証記録](17-concurrent-persistence-validation.md)を参照する。現行は親3表へ`revision_token`を追加し、更新・削除時に`seq`と読込時の版を照合する。古い編集・削除済み・ID再利用時は保存を拒否する。採番と接続設定は維持する。[現行の保存契約](18-stale-edit-protection.md)参照。
- 親子データはレシート登録時にトランザクションを用いる。**レシート更新では子データを削除・再登録**。
- `DataAccess.DeleteReceiptGroups` は **明細対象者 → 明細 → グループ** の順で削除し、`DeleteReceipt` はその後にレシート親を削除する。2026-10-18に旧来の親からの削除順を修正した。既存のトランザクション内で更新・削除し、途中失敗時はDBの変更全体を戻す。接続時のFK設定とDB定義は変更しない。
- `DataAccess.DeleteBalance` は **`t_balance_groups` → `t_balances`** の順に削除する（LEG-027）。FK ON/OFFで削除成功（子0／2件）と、既存トランザクション内の親削除失敗からの復元・同じDataAccessでの再削除（子2件）を検証済み。製品の接続設定・DDL・画面操作は維持する。
- レシートのNULL許容文字列4列（`t_receipts.shop_location`、`t_receipts.tag`、`t_receipt_groups.tag`、`t_receipt_details.tag`）は、`IsDBNull`で空文字へ正規化して読み込む。一覧・残高用の両読込経路と子データ再読込を対応済み。読込・画面の取消だけではDBのNULLや金額・対象者割当を変更せず、明示保存時は既存処理に従い空文字を保存する（LEG-024）。
- 残高・支払方法別残高・振替の`tag`3列も、NULLだけを空文字として読み込む（LEG-028）。`GetBalances`・`GetLastBalance`・`GetLastValuationAmount`のヘッダー3箇所と共通の`LoadBalanceGroupData`、振替の`GetTransfers`・`GetPaymentTransfers`の計6箇所に限定する。DBのNULLや日時・金額・NULLを含む評価額・支払方法は読込で変更せず、既存の非NULL文字列・保存SQL・DDL・計算式・画面操作を維持する。全表・全列のNULL対応ではない。
- 小数割引率の旧`GetInt32`読込は、製品と同じSQLiteプロバイダーで`InvalidCastException`になることを再現した。共通の`LoadReceiptDetailData`で`GetDouble`を使い、整数率・小数率の両方をモデルへ読み込む。DDLの`INTEGER`は変更せず、既存DBの移行や率・保存合計の一括更新は行わない。[検証範囲](06-test-cases.md)参照。
- `RegisterPaymentMemo`は、入力リスト全体の既存UPSERTを1つのトランザクションで処理し、全件成功時にCommit、失敗時にRollbackして例外を再送出する。空リストでは開始前に戻る。FK ON/OFFで成功・末尾INSERT／UPDATE失敗時の全件復元・同じDataAccessでの再保存・空リストを自動検証済み。`t_payment_memo`のDDL、保存順、日付・タグのNULL保存仕様は維持する。
- `t_receipts.total` を保存するが `GetReceipts` のSELECTから合計列を読まないため、DBに保存された`total`と、`Receipt.Total`で再計算する表示・集計額が異なり得る。
- `t_receipt_points.receipt_seq` にFKがなく、`t_receipt_groups.tax` に税率マスタのFKがない点は、移行前の整合性検証対象。
- SQLite接続に `PRAGMA foreign_keys` を明示設定するVBコードは `DataAccess.vb` 冒頭で確認できない。接続文字列・プロバイダー既定値による動作は現状不明。

## 6. 店舗別の消費税端数処理（実装済み）とオーケーの現行運用

アプリ実装と自動テストは完了している。提出時DDL原本は維持し、次の2列を移行SQLで追加する。利用者の実DBでの移行・動作確認は未確認。

| 追加列 | 宣言・既定値 | 現在の用途 |
| --- | --- | --- |
| `t_receipts.tax_rounding_mode` | `TEXT NOT NULL DEFAULT 'floor' CHECK (tax_rounding_mode IN ('floor', 'half_up'))` | 保存済みレシートの税額計算方式の正本 |
| `m_shop.tax_rounding_mode` | 同上 | 既存の移行設計との互換性のため保持。モデルへ読み込むが、入力の初期選択には使用しない |

- 税抜レシートは**税率ごとに課税額を合算してから**、レシートの方式で税額を切り捨て／四捨五入する。税込入力の合計は方式に依存しない。旧`floor`は従来の計算を維持し、`half_up`の負の税額は`AwayFromZero`で丸める。
- 新規入力の初期選択は、**同じ店舗名・現在の利用者の直近レシート**から取得する。支払方法の利用者を`Environment.UserName`で絞り、`date DESC, seq DESC`の先頭を採用する。履歴がなければ`floor`。店舗マスタ未登録の自由入力でも同じ動作とし、手動選択後や既存レシートの編集では自動上書きしない。マスタメンテナンス画面は追加していない。
- 内税／外税・端数方式は画面側で編集中の値を保持し、合計のプレビューへ渡す。登録確認後だけReceiptへ転記し、キャンセルでは元の両設定を変更しない。流用は従来どおり確認後に即時保存して閉じ、追加の登録操作なしで画面の選択値を引き継ぐ。
- 旧レシートは`floor`を保持し、移行SQLで保存`total`・既存明細・対象者割当を更新しない。店舗コードの自動付与や`t_receipts.shop_code`、店舗マスタへのFKも追加しない。`foreign_keys`・`user_version`も変更しない。[詳細設計](15-shop-tax-rounding-design.md)、[移行SQL](migrations/20261017_01_shop_tax_rounding.sql)、[自動テストと画面確認手順](../tests/README.md)参照。
- **オーケー3/103割引の新しいDB列や子テーブルは追加しない**。今までどおり`t_receipt_details.discount_fee`相当の既存「割引額」を手入力する。これは単価あたりの割引なので、割引合計をまとめて入れる明細は原則`quantity=1`とする。

関連: [08 要件と具体例](08-requested-enhancements.md) / [05 課題](05-issues.md) / [06 テスト](06-test-cases.md)。

## 7. 更新版の照合（LEG-016）

[更新版移行](migrations/20261019_01_revision_tokens.sql)は税端数移行後に適用し、次の3列を追加する。元のDDL・業務値・FKは維持する。

| 表・列 | 型・既定値 | 用途 |
| --- | --- | --- |
| `t_receipts.revision_token` | `TEXT NOT NULL DEFAULT '0'` | レシート親と子の更新・削除を保護 |
| `t_transfers.revision_token` | 同上 | 振替の更新・削除を保護 |
| `t_balances.revision_token` | 同上 | 残高親と子の更新・削除を保護 |

既存行は初期版`'0'`、新アプリの登録・更新はGUIDを保存する。照合と親子操作は同じトランザクション内で行い、版をモデルへ反映するのはCommit後だけとする。旧アプリとの同時更新は行わない。[競合時の動作と検証](18-stale-edit-protection.md)を参照。

## 8. 今回のスキーマだけでは確定できないこと

- 実データ件数、NULL値、実際の保存型・日時書式、マスタコードの具体的な値。
- 孤児行・重複の有無、登録済みポイントの有無、履歴データと保存`total`の差。
- アプリ実行接続の`PRAGMA foreign_keys`、実ファイルの`PRAGMA integrity_check`と`foreign_key_check`。
- インデックス性能、税率マスタの具体的なコードとレシート税率の整合性。

テスト用にコピーしたSQLiteへ接続して、次を実行して調査する。**実データファイルをGitHubへアップロードしない。**

```sql
PRAGMA foreign_keys;
PRAGMA foreign_key_check;
PRAGMA integrity_check;
PRAGMA index_list('t_receipts');
SELECT name, type, sql FROM sqlite_master
WHERE type IN ('table','index','view','trigger')
ORDER BY type, name;
```

※ `PRAGMA foreign_keys` は接続単位の状態を返すため、外部ツールでONを確認してもレガシーアプリの接続がONとは限らない。

## 根拠

- [SQLiteから抽出されたDDL](schema/monetail_schema.sql)
- [DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)
- [Receipt.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)、[ReceiptPoint.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/ReceiptPoint.vb)
