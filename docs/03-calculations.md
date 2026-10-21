# 03 金額・税・按分・残高の計算仕様（現行コードの挙動）

更新日: 2026-10-18 / 税端数処理の実装と自動回帰テストを反映。計算・保存テストの範囲は [06-test-cases.md](06-test-cases.md) を参照。実データでの検証と手動E2Eは未実施。

## 1. 明細の基礎金額

`ReceiptDetail` の単価 `fee`、数量 `quantity`、単価値引 `discount_fee`、率値引 `discount_rate`（%）を使用。

```text
基礎金額 = (fee - discount_fee) × quantity
値引額(率) = Ceiling(基礎金額 × discount_rate ÷ 100)
税計算前 = 基礎金額 - 値引額(率)
```

欄を離れたときと明細追加・更新時に、不正な数量は1、値引率は不正なら0・100%超なら100に自動補正する（小数可）。既存データの計算式・読込は変更せず、過去の範囲外値も自動修正しない。[23 入力検証](23-detail-input-validation.md)参照。

単価・値引額の両欄では整数の`+`・`-`・`*`を評価し、乗算を優先する。不正な式と途中のInt32範囲超過は0へ補正し、明細へは評価した整数を反映する。式の評価後の金額計算式は変更しない。画面の明細反映・登録前には合計／収支・グループ／明細の各金額の整数変換を確認し、範囲超過は警告して中止する（[25 範囲確認](25-receipt-amount-overflow.md)）。[24 整数式](24-integer-money-expressions.md)参照。

率値引は従来どおり `Double` と `Math.Ceiling` を使用する。手入力値引・数量への乗算・小数率の計算を自動テストで確認済み。小数率のDB読込（LEG-005）は共通処理を`GetInt32(7)`から`GetDouble(7)`へ修正した。旧処理は実SQLiteのREAL値2.5%・2.6%で`InvalidCastException`となる。DBの`INTEGER`宣言・保存処理・計算式は変更しない。

読込だけでは保存された率や`total`を更新しない。既に小数率が保存されているレシートはその値で再計算するため、旧読込で例外になっていたレシートも表示・集計できる。新規・更新と一覧・残高・子再読込の回帰範囲は[06 テスト](06-test-cases.md)を参照。

オーケーの3/103会員割引は `discount_fee` への手入力を継続する。専用の自動計算・UI・DB列は追加していない。`discount_fee` は数量を掛ける前に引くため、まとめた会員割引合計を入力する場合は原則数量1の明細を用いる。

## 2. 税の扱い

- `Receipt.TaxType=0`: 税込。税額を追加せず、端数方式を変えてもレシート合計は変わらない。
- `Receipt.TaxType=1`: 税抜。`TaxTarget=True` の金額にだけ税額を加える。
- 税率はレシート**グループ**の `TaxRate.Rate`。分類／グループで異なる税率を指定できる。
- `Receipt.Total` は課税額・非課税額を**レシート内の税率ごとに合算**し、各税率の税込額を `Calculation.TaxRateSubtotal` で計算して総和する。分類が違っても税率が同じなら先に合算する。
- 方式は各レシートの `TaxRoundingMode`（`floor`／`half_up`）で保持し、`t_receipts.tax_rounding_mode` に保存する。移行済みの既存レシートは `floor`。

税率 `r` の課税額合計を `T`、非課税額合計を `N` とした税抜入力の実装式は次のとおり。

```text
floor:   Math.Floor(T * (1 + r / 100) + N)       ← 従来のDouble式を保持
half_up: T + Round(Decimal(T) * r / 100, 0, AwayFromZero) + N
```

`floor` は負額・二進浮動小数点の境界も含め既存結果を維持する。単に税額だけをDecimalで切り捨てる式へ置き換えない。`half_up` は税額をDecimalで計算し、ちょうど半分の負額もゼロから遠い側へ丸める（税額−0.5円→−1円）。

明細表示 `DetailCalculatedFee` は**明細ごと**、グループ表示 `GroupCalculatedFee` と分類集計は**グループごと**に当該レシートの方式を適用する参考額。その合計で会計上の `Receipt.Total` を上書きしない。

