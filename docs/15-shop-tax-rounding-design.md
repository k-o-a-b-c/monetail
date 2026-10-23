# 15 レシート単位の税端数処理 — 詳細設計（レガシー版）

- 作成: 2026-10-17
- 更新: 2026-10-19 / 店舗履歴による初期選択、ラジオボタン、登録確定までReceiptの両税設定を変更しない方式を反映
- 状態: **税端数処理はmainへ反映済み。現行コード全体のテスト構成・件数・最新CI結果は [tests/README.md](../tests/README.md) を参照。手動のWindows画面確認・実データでの検証は未実施。実行方法は [tests/README.md](../tests/README.md)**
- 対象: WinForms / VB.NET / SQLite
- 関連: `LEG-019`・[08 改修要件](08-requested-enhancements.md)・[14 決定記録](14-decisions-2026-10-16.md)

## 1. 確定した設計原則

1. **端数処理は各レシートの属性**。`t_receipts.tax_rounding_mode` を保存済みレシートの計算時の参照元（正本）とする。入力画面の合計プレビューには未確定の選択値を使う。
2. **初期値は店舗ごとの直近レシートから取得**する。支払い方法の入力補助と同様に、店名と現在の利用者で履歴を検索する。**店舗登録は必須ではない**。店舗マスタの値は初期選択に使わない。
3. マスタ未登録／過去伝票由来の候補／自由入力の店舗でも、**同じ履歴検索と直接選択が可能**。履歴がない場合の初期値は`floor`。
4. **移行前から存在するレシートは従来の`floor`を維持**。後から別のレシートが保存されても、過去分・編集中の伝票を自動で変えない。
5. 消費税は**税率ごとに合算してから端数処理**。分類・商品ごとの丸めの合計を会計上の正本にしない。
6. **オーケーの3/103割引は現行の`discount_fee`への手入力を継続**。割引の専用入力欄／分数自動計算／追加DB列は作らない。
7. 個人別金額の配分は概算とし、新しい`half_up`では最終端数を四捨五入する。floorの参考集計は切り捨て規則を維持し、共通税計算はDecimalを使う。個人配分の合計で会計総額を上書きしない。

**設計変更（2026-10-17）**: 以前検討した`t_receipts.shop_code`の追加・FK・店舗マスタへの強制紐付け・店の新規登録導線は**採用しない**。

## 2. 追加DBカラムとマイグレーション

| テーブル | 新カラム | 型・既定値 | 用途 |
| --- | --- | --- | --- |
| `m_shop` | `tax_rounding_mode` | `TEXT NOT NULL DEFAULT 'floor' CHECK(... IN ('floor','half_up'))` | 既存設計の移行互換性のため保持する列。現実装では初期選択・編集に使わない |
| `t_receipts` | `tax_rounding_mode` | `TEXT NOT NULL DEFAULT 'floor' CHECK(... IN ('floor','half_up'))` | **レシートごとに適用した方式** |

- `floor`＝切り捨て、`half_up`＝四捨五入。文字列は互換性のため固定し、UIのみ日本語表示。
- **新規カラムはこの2つのみ**。`shop_code`も割引関連カラムも追加しない。
- 既存`shop_name`は文字列として維持。`m_shop`との物理FKは追加しない。
- `t_receipts`の古い行のモードはDDLの既定値で`floor`となる。過去の`total`を更新せず、既存のレシート金額・分類・人への割当を変更しない。
- 既存の`m_shop`の列もすべて`floor`。既にテスト用DBへ適用した移行との互換性を保つため列を維持するが、マスタの編集機能は追加せず、初期選択では参照しない。
- SQLite`ALTER TABLE ADD COLUMN`の**一回限りの移行**。適用済みDBへ再実行しない。`PRAGMA user_version`、`PRAGMA foreign_keys`は変更しない。
- 移行SQL: [migrations/20261017_01_shop_tax_rounding.sql](migrations/20261017_01_shop_tax_rounding.sql)。実行前にDBのバックアップを取り、テスト用コピーで試す。

## 3. 店舗選択・端数方式のUI仕様

### 3.1 基本画面

`RegisterForm`の消費税欄付近に「税端数：切り捨て／四捨五入」のラジオボタンを設ける。`TaxRoundingFloorRadioButton` と `TaxRoundingHalfUpRadioButton` は専用の `TaxRoundingModePanel` に配置し、内税／外税とは独立した選択グループにする。

**すべてのレシートで編集可能**。店舗がマスタに登録されているかは関係ない。

