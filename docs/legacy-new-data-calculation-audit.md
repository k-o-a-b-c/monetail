# 旧版／新版の計算・DBアクセス・移行境界照合

[← 機能・画面比較](legacy-new-comparison.md) | [旧版計算契約](12-calculation-contract.md) | [新版レシート計算／API](ledger-receipt-api.md) | [旧DB](02-database.md) | [新版レシートDB](ledger-receipt-schema.md)

## 照合の範囲と判定方法

2026-11-12時点の`main`を対象に、旧VB.NETの`Receipt`／`ReceiptDetail`／`Calculation`／`DataAccess`と、新C#の`ReceiptCalculator`／`ReceiptValidation`／`ReceiptManagement`、両DB定義・既存自動テストを**静的照合**した。ここで「対応」とするのは**ソース上のアルゴリズムと固定例が整合する**意味であり、**旧・新実行バイナリに同一データを与えた差分テストを実施した意味ではない**。金額の互換性と移行可否は区別する。

- **対応**：同じ業務値を表現できる範囲の計算・トランザクションの契約がソース上で一致。
- **承認済み変更**：旧データの表現と新入力制約が異なるが、新版正本で明示済み。
- **要対応**：新旧の値／制約の同時保持が現状不可能、または移行前に方法を決める必要がある。
- **後続**：新版の振替・残高・支払メモ・人物別集計等が未実装／最終仕様未確定で、数値の一致をまだ判定できない。

## 1. 金額計算の処理順と固定例

| 項目 | 旧VB.NET | 新C# | 照合結果 |
| --- | --- | --- | --- |
| 単価・単価値引・数量・率値引 | `(fee-discount_fee)*quantity`を先に計算し、`Ceiling(基礎額*率/100)`を引く。Decimal | `ReceiptCalculator`が同じ順序・Decimalを使用 | **対応**。正負・小数率の既存固定例あり |
| 税抜`floor` | 同率課税額を**レシート全体**で合算して`Floor(T*(1+r/100)+N)` | 同じ率の明細を科目をまたいで合算し`decimal.Floor` | **対応**。旧`TaxTarget=False`は新版`tax_rate=null`へ意味を変換 |
| 税抜`half_up` | 合算後の税額を`AwayFromZero`で丸めて加算。負の税も同じ規則 | `decimal.Round(..., AwayFromZero)` | **対応** |
| 税込 | 明細の値引後額を合算し、外税を加算しない | 同じ。内部の課税／非課税バケットは別でも会計合計は同額 | **対応** |
| レシート合計と参考額 | 会計合計はレシート全体・税率単位、グループ／明細参考額は各々の単位で丸める | 会計合計は全体、参考額はグループ内の税率単位・明細単位 | **従来の丸め境界を維持**。ただし新版は同科目に複数税率可 |
| 符号別収支 | 各**値引後明細**の正負で収入・支出を別集計・別丸め。会計合計と和が不一致になり得る | `Net < 0`と`Net > 0`で独立集計 | **対応**。科目のIncome/Expense区分だけでは符号を変更しない |
| 0人の人物・按分 | 0人の明細は全体会計合計に含む。人物按分時は0除算せず**算入しない** | 明細対象人物0人を保存可能。人物按分計算はまだ未実装 | **会計額は対応、人物別集計は後続** |
| 収支・参考額の数値上限 | 旧`Int32`最終変換、各画面の範囲超過通知。計算途中にDecimalを用いる | 新版は合計・符号別・グループ・明細の**それぞれの最終額**をInt32で検証。Decimal途中超過も拒否 | **近い契約だが境界の完全一致は未検証**。負額の相殺や個別参考額超過の両実装比較が必要 |
| 期間別の税率適合 | グループが保存しているマスタ税率を計算に使用。旧レシート再保存に新版と同じ期間履歴検証はない | 全明細の実税率について取引日の履歴候補を**更新時も**検証 | **承認済み変更**。旧税率を単純に持ち込んだレシートが新版で更新不可になる可能性 |

**両ソースから確認できる計算例（新C#では既存の`ReceiptCalculationTests`にも期待値あり）**

