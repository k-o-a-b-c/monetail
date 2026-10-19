# 11 SQLite健全性確認・バックアップ・データ移行前チェック

更新: 2026-10-19 / 本書は**手順と検査SQL**。実DBに対しては未実行。

## 前提・注意

- 対象DBをアプリで操作しながらファイル単純コピーすると、WALやジャーナルの状態によって完全なスナップショットにならない可能性がある。**アプリを停止してバックアップ**するか、SQLiteのオンラインバックアップ機構を使う。
- 本番ファイルでは試さない。**取得したバックアップのコピー**を検査する。SQL実行前にファイルパスを目視確認する。
- 本書のSQLは**実データを変更しないSELECT/PRAGMA中心**。集計件数も個人情報に該当し得るため、外部共有前に内容を確認する。
- `PRAGMA foreign_keys` は**接続単位**。CLIで取得した値をそのままVBアプリの値とみなさない。
- 既存レシートの`t_receipts.total`と表示額の差は、SQLだけで正確には計算できない（割引・税・端数の業務ロジックが必要）。
- 提出時の[DDL原本](schema/monetail_schema.sql)は16表・90列・17外部キーのスナップショット。税端数の[移行SQL](migrations/20261017_01_shop_tax_rounding.sql)適用後は92列、[更新版移行](migrations/20261019_01_revision_tokens.sql)後は95列となる。更新版は親3表のrevision_tokenが未追加のコピーへ一度だけ適用する。利用者の実DBへの適用状況は未確認のため、まず列の有無を調べ、追加済みDBへ再適用しない。

## 実装・自動検証済みの範囲

- **税端数**: 税率別合算後の`floor`／`half_up`をレシート単位で保存する。新規入力は同じ店舗・現在の利用者の直近レシートを初期値とし、店舗マスタ列は互換性のため残すが初期選択に使わない。移行テストでは既存行・金額・対象者割当、既定値・制約、PRAGMAの保持と再実行拒否を確認済み。
- **外部キー**: 更新・削除は明細対象者 → 明細 → グループ → レシートの順で削除する（更新ではレシート親を削除せず子を再登録）。既存トランザクション内で処理し、テスト用接続のFK ON/OFF双方で成功と途中失敗時のDB復元を確認済み。製品接続のFK設定やDB定義を一律変更したものではない。`t_receipt_points.receipt_seq`へのFK追加・ポイント保存読込は未対応。
- **残高削除**: `t_balance_groups` → `t_balances`の順へ変更し、既存トランザクション・製品のFK設定・DDL・画面操作は維持する。FK ON/OFFで削除成功（子0／2件）、親DELETE失敗時の復元と同じDataAccessでの再削除（子2件）、他の残高と無関係な全テーブルの保持を一時DBで確認済み（LEG-027）。
- **レシートのNULL文字列**: 所在地・レシートタグ・グループタグ・明細タグはNULLを空文字として読み込む。一覧・残高の両経路、子の再読込、編集取消・再表示で、DBのNULL・金額・対象者割当を変えないことを確認済み。明示保存では従来の保存処理に従い空文字を保存する（LEG-024）。
- **残高・振替のNULLタグ**: `t_balances.tag`・`t_balance_groups.tag`・`t_transfers.tag`の3列・6読込箇所もNULLだけを空文字として読み込む（LEG-028）。非NULLの空文字・通常文字・日本語／引用符・空白、日時・金額・NULLを含む評価額・支払方法と保存済みNULLを保持し、全NULL時の両AutoBalanceも比較する。保存SQL・DDL・計算・画面操作は維持し、DBの一括更新は行わない。全表・全列のNULL対応や実データ全体の健全性確認ではない。
- **小数割引率**: REAL値の旧`GetInt32`読込例外を実SQLiteで再現し、共通処理を`GetDouble`へ修正。新規・更新、整数率・小数率、両端数方式と一覧・残高・子再読込で率と合計の保持、読込によるDB不変を自動検証済み。INTEGER宣言や保存値を一括変更する移行は不要。
- **支払メモ一括保存**: 既存UPSERT全体を1つのトランザクションで処理し、全件成功時だけ確定、失敗時は先行する追加・更新も巻き戻して例外を再送出する。FK ON/OFFの一時DBで、成功・末尾INSERT／UPDATE失敗時の全件復元と同じDataAccessでの再保存・空リストを確認済み。NULL日付／タグと空文字、対象外行、読込時の保存値も保持する。保存SQL・DDL・画面の確認操作は変更していない。

