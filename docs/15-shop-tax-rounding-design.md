# 15 レシート単位の税端数処理 — 詳細設計（レガシー版）

- 作成: 2026-10-17
- 更新: 店舗マスタ編集を見送り、店舗ごとの直近レシートから初期選択する方針へ変更
- 状態: **実装済み。自動テストの実行方法は [tests/README.md](../tests/README.md) を参照。Windows画面・実データでの動作確認は別途必要**
- 対象: WinForms / VB.NET / SQLite
- 関連: `LEG-019`・[08 改修要件](08-requested-enhancements.md)・[14 決定記録](14-decisions-2026-10-16.md)

## 1. 確定した設計原則

1. **端数処理は各レシートの属性**。`t_receipts.tax_rounding_mode` を計算時の唯一の参照元（正本）とする。
2. **初期値は店舗ごとの直近レシートから取得**する。支払い方法の入力補助と同様に、店名と現在の利用者で履歴を検索する。**店舗登録は必須ではない**。店舗マスタの値は初期選択に使わない。
3. マスタ未登録／過去伝票由来の候補／自由入力の店舗でも、**同じ履歴検索と直接選択が可能**。履歴がない場合の初期値は`floor`。
4. **既存レシートは従来の`floor`を維持**。後から別のレシートが保存されても、過去分・編集中の伝票を自動で変えない。
5. 消費税は**税率ごとに合算してから端数処理**。分類・商品ごとの丸めの合計を会計上の正本にしない。
6. **オーケーの3/103割引は現行の`discount_fee`への手入力を継続**。割引の専用入力欄／分数自動計算／追加DB列は作らない。
7. 個人別金額の配分は概算でよく端数は四捨五入。個人配分の合計で会計総額を上書きしない。

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

`RegisterForm`の`ShopNameComboBox`付近に`TaxRoundingModeComboBox`を設ける（表示名「税端数：切り捨て／四捨五入」）。

**すべてのレシートで編集可能**。店舗がマスタに登録されているかは関係ない。

