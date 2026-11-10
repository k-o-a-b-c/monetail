# レシートDBの物理設計（DBモデル・初期化実装済み）

更新: 2026-11-10。対象は**新APIのSQLite/EF Core**。旧VB/WinFormsの保存テーブルは変更しない。
本書は**承認済みの業務・保存要件**と、**実装前に確認する物理列・索引・制約の設計案**を区別する。表に示す列や索引のうち明示的に確定していない物理仕様は、設計候補のまま扱う（Codexが独断で採用しない）。採用状況は[実装ガイド](ledger-receipt-implementation.md#db実装の判断境界)を参照。
関連: [科目・税・権限](ledger-core-schema.md#category-receipt-handoff)、[計算・集計](12-calculation-contract.md)、[旧DB](02-database.md)、[既存の再送契約](ledger-operation-retries.md)。

**実装状態：** 4表のEF Coreモデル、複合PK/FK、RESTRICT、科目グループのUNIQUE、行内CHECKと必要な索引を実装した。`ApiDbContext`から既存の`DatabaseSchema`を通じて新規DBへ作成し、初期化・通常利用時の構造照合にも含める。既存DBの自動更新・Migration追加は行わない。[工程2の共通計算・検証](ledger-receipt-api.md#工程2の実装と工程3への接続)も実装済み。詳細GET/書込APIと、保存トランザクション内のマスタ照会・有効期間/所属・使用済み科目の収支変更拒否への接続も実装済み。一覧・候補・WinUIは後続工程。本工程ではDBスキーマ・初期化方式を変更しない。

実装上の選択は、取引日時を`DateTime`と固定書式のTEXT変換（読込時は`Unspecified`）で保持し、監査日時は既存家計簿と同じUTC用`DateTime`とした。FKは親子もRESTRICTとし、必要なFK列索引はEFの規約と複合PK/索引の先頭列を利用する。一覧の必須索引は昇順B-treeの逆走査で日時・IDのBINARY降順を処理できることを実行計画で検証した。追加候補の支払方法・日時複合索引は今回追加していない。

## 1. 全体構造とキー

| テーブル | 粒度 | 主キー（確定） | 親 |
| --- | --- | --- | --- |
| `ledger_receipts` | レシート1件 | (`ledger_id`, `receipt_id`) | 家計簿・支払方法 |
| `ledger_receipt_groups` | 科目ごとのグループ | (`ledger_id`, `group_id`) | レシート・科目 |
| `ledger_receipt_details` | 購入・収支明細1行 | (`ledger_id`, `detail_id`) | グループ |
| `ledger_receipt_detail_people` | 明細に対応する人物1人 | (`ledger_id`, `detail_id`, `person_id`) | 明細・人物 |

- **確定方針:** 家計簿をまたぐ参照は禁止。業務IDはサーバー発行の**UUID v7**（32文字N形式）、更新版・クライアントの作成`requestId`はUUID v4。IDの採番順を一覧表示順に流用しない。現在のマスタAPIはv7化前のため、業務ID全体の切り替えは既存データ・既存認証との整合性を別途確認する。
- **確定方針:** 既存マスタと同じく**`ledger_id`を含む複合PK**とし、レシート・グループ・明細の**ID単独にUNIQUE索引を追加しない**。UUID v7はサーバーが発行するが、DBで保証する一意性は各家計簿内の複合キー単位。これは既存の科目・人物・支払方法と同じ設計であり、家計簿をまたぐID単独の一意性をDB要件にしない。グループの「同じレシート内に同科目を重複させない」複合UNIQUE索引は別途設ける。
- 子テーブルには`ledger_id`を必ず持ち、すべてのFKは**同一家計簿内の複合FK**とする。参照中のマスタ・認証アカウントの削除はRESTRICT。**レシート削除は、子の対象人物→明細→グループ→親を単一トランザクションで削除する**保存契約に従う。SQLite FKのCASCADEに頼ることを必須としない。
- 新規レシートのヘッダーから明細・対象人物まで一括確定する。部分保存や親だけのコミットは行わない。

## 2. `ledger_receipts`（レシート親）

列名は旧版の `date` → `transaction_at`、`tax_type` → `tax_inclusion_mode`、`total` → `total_amount` に整理した。**これは新テーブルの列名であり、旧版の列名・値・計算処理は変更しない。** 列順はキー → 取引内容 → 税・周期・金額 → メモ → 更新版・監査情報の順。

| 列 | SQLite型・NULL | 内容 |
| --- | --- | --- |
| `ledger_id` | TEXT NOT NULL | 家計簿ID、PKの一部 |
| `receipt_id` | TEXT NOT NULL | UUID v7、PKの一部 |
| `transaction_at` | TEXT NOT NULL | **取引日時を時刻まで保持**。旧版のローカル日時をUTCへ読み替えず、日付部分を税率履歴・有効期間の照合に使う。**日時TEXTは`yyyy-MM-ddTHH:mm:ss`（ISO 8601、タイムゾーンなし、秒精度、小数秒なし）とし、APIの`transactionAt`も同じ文字列（DEC-253）** |
| `shop_name` | TEXT NOT NULL | 店名。マスタFKは設けず、自由入力。**上限100 UTF-16コード単位（DEC-211）** |
| `shop_location` | TEXT NULL | 店舗所在地。**上限100 UTF-16コード単位（DEC-211）** |
| `payment_method_id` | TEXT NOT NULL | `(ledger_id, payment_method_id)`で支払方法FK |
| `tax_inclusion_mode` | INTEGER NOT NULL | 0=税込、1=税抜（旧版`tax_type`の値と計算契約は維持） |
| `tax_rounding_mode` | TEXT NOT NULL | `floor` / `half_up` |
| `recurrence_interval` | INTEGER NULL | 周期間隔。設定時は整数、1〜2,147,483,647（Int32） |
| `recurrence_unit` | TEXT NULL | `Week` / `Month` / `Year` |
| `total_amount` | INTEGER NOT NULL（確定） | 保存時に計算した合計を必ず保持。符号付きInt32範囲内。旧版のNULL値は移行時に個別対応 |
| `memo` | TEXT NULL | レシート自由記述。旧`tag`相当 |
| `revision` | TEXT NOT NULL | レシート単位の楽観的更新版（UUID v4） |
| `created_by_auth_user_id` | TEXT NOT NULL | 作成した認証アカウントID |
| `created_at_utc` | TEXT NOT NULL | 作成UTC日時 |
| `updated_by_auth_user_id` | TEXT NOT NULL | 最終更新した認証アカウントID（新規時は作成者） |
| `updated_at_utc` | TEXT NOT NULL | 最終更新UTC日時（新規時は作成日時） |

**業務上必要な制約と検索用索引候補**

- `FK(ledger_id)→ledgers(id)`、`FK(ledger_id,payment_method_id)→ledger_payment_methods(ledger_id,payment_method_id)`を設ける。**作成者・更新者の両IDはそれぞれ`AspNetUsers(Id)`へのRESTRICT FKを設定する（確定）**。レシート監査情報に参照されている認証アカウントは物理削除不可とし、無効化しても履歴と認証IDを残す。アカウント退会・削除機能は現時点で未実装であり、将来の退会仕様は別途検討する。
- `CHECK(tax_inclusion_mode IN (0,1))`、`CHECK(tax_rounding_mode IN ('floor','half_up'))`、`total_amount`はNOT NULLかつ符号付きInt32範囲内。
- 周期は**両列NULL（初期値：なし）または両列非NULL**。非NULL時は`typeof(recurrence_interval) = 'integer'`かつ`recurrence_interval BETWEEN 1 AND 2147483647`、`recurrence_unit IN ('Week','Month','Year')`をDBで検証し、APIでも同じ値域を検証する。隔週は2+Week、3か月ごとは3+Month。曜日・営業日などの規則列は持たない。
- 周期がある場合、レシート内に`occurrence_type='Regular'`の明細が**1件以上必須**。周期なしでも定期明細を認める。これは行をまたぐためAPIの一括保存トランザクションで検証する。
- **一覧GETのキーセットページングに用いる索引**：`(ledger_id, transaction_at, receipt_id)`はDEC-231で参照する検索順序に合わせて整備する（時刻降順・IDのBINARY降順を検証）。**追加最適化の候補**：支払方法絞り込み用の`(ledger_id, payment_method_id, transaction_at, receipt_id)`は、検索の実行計画・計測で必要性を確認して採否を判断する。両者を無条件に「確定済み索引2本」と扱わない。日時TEXTは**DEC-253の`yyyy-MM-ddTHH:mm:ss`（小数秒なし・オフセットなし）**に統一し、字句順が時系列順と一致する。
- `revision`はレシート親のみ。明細・グループ単位の更新版は持たない。

## 3. `ledger_receipt_groups`（科目グループ）

**税率は明細単位で保持**し、グループは科目とメモのみを保持する。同じ科目内に異なる税率の明細を登録できる（DEC-155）。

| 列 | SQLite型・NULL | 内容 |
| --- | --- | --- |
| `ledger_id` | TEXT NOT NULL | 家計簿ID、PKの一部 |
| `group_id` | TEXT NOT NULL | UUID v7、PKの一部 |
| `receipt_id` | TEXT NOT NULL | 同一家計簿レシートへのFK |
| `category_id` | TEXT NOT NULL | 同一家計簿科目へのFK |
| `memo` | TEXT NULL | グループ自由記述 |

- **同一レシート・同一科目のグループは1件**。率が異なってもグループを分割しない。
- 重複禁止は通常の複合UNIQUE索引1本。`category_id`はNOT NULLであり、税率がグループにないため、税率NULL用の部分UNIQUE索引は不要。

```sql
CREATE UNIQUE INDEX ux_ledger_receipt_groups_category
  ON ledger_receipt_groups(ledger_id, receipt_id, category_id);
```

- **グループの表示順列はなし**。科目の`display_order ASC, category_id ASC`に従う。グループ自体は税率で並べ替えない。
- 科目の`tax_category_code`は明細作成時の税率初期選択だけに用いる。グループに税区分・税率・課税対象フラグを持たせない。
- 使用済み科目の`income_expense`変更は拒否。名前・初期税区分など他の編集は可能。参照中の科目削除はRESTRICT。

## 4. `ledger_receipt_details`（明細）

旧版の `fee` → `unit_price`、`discount_fee` → `unit_discount_amount` に変更し、`display_order` を親キー直後に置く。実税率`tax_rate`は明細単位で保持する。旧Windows版の物理列・計算処理は変更しない。

| 列 | SQLite型・NULL | 内容 |
| --- | --- | --- |
| `ledger_id` | TEXT NOT NULL | 家計簿ID、PKの一部 |
| `detail_id` | TEXT NOT NULL | UUID v7、PKの一部 |
| `group_id` | TEXT NOT NULL | 同一家計簿グループへのFK |
| `display_order` | INTEGER NOT NULL | グループ内の並び順。`typeof()`で整数かつ0〜2,147,483,647（Int32）を検証 |
| `name` | TEXT NOT NULL | 明細名。**上限100 UTF-16コード単位（DEC-211）** |
| `unit_price` | INTEGER NOT NULL | 単価、符号付きInt32（旧版`fee`） |
| `quantity` | INTEGER NOT NULL | 数量、1以上・Int32上限以下 |
| `unit_discount_amount` | INTEGER NOT NULL | 1単位の値引額、符号付きInt32（旧版`discount_fee`） |
| `discount_rate` | TEXT NOT NULL | 率値引（%）、Decimal、0～100、小数可 |
| `tax_rate` | TEXT NULL | **実税率（%）、Decimal。NULL=非課税、'0'=課税0%** |
| `expense_type` | TEXT NOT NULL | `Fixed` / `Variable` / `Excluded` |
| `occurrence_type` | TEXT NOT NULL | `Regular` / `Irregular` / `Temporary` |
| `memo` | TEXT NULL | 明細自由記述 |

- 明細金額：`x=(unit_price-unit_discount_amount)*quantity`、`discount_by_rate=Ceiling(x*discount_rate/100)`、`net=x-discount_by_rate`。**Decimalで計算し、従来の値引と税端数の規則を維持**。単価・値引額・数量・合計等はInt32範囲を検証し、超過ならAPIで保存拒否。入力値の0・100への自動補正は行わない。
- `discount_rate`と`tax_rate`はDecimal文字列のTEXT保存。APIはInvariantCultureで解析・正規化して数値を比較し、`8`・`8.0`・`08`を同じ値として扱う。非課税NULLと課税0%は別物。形式・数値範囲をAPIで検証する。TEXTの辞書順で税率を比較しない。
- **明細の税率候補は取引日に有効な全税区分履歴から数値重複を除いたもの＋非課税**。新規明細では科目の`tax_category_code`と取引日から得た税率を初期選択する。履歴がなければ別区分・別日付から代用せず明示選択を要求。明細ごとに変更可能。同じグループに非課税・課税0%・複数税率を混在させられる。
- **保存時は新規・更新・流用による新規登録のいずれでも、全明細の税率を取引日の有効な候補と照合**する。候補外の率がある場合は保存を拒否し、利用者による再選択を要求する。取引日・税率・明細が未変更の更新でも例外を設けないため、税率履歴が後から変更された場合はメモなど他項目のみの編集でも保存できないことがある。保存済みの税率を自動変更しない。
- **税込・税抜と端数方式はレシート単位のまま**。レシート合計は科目・グループをまたいで同じ数値税率の課税明細を合算・丸め、非課税分は別計上。課税0%と非課税は区別する。グループ・明細・人物の参考額の合計とレシート合計に差が生じ得る。**複数税率グループの参考額は[API・計算契約](ledger-receipt-api.md)のDEC-156・157**に従う。
- **費用区分の初期値・選択肢は科目収支区分に依存**：収入科目は`Excluded`のみ、支出科目は`Fixed`/`Variable`/`Excluded`（初期値`Variable`）。`net`の正負に関係なくAPIでも検証する。
- 発生区分は`Regular`/`Irregular`/`Temporary`、初期値`Irregular`。レシート周期ありなら少なくとも1明細がRegular。
- 明細は`display_order ASC, detail_id ASC`で表示。順序は非負、同順序は許容しIDで安定ソートする。
- 明細名の上限は**100 UTF-16コード単位（DEC-211）**。メモの長さ制約の扱いはまだ未決定であり、未承認の追加上限を作らない。

レシート書込・読取の詳細なHTTP/JSON契約は[レシートAPI](ledger-receipt-api.md)、画面・履歴候補は[WinUI](ledger-receipt-ui.md)・[候補](ledger-receipt-suggestions.md)を参照。

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
- 基本的な`CHECK`：各列の列挙値、Int32範囲、数量正値、周期2列のNULL一致、表示順非負。Decimalの解析、**明細ごとの税率と取引日の適合性**、科目別費用区分、周期に対応する定期明細の有無、取引日に有効な科目・支払方法・人物などの条件はAPIの保存トランザクションで検証する。

- レシート削除は子の対応人物→明細→グループ→親の順に実施し、失敗時は全取消。読込だけでは保存済み合計・税率・明細・履歴を補正しない。
- 同じ数値税率の課税**明細**は科目やグループが異なってもレシート全体で合算し税端数処理。非課税は別集計し課税0%と区別する。グループ/明細/人別の参考額が親合計と1円以上ずれる場合があることを保持する。

## 実装境界

- 本書はDB4表・FK・列・索引・CHECKの物理設計の参照先。**業務ルール・複合PK/FK・同一科目グループの一意性・保存条件は必須**であり、実装方法の細部（特に追加検索索引）は検証可能な設計判断と区別する。明示的に未承認の物理列・制約を確定扱いしない。最初のDB実装PRでは[工程別の判断境界](ledger-receipt-implementation.md#db実装の判断境界)を確認する。

- **正式リリース前**はEF Core Migrationを追加しない。`initialize-db`が現行EFモデルから**空の新規DBを最新スキーマで作成**する。既存DBを自動更新・変換しない。**正式リリース後**にMigrationを導入する方針。運用境界は[API DB初期化](42-api-foundation.md#db初期化登録復旧コマンド)を正本とする。