例：税抜・10%、課税明細19円×2件、値引0の場合。

| 表示・設定 | 金額 |
| --- | ---: |
| レシート合計・`floor` | `Floor((19+19)×1.10)=41円` |
| 明細の個別表示の合計・`floor` | `Floor(19×1.10)×2=40円` |
| レシート合計・`half_up` | `38+Round(3.8)=42円` |

この粒度の差は回帰テストで固定している。`half_up` でも15円を別の10%グループに分けると、レシート合計33円に対しグループ表示の合計は34円になる。

## 3. 収入・支出

`Receipt.Incomes` は税計算前の明細金額が**負**のものだけを収集し、通常は負数で返す。`Receipt.Expenses` は**正**のものだけを収集する。`Total` と共通の処理で税率ごとに合算し、レシートの方式で端数処理する。

- `Receipt.Total` は同一レシート内の正負を相殺した合計。
- 月次収支画面はレシートごとに `Incomes` と `Expenses` を集計して表示。
- 正負を分離すると丸めの対象額が変わるため、税抜では `Total = Incomes + Expenses` を保証しない。両方式の正負混在を自動テストで確認している。

## 4. 人別分類集計

`Receipt.GetGroupStatisticsList(users, IsAnd, IsOnlyPerson)` が基礎となる。

- `users` が空の通常条件では対象者を指定せず集計。ただし `IsAnd=True` かつ選択者0の場合は、対象者の設定がない明細だけを採用する。
- `IsAnd=True` かつ選択者あり: 指定者集合と明細対象者集合が**完全一致**した明細のみ。
- `IsAnd=False` かつ選択者あり: 指定した人物のいずれかが含まれる明細。
- `IsOnlyPerson=True`: 対象者数で明細金額を等分して分類集計に算入し、グループごとに税計算する。
- 明細に対象者が0人の場合、`IsOnlyPerson=True` では0除算せず、その明細の額を加算しない。
- 個人配分は参考額。`half_up` では最終端数を `AwayFromZero` で四捨五入する。既存 `floor` の分類・個人別参考額は従来の切り捨てを維持する。

全16選択集合の完全一致／部分一致・按分あり／なし、正負・税率・グループ境界の回帰を追加した。複数人を選択しても各明細から1人分だけを算入し、同じ利用者コードでも別インスタンスは一致しない現行挙動を記録する。[22 検証範囲と残る判断](22-person-group-statistics.md)参照。

個人配分の合計が会計総額と一致しなくても、レシート合計を変更しない。購入者（支払者）と費用帰属者の優先順位や、差額を特定の人へ配賦する厳密な端数配分は未実装。

分類集計・収支集計の月額／年額は既存のInt32計算を維持する。加算・整数変換で範囲外なら集計表全体を「集計不可／範囲超過」へ置き換え、条件変更で再計算する。[26 集計の金額範囲](26-statistics-amount-overflow.md)参照。

## 5. 残高の推算

`Calculation.AutoBalance` および `DataAccess.GetAutoBalance` は、対象口座の指定日前の最新残高を起点に、対象期間の取引を反映する。期間判定と加減算は`Calculation`の内部オーバーロードに共通化し、各入口が使うDataAccessを明示的に渡す。

```text
推算残高 = 直前の登録残高
         - その口座から支払ったレシート Total の合計
         - その口座から出した振替金額
         + その口座へ入った振替金額
```

- 基準残高がない場合は0から開始。
- 基準残高も取引もない場合は `Nothing`（不明）を返す。
- 登録残高はその日の日末の値。起点は`date < 対象日00:00:00`の最新残高とし、**残高日の翌日00:00:00以上〜対象日の翌日00:00:00未満**を集計する。保存時刻は推算の開始点に使わず、対象日の小数秒も含める。最終日9999/12/31の上限はMaxValueを含む。[21 仕様](21-end-of-day-balance.md)参照。
- 残高用の `GetPaymentReceipts` でも保存済みの端数方式を読む。`half_up` と `floor` のレシートを残高推算へ反映する自動テストがある。
- 残高の `valuation_amount` は実残高と別の評価額（任意）。月次画面では評価額表示切替に使用する。
- 月次残高の推算値には背景色と「自動計算による予測残高」の注記を付ける。

