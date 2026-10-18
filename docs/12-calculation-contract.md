# 12 計算・集計互換性仕様（移植時の契約）

更新: 2026-10-18 / 現行VBコードと実装済み税端数処理・自動回帰テストに基づく契約。全項目がテスト済みではなく、実施範囲は第8節と [06](06-test-cases.md) を参照。実データでの検証・手動E2Eは未実施。

## 1. 買い物明細の金額

引数:

- `fee`: 明細の単価（`Integer`）
- `quantity`: 数量（`Integer`）
- `discount_fee`: 単価に対する金額値引（`Integer`）
- `discount_rate`: 割引率（VB`Double`、%）
- `tax_target`: 課税対象か否か
- `TaxType`: レシートの税区分（`0`=税込、`1`=税抜）
- `TaxRate.Rate`: グループに設定されている税率（%）
- `TaxRoundingMode`: レシートに保存する端数方式（`floor`／`half_up`）

処理順は次の通り。**通常の%割引に近似した3/103割引ではない**。

```text
x = (fee - discount_fee) * quantity
discount_by_rate = Math.Ceiling(x * discount_rate / 100)
net = x - discount_by_rate
```

算出した`net`を`tax_target`で課税・非課税に分配する。現在のコードは`Double`と`Math.Ceiling`を使い、途中で整数化しない。

## 2. 合計と内訳の丸め粒度の違い

### `Receipt.Total`（保存時の合計と一覧表示用）

1. グループごとの明細`net`を計算。
2. **レシート内の税率`Rate`ごと**に課税対象／非課税対象の金額をそれぞれ合算（分類が違っても税率が同じなら合算）。
3. 税率ごとの課税額を`T`、非課税額を`N`として、`Calculation.TaxRateSubtotal`を適用する。
   - 税込: `Math.Floor(T + N)`。整数円の明細では方式によって変化しない。
   - 税抜・`floor`: 従来のDouble式`Math.Floor(T * (1 + rate / 100) + N)`をそのまま維持する。
   - 税抜・`half_up`: Decimalで税額を計算し、`T + Math.Round(Decimal(T) * rate / 100, 0, AwayFromZero) + N`。
4. 税率ごとの結果を合計して`Convert.ToInt32`。

既存レシートは移行時に`floor`となる。`floor`をDecimalの税額切り捨てへ置き換えると、負額の浮動小数点境界で既存結果が変わる。例えば税抜10%の−100円は旧Double式で−111円となり、この結果を回帰テストで保持している。新しい`half_up`は税額−0.5円を−1円へ丸める`AwayFromZero`を採用済み。

`Receipt.GetTotal(taxType, taxRoundingMode)`は同じ集計処理へ引数を渡すプレビュー用の入口。Receiptのプロパティを変更せず、`Total`・`Incomes`・`Expenses`も変えない。登録画面は内税／外税と端数方式を画面だけに保持し、確認後の保存時に転記する。編集用オブジェクトのディープコピーやキャンセル時の設定復元は使わない。

### `Calculation.GroupCalculatedFee`

- **グループ単位**で課税分と非課税分を集計し、税抜なら課税分へレシートの端数方式を適用して非課税分を加算する。
- 税率が同じでも異なるグループの値は分離して端数処理する。

### `Calculation.DetailCalculatedFee`

- **明細単位**で`net`を計算し、税抜の課税対象ならレシートの端数方式で税込額を計算する。
- 税率が同じ明細どうしでも端数は統合しない。

### 再現例（`floor`の既存結果を維持）

| ケース | 既存計算 | 値 |
| --- | --- | ---: |
| 税抜10%、課税19円の明細2件を同税率グループに登録 | `Receipt.Total = Floor((19+19)×1.1)` | 41円 |
| 同条件、明細表示額の合計 | `Floor(19×1.1)×2` | 40円 |
| 同条件、**別の同税率グループ**に分割 | 各グループで`Floor(19×1.1)`、合計`40` | グループ表示40円 |
| 同条件のレシート合計（グループ分割後） | 同税率なら`Receipt.Total`は税率ごとに再集計 | 41円 |