| 操作 | レシートの初期／保存モード |
| --- | --- |
| 新規レシートで店舗候補を選ぶ | 同じ店名・現在の利用者の直近レシートから方式をコピー |
| 履歴候補 `Shop.Code=0`／自由入力の店名を確定 | 登録済み店舗と同じ履歴検索。店舗登録は不要 |
| 履歴のない店舗 | `floor`。レシートの選択欄から変更可能 |
| 店舗名を変更した新規レシート | ユーザーが丸め方式を手動変更済みなら保持。未変更なら変更先の履歴から初期選択 |
| 保存済みレシートを編集／店名を変更 | 保存されたレシートの方式を保持。履歴で上書きしない |
| 保存済みレシートで方式を変更して再保存 | そのレシートだけ変更する。以後の新規入力では最新の履歴が候補になる |
| 保存済みレシートから流用登録 | 画面の内容・端数方式を使い、従来どおり確認後に新規保存して閉じる。追加の登録操作は不要 |
| 編集をキャンセル | 内税／外税 (`TaxType`) と端数方式は登録確認まで画面だけに保持するため、元のレシートの両設定は変わらない。共有する同じレシートを開き直しても編集前の状態を表示 |

### 3.2 履歴検索

`DataAccess.GetTaxRoundingMode(shopName)` は `t_receipts` を `m_payment_classification`、`m_user` と結合し、店名と `Environment.UserName` に一致する支払い履歴から取得する。支払い方法の履歴と同じ利用者の範囲で検索し、別の利用者の方式を混ぜない。

- 店名はパラメーターとして渡す。
- `ORDER BY r.date DESC, r.seq DESC LIMIT 1`。同日時はレシート番号の大きい行を最新とする。
- 対象履歴がなければ `floor`。
- `m_shop` の登録・コード・端数列は初期選択に関与しない。
- マスタメンテナンス画面は既存の空の雛形のまま、入口の設定ボタンも従来どおり非表示とする。

### 3.3 タイミング

- `RegisterForm_Load`: 保存済みレシートの方式を復元。新規ならまず `floor`。
- `ShopNameComboBox` の選択確定時、または自由入力の検証完了時: 新規かつ手動未変更なら直近履歴の方式を初期選択。
- 文字入力中は手動未変更の方式を一旦 `floor` に戻し、確定時に履歴検索する。入力のたびにDBへ問い合わせない。
- 税端数ラジオボタン変更時: 選択値は画面だけで保持する。`Receipt.GetTotal(taxType, taxRoundingMode)` へ画面の内税／外税・端数方式を渡して合計を再計算し、手動選択を記憶する。既に選択されたボタンをクリックした場合も手動選択として保持する。初期化・履歴による自動設定では手動変更扱いにしない。
- 新規入力・既存レシート編集時: 内税／外税・端数方式の初期選択、履歴設定、手動変更で `Receipt.TaxType` と `Receipt.TaxRoundingMode` を変更しない。保存せず閉じても復元処理は不要。
- 保存時: 通常登録・流用登録の確認をすべて通過した後、店名などと同じタイミングで画面の内税／外税・端数方式をReceiptへ転記し、方式と合計を同じトランザクションで保存する。確認で「いいえ」を選んだ場合は転記しない。保存直前に履歴を再取得しない。

## 4. 現行計算ロジックと変更範囲

### 4.1 現行仕様として固定する処理

`Receipt.Total`は各明細に以下の計算を行い、課税／非課税に分け、**レシート内の税率ごとに金額を合算**している。

```text
lineNet = (fee - discount_fee) * quantity
        - Ceiling(((fee - discount_fee) * quantity) * discount_rate / 100)
```

この値引方式は変更しない。オーケーの会員割引も従来の`discount_fee`として扱う。

### 4.2 税率別の丸め

`TaxType=1`（税抜入力）に対して、税率`r`、その税率の課税対象額の合計`T_r`、非課税対象額の合計`N_r`とする。

```text
floor:
  tax_rate_subtotal = Math.Floor(T_r * (1D + r / 100D) + N_r)  // 合算額・税込額はDecimal
half_up:
  tax = Math.Round(Decimal(T_r) * r / 100, 0, AwayFromZero)
  tax_rate_subtotal = T_r + tax + N_r
Receipt.Total = sum(tax_rate_subtotal for all r)
```

**税率が同じ複数グループを必ず合算**してから端数処理する。異なる税率を混ぜてから丸めない。

