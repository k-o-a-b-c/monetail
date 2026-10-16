# 03 金額・税・按分・残高の計算仕様（現行コードの挙動）

更新日: 2026-10-15 / ソースからの静的解析。動作テストは未実施。

## 1. 明細の基礎金額

`ReceiptDetail` の単価 `fee`、数量 `quantity`、単価値引 `discount_fee`、率値引 `discount_rate`（%）を使用。

```text
基礎金額 = (fee - discount_fee) × quantity
値引額(率) = Ceiling(基礎金額 × discount_rate ÷ 100)
税計算前 = 基礎金額 - 値引額(率)
```

率値引は `Math.Ceiling` を使用。負数・小数率を含めた挙動は現行コードを正としてテストで固定する必要がある。

## 2. 税の扱い

- `Receipt.TaxType=0`: 税込。上記「税計算前」をそのまま集計。
- `Receipt.TaxType=1`: 税抜。`TaxTarget=True` の金額にだけ `(1 + 税率/100)` を掛ける。
- 税率はレシート**グループ**の `TaxRate.Rate`。分類／グループで異なる税率を指定できる。
- `Receipt.Total` は各グループの課税分／非課税分を **税率別に合算**し、税率ごとに `Math.Floor` した額を総和する。
- 明細表示 `DetailCalculatedFee` は**明細ごとに** `Math.Floor`。グループ表示 `GroupCalculatedFee` は**グループごとに** `Math.Floor`。そのため「明細表示を単純合計した値」が保存合計と一致するとは限らない。

例：税抜・10%、課税明細19円×2件、値引0の場合。
- レシート合計: `Floor((19+19)×1.10)=41円`
- 明細の個別表示: `Floor(19×1.10)×2=40円`
これは**計算粒度による差**。意図した仕様か確認が必要。

## 3. 収入・支出

`Receipt.Incomes` は税計算前の明細金額が**負**のものだけを収集し、通常は負数で返す。`Receipt.Expenses` は**正**のものだけを収集する。各々で税率別・グループ別の集約処理を行う。

- `Receipt.Total` は同一レシート内の正負を相殺した合計。
- 月次収支画面はレシートごとに `Incomes` と `Expenses` を集計して表示。
- 収入だけ、支出だけを対象にした場合の端数差は、各メソッド単位でテストする。

## 4. 人別分類集計

`Receipt.GetGroupStatisticsList(users, IsAnd, IsOnlyPerson)` が基礎となる。

- `users` が空の通常条件では対象者を指定せず集計。ただし `IsAnd=True` かつ選択者0の場合は、対象者の設定がない明細だけを採用する。
- `IsAnd=True` かつ選択者あり: 指定者集合と明細対象者集合が**完全一致**した明細のみ。
- `IsAnd=False` かつ選択者あり: 指定した人物のいずれかが含まれる明細。
- `IsOnlyPerson=True`: 対象者数で明細金額を等分して分類集計に算入する。税・丸めの順序にも影響する。
- 明細に対象者が0人の場合、`IsOnlyPerson=True` では `personNumber=0` となり、その明細の額は加算されない。

**未確定**: 購入者（支払者）と費用帰属者のどちらを優先すべきか、割り切れない額の端数を誰が負担するか。オンライン版の移植前に確定する。

## 5. 残高の推算

`Calculation.AutoBalance` および `DataAccess.GetAutoBalance` は、対象口座の指定日前の最新残高を起点に、対象期間の取引を反映する。

```text
推算残高 = 直前の登録残高
         - その口座から支払ったレシート Total の合計
         - その口座から出した振替金額
         + その口座へ入った振替金額
```

- 基準残高がない場合は0から開始。
- 基準残高も取引もない場合は `Nothing`（不明）を返す。
- 直前残高の検索条件は `date < 対象日`。時刻境界に注意。
- 残高の `valuation_amount` は実残高と別の評価額（任意）。月次画面では評価額表示切替に使用する。
- 月次残高の推算値には背景色と「自動計算による予測残高」の注記を付ける。

## 6. USD換算

年度ごと `t_exchange_rates` から `USD/JPY` の月末に近い `middle_rate` を1件選ぶ。残高集計値を `JPY金額 / rate` として、小数第2位で `MidpointRounding.AwayFromZero` 丸めする。月のレートがなければ換算値を空欄にする。

## 7. 未確定・リスク

1. 合計・グループ・明細の丸め位置が異なり、画面間の合計が一致しないケース。
2. `Double` での率計算と小数率DB読込（`GetInt32`）に関する再現性。
3. 負額、割引率100%超、単価値引が単価を上回るケース。
4. 2019-10-01 を境とするデフォルト税率選択と将来の税率改定への対応。
5. 同一時点の残高・取引境界の扱い、および年跨ぎ推算。
6. `Int32` の金額上限と為替換算時の精度。

## 計算互換性の追加資料

レシート・分類グループ・明細で異なる税端数粒度、**正負金額を同じ税率で計上したときの合計差**、対象者按分と将来の言語移植で固定したい処理順序を、[12-calculation-contract.md](12-calculation-contract.md) に詳述した。**新しい店別丸め方式とは区別して扱う。**

## 改修予定の仕様案（現行とは別）

店舗別の端数処理（切り捨て／四捨五入、丸め対象単位）と、整数の分子・分母による3/103割引を新たな改修要件として整理した。**どちらも現行コードには未実装**。詳しくは [08-requested-enhancements.md](08-requested-enhancements.md)。

## 根拠

[Receipt.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)、[Calculation.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Logic/Calculation.vb)、[DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、[DataAccessList.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccessList.vb)、[RegisterForm.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)