- **採番・更新競合**: ローカル2接続・FK ON/OFFで採番の直列化とロック失敗時の全行保持・再試行を確認した。現在は更新版照合で古い編集の更新・削除を中止する。移行は3列追加だけで既存の業務値を変更しない。[保存契約・移行手順](18-stale-edit-protection.md)参照。

これらは合成したテストDBでの自動検証であり、利用者の実DBの移行状況・整合性を確認したものではない。[自動テストと実行方法](../tests/README.md)参照。

## 1. スキーマと整合性の検査

Windowsの例（`sqlite3.exe` がPATHにあること。引用符内を実際のパスへ変更）:

```bat
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA integrity_check;"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA foreign_key_check;"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA foreign_keys;"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA index_list('t_receipts');"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA table_info('t_receipts');"
sqlite3 -readonly "C:\work\monetail_test.sqlite" "PRAGMA table_info('m_shop');"
```

- `integrity_check` が`ok`でない場合はDBファイルの健全性を確認する。
- `foreign_key_check` は違反があるとテーブル名・rowid・参照先等を返す。**結果が空**なら（検証対象のDDLに関して）整合性違反は報告されなかった意味。
- `foreign_keys` が0でも、`foreign_key_check`は宣言済みのFKの違反を調査できる。ただしアプリ接続が0か1かは別途確認する。
- `index_list` の`sqlite_autoindex_*`は主キー由来の内部索引であり、手書き`CREATE INDEX`ではない。
- `table_info`で両表に`tax_rounding_mode`があるか確認する。片方だけなら移行状態を調査する。列がないDBでも本節のPRAGMAは実行できる。端数方式そのものの集計は、両列の存在を確認してから[移行SQL末尾の読取確認](migrations/20261017_01_shop_tax_rounding.sql)を必要に応じて実行する。

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

-- 割引率の格納型を確認（INTEGER宣言でも、小数率のREAL格納は正常）
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

-- レシート4列・残高と振替3列のNULL件数（NULL自体は異常ではない）
SELECT 't_receipts.shop_location' AS column_name, COUNT(*) AS null_rows
FROM t_receipts WHERE shop_location IS NULL
UNION ALL SELECT 't_receipts.tag', COUNT(*) FROM t_receipts WHERE tag IS NULL
UNION ALL SELECT 't_receipt_groups.tag', COUNT(*) FROM t_receipt_groups WHERE tag IS NULL
UNION ALL SELECT 't_receipt_details.tag', COUNT(*) FROM t_receipt_details WHERE tag IS NULL
UNION ALL SELECT 't_balances.tag', COUNT(*) FROM t_balances WHERE tag IS NULL
UNION ALL SELECT 't_balance_groups.tag', COUNT(*) FROM t_balance_groups WHERE tag IS NULL
UNION ALL SELECT 't_transfers.tag', COUNT(*) FROM t_transfers WHERE tag IS NULL;
```

**判断上の注意**:

- ポイントの孤児行が1件以上なら、データ消去・一括クリーンアップの前に保存仕様とバックアップを確認。
- 税率コード不一致があれば、現行`TaxRate(code)`呼出しでNullになる可能性があるため、実データを保全したまま原因分析。
- `discount_rate`は`INTEGER`宣言でも小数率をREALで保存でき、現行アプリは`GetDouble`で読み込む。REALの件数だけで不整合と判断しない。INTEGER／REAL以外の格納型や、実際の値の妥当性は別途確認する。
- 店舗の同名・名称不一致は**マスタ運用の仕様確認が先**で、機械的な削除・修正をしてはいけない。
- 上記7列はNULL許容で、アプリでは空欄として扱う。件数を理由に一括で空文字へ書き換えない。読込・取消だけでは保存内容は変わらない。

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
- レシート更新・削除と残高削除の順序は修正済みだが、FKの有効化だけを先に本番へ投入しない。実DBの既存違反やポイント孤児の扱い、他の保存経路を確認してから段階的に試験する。
- `m_shop`と`t_receipts`の店舗名同一性を確認して、安易な名前一致JOINによる誤紐付けを避ける。

**元資料**: [実DDL](schema/monetail_schema.sql)、[DB仕様書](02-database.md)、[ソースと課題](05-issues.md)
