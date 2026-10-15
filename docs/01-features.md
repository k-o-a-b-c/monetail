# 01 機能一覧・画面仕様（レガシー版）

更新日: 2026-10-15 / 対象: `main` のソース静的調査。実機動作は未確認。

## ステータスの定義

- **実装確認**: 画面と処理のソースを確認できた（動作保証ではない）。
- **一部実装**: 雛形、途中までのUI、またはコメントアウトが残る。
- **未確認**: ソースから存在・正否を断定できない。

## 画面別

| 画面・機能 | 状態 | ソースで確認できる操作・仕様 |
| --- | --- | --- |
| メイン画面 `ViewForm` | 実装確認 | 年月移動、支払者選択、タブ切替、絞り込み |
| 一覧タブ | 実装確認 | 月別レシート・振替・残高の表示、選択・ダブルクリックで編集 |
| レシート登録 `RegisterForm` | 実装確認 | 日時、店名・店舗、税込／税抜、支払い方法、メモ |
| 分類グループ | 実装確認 | 分類・税率・メモ、グループ追加／更新／統合／削除 |
| 購入明細 | 実装確認 | 品名、単価、数量、値引額、値引率、課税対象、メモ、対象者（複数選択） |
| レシート保存 | 実装確認 | 新規・編集・削除・流用登録。親と子をSQLiteトランザクションで更新 |
| レシート入力補助 | 実装確認 | 過去の店舗名・場所・品名、直近支払方法・分類・税設定を候補化 |
| 振替 `RegisterTransferForm` | 実装確認 | 日時、名称、金額、移動元・先、メモ、新規・編集・削除・流用 |
| 残高 `RegisterBalanceForm` | 実装確認 | 日付別、支払方法別の残高・評価額・メモ、新規・編集・削除 |
| 支払メモ `RegisterPaymentMemoForm` | 実装確認 | 支払方法ごとの確認日・メモを一括保存 |
| 分類集計タブ | 実装確認 | 年度の分類別月次・年計、支払者と明細対象者による絞り込み |
| 収支集計タブ | 実装確認 | 年度の支払方法別・月次収入／支出／差額、内訳画面への遷移 |
| 残高集計タブ | 実装確認 | 年度の月次残高・評価額、推算値の表示、グラフ、USD換算 |
| 集計内訳 `DetailViewForm` | 実装確認 | 分類／支払方法別、グループ・明細単位の詳細 |
| マスタメンテナンス `MasterMaintenanceForm` | **一部実装** | メニューから画面は開くが、フォーム本体は空の雛形 |
| ポイント入力・反映 | **一部実装** | ポイントマスタ読込とデータクラスはあるが、画面イベントの一部がコメントアウト。レシートポイント保存SQLは未確認 |

## 操作上の補足

- レシート登録は、店名／税込か税抜か／支払方法／明細を最低限チェックして保存ボタンを有効化する。
- 同じ分類のグループを選ぶと「統合」できる。分類の重複登録を防ぐUI処理がある。
- 振替では移動元と移動先が同一のとき登録を拒否する。
- 残高登録では、評価額のみの入力は拒否。少なくとも1件の口座残高が必要。
- 支払方法の所有者として使う `m_user` と、購入明細の「対象者」は同じ人マスタでも用途が異なる。
- 明細対象者の集計は「部分一致」「完全一致」と「対象者１名のみの按分表示」がある。具体的計算は [03-calculations.md](03-calculations.md)。
- アプリのログイン認証、スマホ入力、Web API、クラウド同期は**現行機能としては確認されていない**。

## 根拠

- [メイン画面](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm.vb)、[一覧](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_List.vb)
- [レシート登録](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)、[振替登録](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterTransferForm.vb)、[残高登録](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterBalanceForm.vb)
- [収支集計](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_Expenses.vb)、[分類集計](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_ListStatistics.vb)、[残高集計](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_BalanceStatistics.vb)
- [マスタメンテナンス](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/MasterMaintenanceForm.vb)