残高推算の両APIは既存のInt32計算と途中の範囲超過例外を維持する。登録・支払メモでは表示ヘルパーで「範囲超過」に変換し、残高集計では表全体をエラー表示にしてグラフを消す。0円・データなしと区別する。[27 金額範囲](27-balance-amount-overflow.md)参照。

## 6. USD換算

年度ごと `t_exchange_rates` から `USD/JPY` の月末に近い `middle_rate` を1件選ぶ。残高集計値を `JPY金額 / rate` として、小数第2位で `MidpointRounding.AwayFromZero` 丸めする。月のレートがなければ換算値を空欄にする。為替の回帰テストは未整備。

## 7. 未確定・リスク

1. 合計・グループ・明細の丸め粒度の差は維持している。画面の参考額と会計総額を混同しない。
2. `Double`での率計算の浮動小数点境界。小数率DB読込の例外（LEG-005）は修正したが、あらゆる率・金額の精度を保証するものではない。
3. 割引率100%超、単価値引が単価を上回るケースの業務上の許容範囲。負額の税端数計算自体は両方式で回帰テスト済み。
4. 2019-10-01 を境とするデフォルト税率選択と将来の税率改定への対応。
5. 同一日時の複数残高の優先順位と、別形式の日時の扱い。日末残高に基づく境界修正と回帰範囲は[21 仕様](21-end-of-day-balance.md)を参照。
6. `Int32` の金額上限と為替換算時の精度。

## 入力画面と保存値

内税／外税と端数方式の編集中の値は画面のラジオボタンに保持する。合計プレビューは `Receipt.GetTotal(taxType, taxRoundingMode)` に選択値を渡し、`Receipt` の両設定・`Total`・`Incomes`・`Expenses` を変更しない。登録確認後に店名などと同じタイミングで転記するため、キャンセル時の設定復元処理は不要。編集用Receiptのディープコピーは導入していない。明細などの既存編集処理まで非破壊に変えたものではない。

新規入力の端数方式は同じ店名・現在の利用者の直近レシート（日付降順、同日時はseq降順）から初期選択し、履歴なしは `floor`。店舗マスタの列は移行互換性のため残すが初期選択には使用せず、マスタ編集画面は追加していない。保存済みレシートと手動選択は履歴で上書きしない。流用は画面の設定で確認後に即時新規保存し、追加操作はない。

振替金額は従来どおりInt32の整数入力で、0・負額も許容する。登録・流用で同じ条件を確認し、不正入力はモデルやDBへ反映する前に中止する。レシート単価・値引額の整数式は適用しない。[28 入力契約](28-transfer-input-validation.md)参照。

残高登録は既存の整数範囲と任意評価額を維持し、全行を一時リストで検証してから元モデルへ反映する。入力エラーでは日付・タグ・明細も変更しない。[29 入力契約](29-balance-input-state.md)参照。

## 計算互換性の追加資料

レシート・分類グループ・明細で異なる税端数粒度、正負金額の合計差、対象者按分と移植時の処理順序は [12-calculation-contract.md](12-calculation-contract.md)、実装済み税端数処理の詳細は [15-shop-tax-rounding-design.md](15-shop-tax-rounding-design.md) を参照。

## 根拠

[Receipt.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)、[Calculation.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Logic/Calculation.vb)、[DataAccess.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、[DataAccessList.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccessList.vb)、[RegisterForm.vb](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)、[自動テスト](../tests/README.md)

レシートの登録・流用は必須入力も共通確認し、不備があれば確認・モデル転記前に中止する。税率別合算・端数処理・明細値引の式は変更しない。[30 入力契約](30-receipt-input-validation.md)。

集計詳細の明細別／グループ別でも、既存の整数変換が範囲外なら表内に範囲超過を表示する。相殺・税・端数・値引きの式を変更せず、表示を切り替えて再計算する。[31 表示契約](31-detail-view-amount-overflow.md)。