税率別の会計合計を正本とし、グループ／明細の参考額の合計で上書きしない。この粒度差は自動テストで確認している。`half_up`でも、税抜10%の15円を別々の2グループに入れると`Total=33円`、グループ表示の合計`34円`となる。

保存する設定は`TaxRoundingMode`だけで、集約単位のDB列は追加していない。会計合計の集約単位は「レシート内の税率ごと」で固定する。

## 3. 収入・支出の分離とその差

- `Receipt.Incomes` は、`net < 0`の明細だけを税率ごとにまとめて税・丸めを計算。
- `Receipt.Expenses` は、`net > 0`の明細だけを同様に集計。
- `Total`・`Incomes`・`Expenses`は同じ計算処理を使い、対象明細の符号フィルターだけを変える。端数方式は同じレシートの保存値を参照する。
- 収入は現行の負符号を維持し、支出は正。
- **税抜モードでは、一般に `Total = Incomes + Expenses` を保証できない**。正負別と相殺後で税の端数処理の対象金額が異なるため。税込モードは、現行の整数単価・数量と `Ceiling` 後の整数金額を前提にすると通常は一致する。

**例（従来の`floor`式）**: 税抜10%、課税対象の+19円と-1円を同一レシートに含める。

```text
Total = Floor((19 - 1) * 1.10) = 19
Expenses = Floor(19 * 1.10) = 20
Incomes = Floor(-1 * 1.10) = -2
Expenses + Incomes = 18  ← Total(19) と1円異なる
```

正負を分離する仕様は維持しており、両方式で合計との差を自動テストしている。実行済みケースの税抜10%・+5円と−1円では、`floor`は`Total=4 / Expenses=5 / Incomes=-2`、`half_up`は`Total=4 / Expenses=6 / Incomes=-1`となる。収支列の合計で会計合計を再構築しない。

## 4. 人別分類集計

`GetGroupStatisticsList(users, IsAnd, IsOnlyPerson)` はレシート内の**各グループ単位**で返す。

| 条件 | 採用する明細 |
| --- | --- |
| `users.Count=0` かつ `IsAnd=False` | 全明細 |
| `users.Count=0` かつ `IsAnd=True` | 対象者が0人の明細だけ |
| `users.Count>0` かつ `IsAnd=False` | 指定者集合と共通する対象者が1名以上いる明細 |
| `users.Count>0` かつ `IsAnd=True` | 指定者集合と明細の対象者集合が完全に一致 |
| `IsOnlyPerson=True` | 対象明細の`net`を`detail.Users.Count`で割り、グループごとに税・丸めを実施 |
| `IsOnlyPerson=True` で`detail.Users.Count=0` | 0除算は行わず、その明細の集計額を加算しない |

**注意**: 「対象者1人の按分額」を出す目的なら、`IsOnlyPerson=True`だけでなく、適切な利用者選択と`IsAnd`の条件設定が必要。

分類・個人配分はグループ単位の参考額。`half_up`の個人配分では最終端数を`AwayFromZero`で四捨五入し、税込時も按分後の小数額を四捨五入する。既存`floor`の分類・個人別参考額は従来どおり切り捨てる。個人別合計でレシートの会計総額を変更しない。

## 5. 残高推算の契約

`Calculation.AutoBalance`と`DataAccess.GetAutoBalance`のロジックは近似している。`GetLastBalance(TargetDate.Date, PaymentClassification)`から**対象日より前**の最新残高を取得し、以下を反映する。

```text
balance = （起点となる登録残高。なければ0）
        - 起点以降から対象日末までのレシートTotalの合計
        - その支払方法から出る振替金額
        + その支払方法へ入る振替金額
```