| 入力（税抜、課税10%） | 会計合計 | 参考額や収支 | 照合目的 |
| --- | ---: | --- | --- |
| 19円の明細2件を**異なる科目グループ**へ、`floor` | 41円 | 各明細・各グループの合計40円 | 同率・グループ横断の端数 |
| 15円の明細を2グループへ、`half_up` | 33円 | グループ参考額合計34円 | グループ間で参考額を合計しない |
| 単価1,503円、単価値引3円、数量2、率値引1.1%、`floor` | 3,263円 | 税前2,967円。half_upなら3,264円 | 単価値引→数量→Ceiling |
| 明細-5円、`floor`または`half_up` | -6円 | 収入-6円 | 負税額の丸め |
| 明細5円と-1円、`floor` | 4円 | 収入-2円・支出5円（和3円） | 収支の独立した丸め |
| 同じ5円と-1円、`half_up` | 4円 | 収入-1円・支出6円（和5円） | 正負混在と端数方式 |

**注意**：上表の`tax_rate=null`（非課税）と`tax_rate="0"`（課税0%）は新版で**別の税区分**として格納するが、税額だけでは区別できない。旧`TaxTarget=False`のレシートに税率0を設定していたかどうかも、旧`tax`コードとマスタを紐付けて検証すること。

**人物按分は未照合のまま残す**：旧`Receipt.GetGroupStatisticsList`は、明細のDecimalを`Double`へ変換して人数で割り、グループの`Double`へ加算してからDecimalへ戻す。新版DEC-157は明細別按分後にグループ内の税率別集計まで確定したが、**負額・小数配分の最終端数**は未決定。旧版から安易に`Decimal`の人数割りへ置き換えて「完全互換」と呼ばない。

**参照ソース**：[旧レシート](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)／[旧計算](../HouseholdAccountsBeta/HouseholdAccountsBeta/Logic/Calculation.vb)／[新計算](../src/Monetail.Api/Features/Monetail/Receipts/ReceiptCalculator.cs)／[新保存検証](../src/Monetail.Api/Features/Monetail/Receipts/ReceiptValidation.cs)／[新計算テスト](../tests/Monetail.Api.Tests/ReceiptCalculationTests.cs)。

## 2. DBアクセス・保存／読み取りの境界

| 対象 | 旧Windows版で確認 | 新版で確認 | 判定 |
| --- | --- | --- | --- |
| レシート新規ID | SQLiteトランザクション内で`MAX(seq)+1`。子の`seq`も並び順から振り直す | サーバー発行UUID v7。家計簿内の複合PK/FK | **承認済み変更**。移行時は旧→新IDの対応が必須 |
| 親子の作成・更新・削除 | 単一トランザクション。**更新時は子全削除→全再挿入**。削除は対象者→明細→グループ→親 | 単一DBトランザクション。更新は既存`groupId/detailId`を維持する差分更新。削除は対象者→明細→グループ→親 | **原子性は対応、子IDの維持方法は変更** |
| 競合・再送 | 親に`revision_token`、UPDATE/DELETEで照合。重複要求IDによる成功再送の契約はない | 親の`revision`、`requestId`記録・同一要求の再送、確定前の参加／権限・マスタ再照合 | **承認済み強化** |
| レシート一覧読取 | 親と子をLEFT JOINの**1回の階層SELECT**で読込み、未設定子があっても親を保持。合計はモデルで再計算 | 一覧は親7フィールドの概要を最大100件カーソル取得。詳細は別APIで親→グループ→明細→人物を取得 | **承認済み変更**。旧版の明細人数0警告・再計算差異を親一覧だけで復元できない |
| 保存済み合計 | `t_receipts.total`はNULL可。表示・集計は再計算額、保存値は比較用に保持 | `total_amount`はNOT NULLのInt32。**詳細GETに保存時合計なし** | **移行上の要対応**。NULL保存額を推測で0や再計算額に置換しない |
| 旧マスタの参照 | `t_receipt_groups.tax`は税率マスタの**コード**で、DBの税率マスタFKはない | 新`tax_rate`は**明細の実数値**、保存時に期間履歴と照合 | **値の変換が必須**。コード`8`を無条件に8%と読まない |
| NULL文字列 | 旧DataAccessは所在地とタグのNULLを**読取時のみ空文字に変換**、元DB値は変えない | 新版は所在地・各メモのNULLを意味ある保存値として扱い、未編集時に元値を保持するUI契約 | **承認済み差異**。移行SQLでNULL/空文字を勝手に同一化しない |
| 人物・所有者 | 旧`m_user.account_name`とWindows利用者名を一部の参照・表示に使う | 認証アカウントと家計簿の**人物ID対応**・支払方法所有者を検証 | **移行時に明示的対応表**が必要。名前の自動照合は禁止 |
| 振替・残高・支払メモ | 旧版にSQLとトランザクションあり。残高は指定日終了時点、支払メモは全行の一括UPSERT | 新版で対応する業務テーブル/API/集計経路は後続 | **後続**。旧データの変換可否と計算互換はまだ断定不可 |