| 操作 | レシートの初期／保存モード |
| --- | --- |
| 新規レシートで店舗候補を選ぶ | 同じ店名・現在の利用者の直近レシートから方式をコピー |
| 履歴候補 `Shop.Code=0`／自由入力の店名を確定 | 登録済み店舗と同じ履歴検索。店舗登録は不要 |
| 履歴のない店舗 | `floor`。レシートの選択欄から変更可能 |
| 店舗名を変更した新規レシート | ユーザーが丸め方式を手動変更済みなら保持。未変更なら変更先の履歴から初期選択 |
| 保存済みレシートを編集／店名を変更 | 保存されたレシートの方式を保持。履歴で上書きしない |
| 保存済みレシートで方式を変更して再保存 | そのレシートだけ変更する。以後の新規入力では最新の履歴が候補になる |
| 保存済みレシートから流用登録 | 新規レシートとして、画面上の店舗名の直近の方式を初期選択。保存前に変更可能 |
| 編集をキャンセル | 元のレシートの方式を復元 |

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
- 税端数コンボボックス変更時: `Receipt.TaxRoundingMode` を更新して合計を再計算し、手動選択を記憶する。
- 保存時: レシートが保持する方式と合計を同じトランザクションで保存する。保存直前に履歴を再取得しない。

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
tax = RoundTax(T_r * r / 100, Receipt.TaxRoundingMode)
tax_rate_subtotal = T_r + tax + N_r
Receipt.Total = sum(tax_rate_subtotal for all r)
```

正の税額の`floor`は小数点以下切り捨て、`half_up`は`0.5以上を切り上げ`。**税率が同じ複数グループを必ず合算**してから税額を丸める。異なる税率を混ぜてから丸めない。

- `TaxType=0`（税込入力）は税額を追加しないため、丸めモードを保存しても`Receipt.Total`を変更しない。
- **現行の`floor`に対して結果の互換性を保持**する。現行の`Math.Floor(T_r*(1+r/100)+N_r)`と正の整数円について一致するようにテストする。
- 新規計算ヘルパーは、可能なら整数の税率％と`Decimal`で作り二進小数の誤差を抑える。負額については次の未決定事項に注意。

### 4.3 返金・負数の例外

現行コードの`Math.Floor(-1.1) = -2`は、ゼロ方向へ切り捨てる計算とは異なる。

- **既存の`floor`レシート**は旧`Math.Floor`と同じ意味を守ること。
- 新`half_up`のマイナス税額について、`MidpointRounding.AwayFromZero`は**提案**に留める。負の明細だけのレシート、正負混在のレシートを実際の期待値と照合して採用する。
- `Receipt.Incomes`／`Expenses`は正負を分けて集計する現行挙動を維持する。同じ丸めヘルパーを適用するが、`Total = Incomes + Expenses`を一律保証しない。
- `Calculation.GroupCalculatedFee`／`DetailCalculatedFee`と`Receipt.GetGroupStatisticsList`は現在はグループ／明細単位で丸めている。改修対象・表示時の端数差は個別にテストし、**会計のレシート合計は税率別合算値を正本**とする。

## 5. 修正ファイル・メソッド

| ソース | 修正要件 |
| --- | --- |
| `Data/Master/Shop.vb` | 既存設計の店舗列の読込互換性を保持。入力補助では使用しない |
| `Data/Data/Receipt.vb` | `TaxRoundingMode`（レシート確定値）を追加。`Total`、`Incomes`、`Expenses`を検証・丸め処理を共通化 |
| `Logic/Calculation.vb` | 税端数の共通関数を用意。グループ／明細表示との関係をテスト |
| `DataAccess/DataAccess.vb` `ReadShop` | マスタの税端数列をSELECTして`Shop`へ復元。履歴候補`Code=0`のモードは`floor` |
| `DataAccess/DataAccess.vb` `GetReceipts`／`GetPaymentReceipts` | **両方**のSELECTで`t_receipts.tax_rounding_mode`を取得。残高推算などの読み込み漏れを防ぐ |
| `DataAccess/DataAccess.vb` `RegisterReceipt`／`UpdateReceipt` | 同じトランザクション内で`Receipt.TaxRoundingMode`と`Receipt.Total`を保存 |
| `Control/RegisterForm.vb`／`RegisterForm.Designer.vb` | 選択欄、初期値設定、変更時再計算、既存編集での保持、流用登録を対応 |
| `DataAccess/DataAccess.vb` `GetTaxRoundingMode` | 店舗別・現在の利用者の直近レシートの方式を取得。履歴なしは `floor` |
| `MasterMaintenanceForm` | 新たな編集機能を追加しない |
| 一覧・分類集計・残高集計 | `Receipt.Total`や`Receipt.Incomes/Expenses`を経由して再計算。変更の影響を回帰テスト |

**変更しない**: `t_receipts`に`shop_code`は作らない。`m_shop`とのFKを追加しない。オーケー向け専用の割引データも作らない。

## 6. マイグレーション・互換性と運用

1. 本番SQLiteから一貫性のあるバックアップを作成し、**テスト用のコピー**で移行SQLを実行する。
2. 元の全伝票の`total`や対象者割当は**更新しない**。移行後のレシート方式は一律`floor`となる。
3. `PRAGMA table_info('m_shop')`／`('t_receipts')`と計算値を照合する。
4. 新しいアプリがレシートごとの端数方式を保存・読込できることを確認する。
5. **旧アプリと新アプリのDB共有は行わない**。旧アプリで`half_up`の伝票を編集すると古い`Receipt.Total`が保存合計を上書きし得る。
6. 途中で問題があれば移行前バックアップから**ファイルを復元**する（安易な`DROP COLUMN`をロールバックに使わない）。
7. 現行の親子削除順とFK設定のリスクは、本機能と独立したコードの安全性課題として扱う。

## 7. 受け入れテスト

計算用の期待値: [tests/tax-rounding-cases.json](tests/tax-rounding-cases.json)。JSONは**仕様上の期待値**。`tests/CalculationTests` が実際のVBコードへ入力して検証する。

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
| RT-010 | 店舗変更の操作中に手動選択したモードが不用意に上書きされない |
| RT-011 | マイナス・返品の税額丸めは運用期待値を確定させてから合否判定 |
| RT-012 | 新規保存／編集／流用でモードが混同されない |

## 8. 実装で採用した操作・計算

- 店舗マスタ編集画面の作成は見送り、直近の支払い方法と同じ使い方で端数方式を初期選択する。
- 店舗マスタに新たな登録・変更操作を求めない。既存移行SQLは互換性のため2列追加のまま維持し、店舗側の列は初期選択に利用しない。
- 流用は新規入力へ切り替え、店舗の直近方式を初期選択する旨を画面に表示する。内容・方式を確認して登録ボタンで保存する。
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
