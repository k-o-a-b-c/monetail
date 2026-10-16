# 11 SQLite健全性確認・バックアップ・データ移行前チェック

更新: 2026-10-16 / 本書は**手順と検査SQL**。実DBに対しては未実行。

## 前提・注意

- 対象DBをアプリで操作しながらファイル単純コピーすると、WALやジャーナルの状態によって完全なスナップショットにならない可能性がある。**アプリを停止してバックアップ**するか、SQLiteのオンラインバックアップ機構を使う。
- 本番ファイルでは試さない。**取得したバックアップのコピー**を検査する。SQL実行前にファイルパスを目視確認する。
- このSQLファイルは**実データを変更しないSELECT/PRAGMA中心**。集計件数も個人情報に該当し得るため、外部共有前に内容を確認する。
- `PRAGMA foreign_keys` は**接続単位**。CLIで取得した値をそのままVBアプリの値とみなさない。
- 既存レシートの`t_receipts.total`と表示額の差は、SQLだけで正確には計算できない（割引・税・端数の業務ロジックが必要）。

## 1. スキーマと整合性の検査

Windowsの例（`sqlite3.exe` がPATHにあること。引用符内を実際のパスへ変更）:

```bat
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA integrity_check;"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA foreign_key_check;"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA foreign_keys;"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA index_list('t_receipts');"
```

- `integrity_check` が`ok`でない場合はDBファイルの健全性を確認する。
- `foreign_key_check` は違反があるとテーブル名・rowid・参照先等を返す。**結果が空**なら（検証対象のDDLに関して）整合性違反は報告されなかった意味。
- `foreign_keys` が0でも、`foreign_key_check`は宣言済みのFKの違反を調査できる。ただしアプリ接続が0か1かは別途確認する。
- `index_list` の`sqlite_autoindex_*`は主キー由来の内部索引であり、手書き`CREATE INDEX`ではない。

## 2. 個人データを出力しない確認SQL

以下は**主に件数を返す**。バックアップのコピーで実行する。

```sql
-- 行数（実データの個別内容は表示しない）
SELECT 't_receipts' AS table_name, COUNT(*) AS rows FROM t_receipts
UNION ALL SELECT 't_receipt_groups', COUNT(*) FROM t_receipt_groups
UNION ALL SELECT 't_receipt_details', COUNT(*) FROM t_receipt_details
UNION ALL SELECT 't_receipt_detail_users', COUNT(*) FROM t_receipt_detail_users
UNION ALL SELECT 't_receipt_points', COUNT(*) FROM t_receipt_points
UNION ALL SELECT 't_transfers', COUNT(*) FROM t_transfers
UNION ALL SELECT 't_balances', COUNT(*) FROM t_balances
UNION ALL SELECT 't_balance_groups', COUNT(*) FROM t_balance_groups
UNION ALL SELECT 't_payment_memo', COUNT(*) FROM t_payment_memo;

-- FKが設定されていないポイント → レシート参照の孤児行
SELECT COUNT(*) AS orphan_receipt_points
FROM t_receipt_points p LEFT JOIN t_receipts r ON p.receipt_seq = r.seq
WHERE r.seq IS NULL;

-- 物理FKのないグループ税率コードの参照切れ
SELECT COUNT(*) AS unknown_tax_codes
FROM t_receipt_groups g
LEFT JOIN m_tax_rate m ON g.tax = m.code
WHERE m.code IS NULL;

-- SQLiteの型親和性で、宣言INTEGERの割引率にREAL等が入っていないか
SELECT typeof(discount_rate) AS stored_type, COUNT(*) AS rows
FROM t_receipt_details
GROUP BY typeof(discount_rate);

-- 保存合計がNULLのレシート件数
SELECT COUNT(*) AS receipt_total_nulls
FROM t_receipts WHERE total IS NULL;

-- 店舗の同名登録件数（店舗名そのものは出力しない）
SELECT COUNT(*) AS duplicate_shop_names
FROM (
  SELECT name FROM m_shop GROUP BY name HAVING COUNT(*) > 1
);

-- レシートの店舗名が店舗マスタと一致しない件数
-- ※過去店・自由入力が仕様なら不一致自体は異常と限らない
SELECT COUNT(*) AS unmatched_receipt_shops
FROM t_receipts r
WHERE NOT EXISTS (SELECT 1 FROM m_shop s WHERE s.name = r.shop_name);

-- 同名の店舗マスタが複数あるレシート件数
SELECT COUNT(*) AS ambiguously_named_receipts
FROM t_receipts r
WHERE (SELECT COUNT(*) FROM m_shop s WHERE s.name = r.shop_name) > 1;

-- DB上はNULLを許すが読込コードのGetStringで問題となり得る値
SELECT COUNT(*) AS null_shop_locations
FROM t_receipts WHERE shop_location IS NULL;
```

**判断上の注意**:

- ポイントの孤児行が1件以上なら、データ消去・一括クリーンアップの前に保存仕様とバックアップを確認。
- 税率コード不一致があれば、現行`TaxRate(code)`呼出しでNullになる可能性があるため、実データを保全したまま原因分析。
- `discount_rate`は`INTEGER`宣言でも、SQLiteの型親和性により内容によっては他型が入る可能性がある。
- 店舗の同名・名称不一致は**マスタ運用の仕様確認が先**で、機械的な削除・修正をしてはいけない。

## 3. DBバックアップ・復元のリハーサル（本番を直接触らない）

1. アプリを終了し、作業用フォルダ内でバックアップを作る。可能ならSQLiteの`.backup`機構を利用。コピー元DBの取扱い権限・暗号化・保管期限を確認する。
2. 作成したファイルを別名で**テスト用に複製**する。接続文字列をこの複製に変更する。
3. `PRAGMA integrity_check`、`foreign_key_check`、主要テーブル行数を比較する。
4. テスト専用DBでレシート追加・編集・削除を実施し、アプリとDBの金額の変化を確認する。
5. 元の本番DBには**検査・移行テスト結果を反映しない**。必要な修正は別チケットで計画する。

## 4. 将来のスキーマ変更前チェック

- 変更前DDLと変更後DDLをどちらも版管理し、**ロールバックの手順**を用意。
- 旧レシートの計算結果をテスト用データで固定（[12](12-calculation-contract.md)）。
- 新カラムのDEFAULT・NULL・移行方法を定義。古いレシートは従来計算を維持する。
- FKの有効化だけを先に本番へ投入しない。削除順・ポイント孤児の扱いを解決してから段階的に試験する。
- `m_shop`と`t_receipts`の店舗名同一性を確認して、安易な名前一致JOINによる誤紐付けを避ける。

**元資料**: [実DDL](schema/monetail_schema.sql)、[DB仕様書](02-database.md)、[ソースと課題](05-issues.md)
