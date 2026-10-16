# 12 計算・集計互換性仕様（移植時の契約）

更新: 2026-10-16 / **現行VBコードの読み取りから作成**。実行テスト・精度検証は未実施。新仕様案は[08](08-requested-enhancements.md)に分離。

## 1. 買い物明細の金額

引数:

- `fee`: 明細の単価（`Integer`）
- `quantity`: 数量（`Integer`）
- `discount_fee`: 単価に対する金額値引（`Integer`）
- `discount_rate`: 割引率（VB`Double`、%）
- `tax_target`: 課税対象か否か
- `TaxType`: レシートの税区分（`0`=税込、`1`=税抜）
- `TaxRate.Rate`: グループに設定されている税率（%）

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
3. 税抜なら税率の課税分だけ`×(1 + rate/100)`。非課税分と合算して`Math.Floor`。
4. 税率ごとに切り捨てた結果を合計して`Convert.ToInt32`。

### `Calculation.GroupCalculatedFee`

- **グループ単位**で課税分と非課税分を集計してから、課税分への税額を計算し`Math.Floor`。
- 税率が同じでも異なるグループの値は分離して端数処理する。

### `Calculation.DetailCalculatedFee`

- **明細単位**で`net`を計算し、税抜の課税対象なら税率を掛け`Math.Floor`。
- 税率が同じ明細どうしでも端数は統合しない。

### 再現例（コードからの予測値、未実行）

| ケース | 既存計算 | 値 |
| --- | --- | ---: |
| 税抜10%、課税19円の明細2件を同税率グループに登録 | `Receipt.Total = Floor((19+19)×1.1)` | 41円 |
| 同条件、明細表示額の合計 | `Floor(19×1.1)×2` | 40円 |
| 同条件、**別の同税率グループ**に分割 | 各グループで`Floor(19×1.1)`、合計`40` | グループ表示40円 |
| 同条件のレシート合計（グループ分割後） | 同税率なら`Receipt.Total`は税率ごとに再集計 | 41円 |

**重要**: これは実装された丸め位置の説明。仕様上期待するレシート金額と必ず一致するわけではない。新しい「切捨／四捨五入」の設定時は丸めモード**だけでなく集約単位**を保存する。

## 3. 収入・支出の分離とその差

- `Receipt.Incomes` は、`net < 0`の明細だけを税率ごとにまとめて税・丸めを計算。
- `Receipt.Expenses` は、`net > 0`の明細だけを同様に集計。
- 収入は現行の負符号を維持し、支出は正。
- **税抜モードでは、一般に `Total = Incomes + Expenses` を保証できない**。正負別と相殺後で税額の端数を切り捨てる対象金額が異なるため。税込モードは、現行の整数単価・数量と `Ceiling` 後の整数金額を前提にすると通常は一致する。

**例（コードからの予測値）**: 税抜10%、課税対象の+19円と-1円を同一レシートに含める。

```text
Total = Floor((19 - 1) * 1.10) = 19
Expenses = Floor(19 * 1.10) = 20
Incomes = Floor(-1 * 1.10) = -2
Expenses + Incomes = 18  ← Total(19) と1円異なる
```

この差は、単純なコードバグと断定せず、**現行仕様の再現性・今後の業務上の期待値**を分離して判断する。

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

**注意**: 「対象者1人の按分額」を出す目的なら、`IsOnlyPerson=True` だけでなく、適切な利用者選択と`IsAnd`の条件設定が必要。

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
| 税計算 | 税率ごとの集約と`Floor`の位置を変更しない |
| 負額 | `Floor(-1.1) = -2`。0方向への切捨ではない |
| 割引 | `discount_fee`は数量を掛ける前、`discount_rate`は数量を掛けた後 |
| 按分 | 対象人数割りの後にグループ単位で税計算・切捨 |
| 日時 | 登録された`DateTime`と日時境界をUTCへ読み替えてはいけない（既存DBはローカル時刻運用） |
| DB | 物理型とVB読込型の不整合は実DBで検証してから変換 |

## 8. 回帰テストに追加すべき契約

- **CAL-001**: 19円×2件（同グループ・別グループ）: レシート合計と詳細表示差。
- **CAL-002**: 税抜10%、+19円・-1円の同居: `Total=19 / Expenses=20 / Incomes=-2`。
- **CAL-003**: 値引率の境界（0%、小数、100%超）と数量0・複数。
- **CAL-004**: 対象者0人、1人、2人、完全一致／部分一致／人数按分。
- **CAL-005**: 月末残高・同日取引・振替・データなし`Nothing`。
- **CAL-006**: 為替なし月・月末近傍のデータ・負数換算。
- **CAL-007**: オーケーの会員割引は**新規3/103自動計算をしない**。現行`discount_fee`に手入力し、**数量倍される計算**・税率別合算・収支への影響を既存コードの回帰テストで確認する。

参照: [Receipt.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)、[Calculation.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Logic/Calculation.vb)、[DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、[DataAccessList.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccessList.vb)