- `TaxType=0`（税込入力）は`Math.Floor(T_r + N_r)`。整数円の明細では税額を追加しないため、方式を変更しても`Receipt.Total`は変わらない。
- `floor`も合算額・税計算・戻り値をDecimalに統一する。負の方向への切り捨ては維持し、Double由来の境界誤差は修正する。[40 Decimal税計算](40-decimal-tax-calculation.md)。
- `half_up`は税額をDecimalで計算し、`MidpointRounding.AwayFromZero`で丸める実装を採用済み。税額の二進小数誤差による中間値の変化を抑える。
- `Total`／`Incomes`／`Expenses`は対象明細の符号フィルターを変えて同じ集計処理・税率ヘルパーを使用する。画面の合計プレビューは同じ処理へ未確定の選択値を引数で渡す。

### 4.3 返金・負数の例外

現行コードの`Math.Floor(-1.1) = -2`は、ゼロ方向へ切り捨てる計算とは異なる。

- 過去レシートもDecimalで再計算する。旧Double式の−100円・10%は−111円から−110円へ、−50円は−56円から−55円へ修正する。保存済みtotalは変更せず、不一致は一覧で確認する。
- 新`half_up`のマイナス税額は`MidpointRounding.AwayFromZero`を**採用済み**（税額−0.5円→−1円）。負額のみ・正負混在の計算回帰テストが成功している。実運用の返品期待値は導入前に別途確認する。
- `Receipt.Incomes`／`Expenses`は正負を分けて集計する現行挙動を維持する。同じ丸めヘルパーを適用するが、`Total = Incomes + Expenses`を一律保証しない。
- `Calculation.GroupCalculatedFee`／`DetailCalculatedFee`と`Receipt.GetGroupStatisticsList`はグループ／明細単位でレシートの方式を適用する参考計算。粒度による端数差を自動テストし、**会計のレシート合計は税率別合算値を正本**とする。個人配分の`half_up`は最終端数を四捨五入、既存`floor`は従来結果を維持する。

## 5. 修正ファイル・メソッド

| ソース | 修正要件 |
| --- | --- |
| `Data/Master/Shop.vb` | 既存設計の店舗列の読込互換性を保持。入力補助では使用しない |
| `Data/Data/Receipt.vb` | `TaxRoundingMode`（レシート確定値）を追加。`Total`、`Incomes`、`Expenses`の検証・丸め処理を共通化。`GetTotal(taxType, taxRoundingMode)` は状態を変更せず選択値で合計を計算 |
| `Logic/Calculation.vb` | 税端数の共通関数を用意。グループ／明細表示との関係をテスト |
| `DataAccess/DataAccess.vb` `ReadShop` | マスタの税端数列をSELECTして`Shop`へ復元。履歴候補`Code=0`のモードは`floor` |
| `DataAccess/DataAccess.vb` `GetReceipts`／`GetPaymentReceipts` | **両方**のSELECTで`t_receipts.tax_rounding_mode`を取得。残高推算などの読み込み漏れを防ぐ |
| `DataAccess/DataAccess.vb` `RegisterReceipt`／`UpdateReceipt` | 同じトランザクション内で`Receipt.TaxRoundingMode`と`Receipt.Total`を保存 |
| `Control/RegisterForm.vb`／`RegisterForm.Designer.vb` | 選択欄、初期値設定、変更時再計算、既存編集での保持、流用登録を対応 |
| `DataAccess/DataAccess.vb` `GetTaxRoundingMode` | 店舗別・現在の利用者の直近レシートの方式を取得。履歴なしは `floor` |
| `MasterMaintenanceForm` | 新たな編集機能を追加しない |
| 一覧・収支・分類・残高集計 | 一覧・残高は`Receipt.Total`、収支は`Incomes/Expenses`、分類は`GetGroupStatisticsList`の参考額を使用。方式による影響を回帰テスト |

**変更しない**: `t_receipts`に`shop_code`は作らない。`m_shop`とのFKを追加しない。オーケー向け専用の割引データも作らない。

## 6. マイグレーション・互換性と運用

1. 本番SQLiteから一貫性のあるバックアップを作成し、**テスト用のコピー**で移行SQLを実行する。
2. 元の全伝票の`total`や対象者割当は**更新しない**。移行後のレシート方式は一律`floor`となる。
3. `PRAGMA table_info('m_shop')`／`('t_receipts')`と計算値を照合する。
4. 新しいアプリがレシートごとの端数方式を保存・読込できることを確認する。
5. **旧アプリと新アプリのDB共有は行わない**。旧アプリで`half_up`の伝票を編集すると古い`Receipt.Total`が保存合計を上書きし得る。
6. 途中で問題があれば移行前バックアップから**ファイルを復元**する（安易な`DROP COLUMN`をロールバックに使わない）。
7. 後続の外部キー対応（PR #2）で、子データは対象者割当→明細→グループの順に削除するよう修正済み。更新はその後に子を再INSERTし、レシート全体の削除では親を最後に削除する。FK ON/OFFの成功と途中失敗時のDB復元を自動テストしている。DB定義や接続時のFK設定は変更していない。
8. 後続のNULL文字列対応（PR #3）で、所在地とレシート／グループ／明細のタグのNULLをモデルの空文字として読む。読込・取消ではDBのNULLや金額・割当を変更しない。明示保存時は従来の保存処理により空文字を保存する。
9. 後続の小数割引率対応（LEG-005）で、REAL値を読むと例外になった`GetInt32`を共通読込の`GetDouble`へ変更した。既存の小数率も保存値どおりに再計算できる。DBの率や保存合計を読込だけで更新せず、税・割引の計算式も変更しない。

