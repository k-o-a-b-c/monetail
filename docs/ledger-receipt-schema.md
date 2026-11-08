# レシートDBの物理設計案（未実装・レビュー用）

更新: 2026-11-08。対象は**新APIのSQLite/EF Core**。旧VB/WinFormsの保存テーブルは変更しない。
確定済みの業務仕様と、実装前に確認する物理設計上の**提案**を区別する。本文の「提案」は利用者確認を経るまで確定仕様ではない。
関連: [科目・税・権限](ledger-core-schema.md#category-receipt-handoff)、[計算・集計](12-calculation-contract.md)、[旧DB](02-database.md)、[既存の再送契約](ledger-operation-retries.md)。

## 1. 全体構造とキー

| テーブル | 粒度 | 主キー（提案） | 親 |
| --- | --- | --- | --- |
| `ledger_receipts` | レシート1件 | (`ledger_id`, `receipt_id`) | 家計簿・支払方法 |
| `ledger_receipt_groups` | 科目と税率ごとのグループ | (`ledger_id`, `group_id`) | レシート・科目 |
| `ledger_receipt_details` | 購入・収支明細1行 | (`ledger_id`, `detail_id`) | グループ |
| `ledger_receipt_detail_people` | 明細に対応する人物1人 | (`ledger_id`, `detail_id`, `person_id`) | 明細・人物 |

- **確定方針:** 家計簿をまたぐ参照は禁止。業務IDはサーバー発行の**UUID v7**（32文字N形式）、更新版・クライアントの作成`requestId`はUUID v4。IDの採番順を一覧表示順に流用しない。現在のマスタAPIはv7化前のため、業務ID全体の切り替えは既存データ・既存認証との整合性を別途確認する。
- **物理設計案:** 既存マスタに合わせて複合PKを採用し、レシート/グループ/明細のID単独にもUNIQUE索引を付け、DBでの全家計簿横断の重複を防ぐ。なお現行の科目・人物・支払方法は`(ledger_id, *_id)`がPKであり、**これだけでは全家計簿横断のID一意性をDBで保証しない**。将来の全業務ID一意性要件に合わせるには既存マスタも別途点検する。
- 子テーブルには`ledger_id`を必ず持ち、すべてのFKは**同一家計簿内の複合FK**とする。親・マスタの削除はRESTRICT、レシート自身の削除だけ明細・対応人物・グループを**同一トランザクションで明示的に削除**する案（SQLite FKのCASCADEに依存しない）。
- 新規レシートのヘッダーから明細・対象人物まで一括確定する。部分保存や親だけのコミットは行わない。

## 2. `ledger_receipts`（レシート親）

| 列 | SQLite型・NULL | 内容 |
| --- | --- | --- |
| `ledger_id` | TEXT NOT NULL | 家計簿ID、PKの一部 |
| `receipt_id` | TEXT NOT NULL | UUID v7、PKの一部 |
| `date` | TEXT NOT NULL | **取引日時を時刻まで保持**。旧版のローカル日時をUTCへ読み替えず、日付部分を税率履歴・有効期間の照合に使う。具体的なTEXT書式と秒未満の精度はAPI契約で定める |
| `shop_name` | TEXT NOT NULL | 店名。マスタFKは設けず、自由入力 |
| `shop_location` | TEXT NULL | 店舗所在地 |
| `payment_method_id` | TEXT NOT NULL | `(ledger_id, payment_method_id)`で支払方法FK |
| `tax_type` | INTEGER NOT NULL | 0=税込、1=税抜（旧計算契約と同じ） |
| `tax_rounding_mode` | TEXT NOT NULL | `floor` / `half_up` |
| `memo` | TEXT NULL | レシート自由記述。旧`tag`相当 |
| `total` | INTEGER NULL（提案） | 保存時の計算合計。通常の新規API登録では必須・Int32内。旧データのNULL取り込み余地を残す案 |
| `recurrence_interval` | INTEGER NULL | 周期間隔。設定時は1以上の正整数 |
| `recurrence_unit` | TEXT NULL | `Week` / `Month` / `Year` |
| `revision` | TEXT NOT NULL | レシート単位の楽観的更新版（UUID v4） |
| `created_by_auth_user_id` | TEXT NOT NULL | 作成した認証アカウントID |
| `created_at_utc` | TEXT NOT NULL | 作成UTC日時 |
| `updated_by_auth_user_id` | TEXT NOT NULL | 最終更新した認証アカウントID（新規時は作成者） |
| `updated_at_utc` | TEXT NOT NULL | 最終更新UTC日時（新規時は作成日時） |

**制約／索引案**

- `FK(ledger_id)→ledgers(id)`、`FK(ledger_id,payment_method_id)→ledger_payment_methods(ledger_id,payment_method_id)`、作成者・更新者→`AspNetUsers(Id)`のRESTRICT FKを提案する。監査情報がアカウント削除を阻害し得るため、認証アカウント削除運用とは整合性確認が必要。
- `CHECK(tax_type IN (0,1))`、`CHECK(tax_rounding_mode IN ('floor','half_up'))`、`total`が非NULLならInt32範囲内。
- 周期は**両列NULL（初期値：なし）または両列非NULL**。非NULL時は`recurrence_interval >= 1`かつ`recurrence_unit IN ('Week','Month','Year')`をDBとAPIの両方で検証する。隔週は2+Week、3か月ごとは3+Month。曜日・営業日などの規則列は持たない。
- 周期がある場合、レシート内に`occurrence_type='Regular'`の明細が**1件以上必須**。周期なしでも定期明細を認める。これは行をまたぐためAPIの一括保存トランザクションで検証する。
- 一覧・残高参照用の`(ledger_id,date,receipt_id)`、`(ledger_id,payment_method_id,date,receipt_id)`索引を提案する。日時TEXTは時系列で字句順が一致する正規化書式を使用し、API/DBで同一書式へ統一する（厳密な小数秒精度などは後続で確定）。
- `revision`はレシート親のみ。明細・グループ単位の更新版は持たない。

## 3. `ledger_receipt_groups`（科目・税率グループ）

| 列 | SQLite型・NULL | 内容 |
| --- | --- | --- |
| `ledger_id` | TEXT NOT NULL | 家計簿ID、PKの一部 |
| `group_id` | TEXT NOT NULL | UUID v7、PKの一部 |
| `receipt_id` | TEXT NOT NULL | 同一家計簿レシートへのFK |
| `category_id` | TEXT NOT NULL | 同一家計簿科目へのFK |
| `tax_rate` | TEXT NULL | 実際の税率（%）をDecimal文字列で保存。**NULL=非課税、'0'=課税0%** |
| `memo` | TEXT NULL | グループ自由記述 |

- 同一レシート内の**同じ科目・同じ数値の税率は1グループだけ**。科目は同じで税率が違う場合だけ複数グループを認める。非課税も科目ごとに1件。
- **SQLiteではNULLを含む通常UNIQUEでは非課税行が重複する。** 次の2本の部分UNIQUE索引で保護する案。

```sql
CREATE UNIQUE INDEX ux_receipt_group_taxable
  ON ledger_receipt_groups(ledger_id, receipt_id, category_id, tax_rate)
  WHERE tax_rate IS NOT NULL;
CREATE UNIQUE INDEX ux_receipt_group_exempt
  ON ledger_receipt_groups(ledger_id, receipt_id, category_id)
  WHERE tax_rate IS NULL;
```

- 税率文字列はAPIでInvariantCultureの正規表記へ統一し、`8`・`8.0`・`08`を同じ値として保存・照合する。生SQLが非正規文字列を書き込む場合の防御は追加検討（SQLiteのTEXT UNIQUEだけでは数値同値を保証しない）。
- 初期税率は科目の`tax_category_code`と取引日の履歴から選択するが、**グループには`tax_category_code`を保存しない**。選択肢は取引日に有効な全税区分の履歴の数値税率を重複排除したものと非課税。該当日付の税履歴がなければ勝手に代替しない。
- **並び順列はなし**。表示は科目の`display_order ASC` → 同順位なら`category_id ASC` → 同科目では数値税率のDecimal値**降順** → **非課税を最後**。TEXTを辞書順でソートしない（`10`と`8`の誤順を避ける）。必要ならAPI/.NET側でDecimal比較して並び替える。
- 課税対象フラグを明細に持たず、グループの`tax_rate`だけで扱う。既存の「レシート全体を**税率ごとに合算**してから端数処理」という計算契約は維持する。
- 使用済み科目の`income_expense`の変更は拒否（同じトランザクションで使用有無を照合）。名前・初期税区分など他の編集は引き続き可能。参照中の科目削除はRESTRICT。

## 4. `ledger_receipt_details`（明細）

| 列 | SQLite型・NULL | 内容 |
| --- | --- | --- |
| `ledger_id` | TEXT NOT NULL | 家計簿ID、PKの一部 |
| `detail_id` | TEXT NOT NULL | UUID v7、PKの一部 |
| `group_id` | TEXT NOT NULL | 同一家計簿グループへのFK |
| `name` | TEXT NOT NULL | 明細名 |
| `fee` | INTEGER NOT NULL | 単価、符号付きInt32 |
| `quantity` | INTEGER NOT NULL | 数量、1以上・Int32上限以下 |
| `discount_fee` | INTEGER NOT NULL | 1単位の値引額、符号付きInt32 |
| `discount_rate` | TEXT NOT NULL | 率値引（%）、Decimal、0～100、小数可 |
| `expense_type` | TEXT NOT NULL | `Fixed` / `Variable` / `Excluded` |
| `occurrence_type` | TEXT NOT NULL | `Regular` / `Irregular` / `Temporary` |
| `memo` | TEXT NULL | 明細自由記述 |
| `display_order` | INTEGER NOT NULL | グループ内の並び順、0以上 |

- 明細金額：`x=(fee-discount_fee)*quantity`、`discount_by_rate=Ceiling(x*discount_rate/100)`、`net=x-discount_by_rate`。**Decimalで計算し、既存の計算/税端数処理を維持**。明細単価・値引額・数量・レシート合計等はInt32範囲を検証し、計算超過ならAPIで保存拒否。入力値の0・100への自動補正は行わない。
- `discount_rate`は税率履歴と同じTEXT形式。APIは不変カルチャのDecimalとして厳密に解析・正規化し、0～100を検証。TEXTへのレキシカル比較で数値範囲を判定しない。
- **費用区分の初期値・選択肢は科目の収支区分に依存**：収入科目は`Excluded`のみ（初期値も同じ）。支出科目は3種類すべて（初期値`Variable`）。`net`の正負では変更しない。API保存時にも整合性を検証。
- 発生区分は全科目で3種類、初期値`Irregular`。発生周期は**明細に持たせない**。レシート周期と発生区分は片方向の関係（周期あり→少なくとも1明細がRegular）。
- 明細は`display_order ASC, detail_id ASC`で表示する。順序は非負とし、同順序の一意性は付けず安定したタイブレークを設ける。
- 詳細の名前文字数・メモの長さ等は既存UI仕様との整合後に確定。DB型のみ先行して示す。

## 5. `ledger_receipt_detail_people`（対象人物）

| 列 | SQLite型・NULL | 内容 |
| --- | --- | --- |
| `ledger_id` | TEXT NOT NULL | 家計簿ID、PKの一部 |
| `detail_id` | TEXT NOT NULL | 同一家計簿明細へのFK、PKの一部 |
| `person_id` | TEXT NOT NULL | 同一家計簿人物へのFK、PKの一部 |

- 明細に紐づく人物は**0人以上**。同じ人物の重複をPKで防ぐ。人物は家計簿の人物IDであり、認証ユーザーIDではない。
- 人別の金額按分は別の金額列で保持せず、既存の参考額計算を利用する。人数で割った端数の扱いは[計算仕様](12-calculation-contract.md)を維持する。
- 人物の削除は参照が残る間RESTRICT。作成/編集/削除は親レシートのトランザクションで実施する。

## 6. FK・保存・読取の原則

- 必須の複合FK：グループ→レシート、グループ→科目、明細→グループ、明細対象者→明細/人物、親→支払方法。いずれも`ledger_id`を含む。必要な親の`(ledger_id,id)`キーと子FK列索引を用意する。
- 基本的な`CHECK`：各列の列挙値、Int32範囲、数量正値、周期2列のNULL一致、表示順非負。Decimalの解析、科目別費用区分、周期に対応する定期明細の有無、取引日に有効な税率・科目・支払方法・人物などの条件はAPIの保存トランザクションで検証する。
- 作成は既存`operation_requests`で`receipt.create`を識別して再送保護する案（再送記録の保持方式は取引件数増加も考慮して再確認）。修正/削除は親`revision`を必須とし、子階層まで**まとめて**保存・置換する案。成功時に新`revision`、`updated_by_auth_user_id`、`updated_at_utc`を更新する。
- 操作権限：Adminは全レシート、Viewerは閲覧のみ、Editorは対応人物が所有する支払方法のレシートを操作。**支払方法を変更する編集では旧・新の両方の所有者を照合**。監査の作成者は権限制御に使わない。書込トランザクション中にも最新の参加・権限を照合する。
- レシート削除は子の対応人物→明細→グループ→親の順に実施し、失敗時は全取消。読込だけでは保存済み合計・税率・明細・履歴を補正しない。
- 同じ税率の課税明細は科目やグループが異なっても**レシート全体のその税率で合算**して税端数処理。グループ/明細/人別の参考額が親合計と1円以上ずれる場合があることを保持する。

## 7. 実装前に残る確認事項

| 論点 | 設計案・現状 | 確認が必要な理由 |
| --- | --- | --- |
| 取引日時の詳細書式 | **時刻まで保存すると確定**。`date TEXT NOT NULL` | ローカル日時を維持し、具体的なISO系文字列形式、小数秒の精度、API日時表現を後続で確定 |
| `total`のNULL | DB上はNULL許容、新APIでは計算済み整数必須（提案） | 旧DBにはNULLの保存済み合計があり得る。新DBの移行方針と合わせる |
| IDの横断的一意性 | 新テーブルのID単独UNIQUE（提案） | 既存マスタの複合PKはID単独の一意性を物理保証していない。UUID v7導入時の整合が必要 |
| 監査ユーザーと削除 | 監査ユーザーIDにRESTRICT FK（提案） | 認証ユーザーの削除ルールと矛盾しないか確認 |
| 有効期間と再編集 | 取引日基準の参照条件をAPIでチェック（既存原則） | 過去データ編集時に期間や税率履歴が後変更された場合の扱いをAPI契約で詳細化 |
| レシートのAPI形 | POST/PATCH/DELETEで子階層を含む一括保存を提案 | 部分更新や子ID保持、競合応答、再送ハッシュの具体形は別途API仕様で確定 |
| 旧版データ引継ぎ | **実データ検査まで保留** | 同科目同税率の重複グループや明細ごとの課税フラグ混在があれば、移行前手動修正も可。自動統合は前提としない |

## 8. 今回の対象外

- EF Coreモデル・Migration・DB表作成・API・Windows画面の実装。ここは**文書による物理モデル案**のみ。
- 店舗マスタの必須化、明細への税率・税区分、別個のグループ表示順、明細の周期列、ポイント明細（旧`t_receipt_points`）。ポイント機能は別途検討。
- 「週・月・年」から次回の入力候補を計算する際の月末・曜日・営業日ルール。現時点では周期を**保存するだけ**。