**参照ソース**：[旧SQL](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)／[新版レシート保存](../src/Monetail.Api/Features/Monetail/Ledgers/ReceiptManagement.cs)／[旧DDLと危険箇所](02-database.md)／[新版DB](ledger-receipt-schema.md)／[新版API](ledger-receipt-api.md)。

## 3. 新たに見つかった移行時の重要差異と事前検査

これらは**既存の承認済み仕様を否定せず、実データ投入前の矛盾・境界を明確にする**もの。まだデータ移行SQLは作らない。

| 優先 | 差異／影響 | 旧版側の根拠 | 新版側の根拠 | 必要な判断・検証 |
| --- | --- | --- | --- | --- |
| **高** | **保存時合計NULLを無損失でそのまま新`total_amount`へ格納できない**。新カラムはNOT NULL、旧はNULL可 | `t_receipts.total`（SQLite NULL可）、`SavedTotal As Long?` | `ledger_receipts.total_amount INTEGER NOT NULL`、Int32 | **旧保存値を補正しない方針と物理制約が衝突**。NULLあり件数・Int32外保存額件数を事前集計し、別格納・履歴保持等の扱いを**ユーザー承認のうえ**決める |
| **高** | **空の科目グループ**：旧画面はグループだけ追加可能、レシート全体として明細が1件あれば保存を許すため、空グループが残る可能性 | `RegisterForm.AddGroupButton_Click`でグループ作成、`HasRequiredReceiptInput`は`Any(Details.Count>0)` | `ReceiptValidation.Parse`は**すべてのグループに明細1件以上**を要求 | 旧DB内の空グループ数を調べ、移行時にグループメモ・税率情報を失わない扱いを決める。**実データに存在するとは未確認** |
| **高** | **時刻精度の変更**：旧`date TEXT`と`DateTime`は秒未満を格納し得る。一覧SQLの上限`23:59:59`は秒未満の終端レコードを取りこぼし得る | 旧`GetReceipts(Year,Month)`の`BETWEEN`上限、元DDLのTEXT | 新`transaction_at`は`yyyy-MM-ddTHH:mm:ss`の**秒精度のみ** | 秒未満・日時形式の異種値・同一秒への集約を**旧DBのSQL直接照会**で検査。移行時の丸め・切捨て・保存場所を決め、旧一覧に表示されない行も見落とさない |
| **高** | **グループ税率コード→明細税率**：課税対象Falseは非課税へ、Trueは旧税率**コードをマスタから実率に変換**する必要がある | 旧`t_receipt_groups.tax`、`t_receipt_details.tax_target` | 新`ledger_receipt_details.tax_rate` NULL／Decimalと履歴照合 | 存在しない旧コード・同コードに対する率の揺れ・期間外の率・課税0%と非課税を検査。率が数値的に同じでも税区分の意味を落とさない |
| **高** | **新版での再保存不可**：取引日時・マスタ有効期間・税率履歴が旧値を覆わない場合、メモ変更だけでもAPIが全明細を拒否する | 旧グループ税率と旧マスタの使用 | `ValidateForSave`と全明細の現行候補照合 | 過去期間の履歴整備、読取互換・変更制限の表示方法を移行前に確定。**単なるマスタ追加で保存済み業務値を自動変更しない** |
| **中** | **グループ／明細の識別・順序**：旧複合Seq、更新時に子を作り直す。新版は子IDを保持し、明細`display_order`で表示 | 旧`SetChildSequence`・`DeleteReceiptGroups` | 新`ReceiptManagement`・`display_order` | 旧Seq→新UUIDと旧子順の対応表を保持し、同科目グループの重複・名称変更・空グループを検査 |
| **中** | **人物の割当・支払方法の所有者**：旧人物コードとWindowsアカウント名は新版の認証ユーザーIDではない | `m_user`、`m_payment_classification`、`t_receipt_detail_users` | 新家計簿・人物・参加者・所有者複合FKと権限 | 旧人物／支払方法／家計簿の明示マッピング。NULL所有者や参照欠損があれば手動確認 |
| **中** | **旧DBに制約が弱い参照**：旧グループ税コードに税率FKなし、旧ポイントもレシートFKなし | 旧DDLと実コード | 新レシートは強い複合FK・CHECK。ポイントは後続 | FK検査に加え**アプリ上の論理参照**（旧税コードとポイント）を別集計。孤児・不正コードを黙って除去しない |
| **中** | **割引率の保存型**：旧SQLiteはINTEGER宣言でも実データがREALとなり得る。新版はDecimal文字列TEXT | 旧`GetDecimal`・DDL | 新`discount_rate TEXT`・InvariantCulture検証 | 旧REALの小数率をどの精度でDecimal文字列へ写すか決め、再計算・保存済み額の差を固定例で確認 |