## 7. 受け入れテスト

計算用の期待値: [tests/tax-rounding-cases.json](tests/tax-rounding-cases.json)。JSONの全11件は`tests/CalculationTests`が実際のVBコードへ入力して実行済み。計算テスト20件の一部であり、負額・状態を変更しないプレビューや保存／画面の検証はテストコード側にも定義している。以下は受け入れ仕様の一覧で、実データの全行・手動E2Eの確認完了を意味しない。

| ID | 検証 |
| --- | --- |
| RT-001 | **マスタ未登録店**で切捨／四捨五入を指定し、正しく再読込できる |
| RT-002 | 店舗の直近レシートの方式を新規伝票に初期選択。日付・同日時のseq順・現在の利用者の範囲を確認 |
| RT-003 | `Code=0`の履歴候補と自由入力では、マスタ登録を要求しない |
| RT-004 | 既存レシート編集・別レシートの保存・店名変更で保存済みモードが勝手に変わらない |
| RT-005 | 旧レシートはすべて`floor`。移行前と合計が一致 |
| RT-006 | 新規`half_up`レシートも一覧と残高推算の両経路で同じ金額 |
| RT-007 | 税率ごと合算した端数処理。`19円×2件（10%）`が切捨41円／四捨五入42円 |
| RT-008 | 税込レシートは方式を変更しても合計が変わらない |
| RT-009 | オーケーは`discount_fee`を従来どおり手入力。追加UI・専用割引計算なし |
| RT-010 | 店舗変更の操作中に手動選択したモードが不用意に上書きされない。選択済みのラジオボタンを再クリックした場合も保持 |
| RT-011 | `floor`の負額はDecimalで負の方向へ切り捨て、Double境界誤差を修正。`half_up`は税額−0.5円→−1円。負額・正負混在を自動テストし、返品の実運用確認は別途行う |
| RT-012 | 新規保存／編集／流用でモードが混同されない。編集・履歴設定・確認取消中はReceiptの内税／外税と端数方式を変更せず、登録確認後にだけ選択値を反映 |
| RT-013 | 端数方式のラジオボタンは内税／外税の選択を解除しない。両グループの切替と合計表示を確認 |
| RT-014 | 内税／外税・端数方式を渡す合計プレビューはReceiptの設定・Total・Incomes・Expensesを変更しない |

## 8. 実装で採用した操作・計算

- 店舗マスタ編集画面の作成は見送り、直近の支払い方法と同じ使い方で端数方式を初期選択する。
- 内税／外税・端数方式は画面に保持し、選択値を渡す合計プレビューで確認する。保存確認後にだけReceiptへ転記する。編集用Receiptのディープコピーやキャンセル時の両設定の復元処理は導入していない。明細などを含むReceipt全体の編集方法を変更したものではない。
- 店舗マスタに新たな登録・変更操作を求めない。既存移行SQLは互換性のため2列追加のまま維持し、店舗側の列は初期選択に利用しない。
- 流用は従来の `RegisterReceipt(True)` で確認後にそのまま新規保存し、画面を閉じる。画面上の端数方式を保存し、流用時には履歴で上書きしない。方式を変更する場合は流用ボタンを押す前に選択する。
- `floor` の計算は、負額と浮動小数点境界も含め既存の Double 式を保持する。`half_up` の税額計算は Decimal を使用する。
- 新しい `half_up` の負額は `MidpointRounding.AwayFromZero`（税額 −0.5円→−1円）。返品・正負混在の回帰テストを用意し、実運用の返品期待値は導入前に確認する。
- 分類・明細は各範囲の参考計算。`half_up` の個人配分の最終端数は四捨五入し、既存 `floor` の参考集計は互換性を維持する。
- 移行SQLはテスト用コピーへ一度だけ適用する。アプリからの自動移行は行わない。

## 根拠

- [SQLite DDL](schema/monetail_schema.sql)
- [Receipt.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)
- [Shop.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Master/Shop.vb)
- [DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)
- [RegisterForm.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)