- 前回残高・当該期間のレシート・振替が全くなければ `Nothing`。
- `dateFrom` が前回残高日の23:59:59、`dateTo` が対象日23:59:59に設定される。秒以下の端数・同一日の処理はテストが必要。
- 負のレシート合計（収入）があれば「`- Total`」のため残高は増える。
- 同じ名称の業務計算が複数場所にあり、将来仕様変更時には両方を比較する。

## 6. 為替換算

- 取得: 指定年度の`t_exchange_rates`から`base_currency=USD`、`quote_currency=JPY`を抽出。
- 月の代表レート: その月の末日に**日付が近い行**を選択。必ずしも月末当日のデータではない。
- 換算: `JPY残高 / middle_rate`、小数2桁、`MidpointRounding.AwayFromZero`。
- レートがない月は空欄。為替取得・データ投入の業務プロセスは確認できない。

## 7. 実装言語間で一致させる要素

| 項目 | 避けるべき変更 |
| --- | --- |
| 金額演算 | `Double`→整数・`Decimal`の変更で端数が変わる場合がある。変更する場合は期待値を確認 |
| 税計算 | 税率別合算の単位と保存済み方式を維持。`floor`は旧Double式、`half_up`はDecimal税額の`AwayFromZero` |
| 負額 | `floor`は0方向でなく負の無限大方向。`half_up`は税額の中間値をゼロから遠い側へ丸める |
| 割引 | `discount_fee`は数量を掛ける前、`discount_rate`は数量を掛けた後 |
| 按分 | 人数割りの後にグループ単位で税計算。既存`floor`の切り捨てを保持し、`half_up`の個人最終端数は四捨五入 |
| 日時 | 登録された`DateTime`と日時境界をUTCへ読み替えてはいけない（既存DBはローカル時刻運用） |
| DB | 物理型とVB読込型の不整合は実DBで検証してから変換 |

## 8. 回帰契約と現在の検証範囲

- **CAL-001**: 19円×2件（同グループ・別グループ）: レシート合計と詳細表示差。
- **CAL-002**: 税抜10%、+19円・-1円の同居: `Total=19 / Expenses=20 / Incomes=-2`。
- **CAL-003**: 値引率の境界（0%、小数、100%超）と数量0・複数。
- **CAL-004**: 対象者0人、1人、2人、完全一致／部分一致／人数按分。
- **CAL-005**: 月末残高・同日取引・振替・データなし`Nothing`。
- **CAL-006**: 為替なし月・月末近傍のデータ・負数換算。
- **CAL-007**: オーケーの会員割引は**新規3/103自動計算をしない**。現行`discount_fee`に手入力し、**数量倍される計算**・税率別合算・収支への影響を既存コードの回帰テストで確認する。

上記は計算・移植で維持したい契約の一覧であり、全条件を実行済みという意味ではない。現時点の自動テストは以下を実施している。

- CAL-001の税率別合算とグループ／明細の参考額の差、CAL-002と同じ正負分離の性質（実行入力は+5円・−1円）。
- CAL-003の単価値引・数量2・小数率2.6%の計算。小数率のDB往復、100%超、数量0の網羅は未実施。
- CAL-004の対象者2名からの1名配分、完全一致で不採用、税込／税抜と端数方式の参考配分差。対象者0名など全組合せの網羅は未実施。
- CAL-005のレシート方式を反映した残高推算とデータなし`Nothing`。月末・同日・振替・年跨ぎの網羅は未実施。
- CAL-007の既存手入力値引・数量乗算・正負の回帰。オーケーの専用自動計算は追加していない。
- CAL-006の為替回帰は未整備。

実行方法と全体の自動テスト件数は[tests/README.md](../tests/README.md)および[06](06-test-cases.md)を参照。

参照: [Receipt.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)、[Calculation.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Logic/Calculation.vb)、[DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、[DataAccessList.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccessList.vb)