**直接チェックしたい旧DBの最小SQL例（実データには未実行）**：

```sql
-- 保存合計のNULL・新版Int32外の存在確認
SELECT COUNT(*) AS receipts,
       SUM(total IS NULL) AS null_totals,
       SUM(total IS NOT NULL AND (total < -2147483648 OR total > 2147483647)) AS out_of_int32
FROM t_receipts;

-- 少なくとも1つの空グループ（子明細0件）
SELECT COUNT(*) AS empty_groups
FROM t_receipt_groups g
WHERE NOT EXISTS (
  SELECT 1 FROM t_receipt_details d
  WHERE d.receipt_seq=g.receipt_seq AND d.group_seq=g.seq
);

-- マスタに存在しない税率コード（FKの宣言がない）
SELECT g.tax, COUNT(*) AS groups_count
FROM t_receipt_groups g LEFT JOIN m_tax_rate t ON t.code=g.tax
WHERE t.code IS NULL
GROUP BY g.tax;

-- 税対象の実態。非課税へ移す明細の件数確認
SELECT tax_target, COUNT(*) AS details_count
FROM t_receipt_details
GROUP BY tax_target;
```

日付の異種形式と小数秒の検出はSQLiteの`typeof(date)`・`length(date)`・`substr(date,...)`等を**実際の保存例に合わせて**設計する。旧`System.Data.SQLite`の日付保存表現を、根拠なく全件ISO文字列と仮定しない。

## 4. 次の検証順序・未完了判定

1. **旧DB実値の棚卸し**：上記SQL、日時の異種形式・小数秒、実率マスタと旧税コード、同科目の重複、0人明細、未知の科目／支払方法／人物、NULLメモ、割引率REALを集計。**実運用DBに未アクセス**なので、件数と発生率を推測しない。
2. **計算の対照テスト**：同一合成レシートを旧`Receipt.Total/Incomes/Expenses/GroupCalculatedFee/DetailCalculatedFee`と新`ReceiptCalculator`で計算し、結果・例外／上限を比較。特に正負、税率0／非課税、税端数2種、小数率、グループ分割、Int32の両端・途中超過。旧・新版の既存単独回帰テストがあることは、対照実行に代えない。
3. **旧版の人物別集計を固定例にする**：対象者0・1・複数、完全/部分一致、人数按分、負額・半端な割り算、税込/税抜、`floor/half_up`。新版の人物別APIが実装されてから差分を測定する。
4. **振替・残高・為替**：旧日末境界・税後レシート額と振替の加減算、基準残高なし／取引なしの`Nothing`、月末・閏日・最大日・USD中値と欠損を固定例化。新版業務API未実装時点で同等性を宣言しない。
5. **移行試験**：旧DBの**コピー**と新空DBで件数・親子ID対応・NULL・金額・日付・FK・再計算差額・画面表示を照合。**正式リリース前は通常のDB Migrationを導入しない**。一度きりの旧データ移行SQLは別工程、`initialize-db`を旧DB変換に流用しない。

**完了条件**：旧データの値域を確認し、上記の**高優先度の非互換の扱いを確定**し、旧・新の対照テストと移行コピーの差分結果を記録して初めて「計算・DB・移行も照合済み」とする。本書はその前段の静的監査。詳細な正本は[旧計算](12-calculation-contract.md)、[新版レシート](ledger-receipt-api.md)、[移行方針](ledger-core-transition.md)、[検証記録](../tests/README.md)とし、本書からこれらの仕様を変更しない。
