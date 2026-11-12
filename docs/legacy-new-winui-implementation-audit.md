# 旧WinFormsと現WinUI・Windows Clientの実装照合

[← 文書の入口](INDEX.md) | [旧版／新版の機能比較](legacy-new-comparison.md) | [新版レシートUIの正本](ledger-receipt-ui.md) | [実装工程](ledger-receipt-implementation.md)

## 対象・判定の意味

2026-11-12時点の旧VB.NET `RegisterForm`／`ViewForm_List`／`DataAccess`／入力式と、新C# `MainWindow.ReceiptEditor`／`ReceiptDetails`／`Receipts`、`ReceiptEditorState`／`ReceiptDetailInput`／`ReceiptListState`／`ReceiptSearchState`を**実コードで対照**。関連のWindows Client xUnitとWinUI Smokeテストも照合した。別途実施した旧VB／新APIの24ケース計算比較とは異なり、**旧WinFormsと新WinUIの自動E2E相互実行はしていない**。

- **実装確認**：旧版との継承／改善をコード・既存テストで確認。両製品の実機UI同等性を保証しない。
- **承認済み差異**：新版の既存DEC/正本で決まっている改善。旧版へ戻す理由にしない。
- **後続接続**：新APIやClientの部品があっても現WinUIの操作へ未接続。既存工程6-D4／D5を優先。
- **実装上の検証候補**：新版コードに条件付きの予期せぬ動作を見つけたもの。再現テスト前に断定した不具合とは呼ばない。

## 旧UIの実処理 ↔ 新UI/Clientの直接対照

| 観点 | 旧WinFormsの実処理 | 現WinUI・Clientの実処理・照合結果 | 判定 |
| --- | --- | --- | --- |
| レシート新規の日時 | 旧`RegisterForm_Load`は**今日の日付・時刻00:00:00**から開始。時刻0で保存する場合は確認を出す | 新`OpenReceiptEditorAsync`も`DateTime.Today`を初期値にし、表示は日付・時刻欄へ分割。**時刻0の保存確認は全体保存未接続のため未検証** | **日時初期値は実装確認／保存確認は後続** |
| 既存レシート編集のオブジェクト | 旧`ModifyButton_Click`は一覧の`Receipt`を編集画面へ**同一参照で渡す**。未保存の明細操作でも一覧のメモリモデルへ影響し得る | 新`ReceiptEditorState.Edit`は詳細GETの明細を**`ReceiptDetailDraft.From`→Copy**して保持、フォームと確定行を分離。未保存の破棄は既存の画面内モデルに留まる | **改善・実装確認** |
| 編集画面の読込中・古い応答 | 旧版は同一SQLiteから同期読込するため旧フォームにAPI応答の失効概念がない | 新`OpenReceiptEditorAsync`は詳細GET→マスタ取得をキャンセル可能な要求として実行し、**画面・本人・家計簿・要求同一性**を確認してから表示する | **新版固有・実装確認** |
| 明細一覧での入力切替 | 旧`DetailListView_SelectedIndexChanged`は未反映値を確認せず、行選択と同時にフォームを書き換える | 新`ReceiptDetailSelected`／`ResolveReceiptDetailAsync`は**追加/更新して移動・破棄して移動・戻る**を明示し、確認・検証に失敗したら元の選択と生文字列を保持。既存Smokeで成功・取消・入力不正を確認済み | **承認済み変更・実装確認** |
| 連続明細入力と人物の引継ぎ | 旧`ClearDetailControl(False)`は品名等を戻し、対象人物集合は保持。旧版の新規初回の人物自動選択は不明 | 新`ReceiptEditorState.ApplyDraft`は新規明細へ人物だけを持ち越し、価格・数量・値引率等は初期値へ戻す。新規初回は0人。人物のSpace・矢印・Tab動作はWinUI Smokeで検証 | **継承＋初回明確化・実装確認** |
| 金額欄で不正な計算式 | 旧`NormalizeMoney`は`IntegerExpression.EvaluateOrZero`で**不正式／Int32超過を0に置換**する | 新`ReceiptDetailInput`は同じ**整数四則のうち加減乗の優先順**を解析し、失敗は`ReceiptInputException`。生入力を残し、明細モデルは書き換えない。入力`100+20*3`の成功はSmokeで検証済み | **承認済み変更・実装確認** |
| 数量の不正値 | 旧`NormalizeQuantity`は**空欄・不正・0以下を1に変更**する | 新`ReceiptDetailInput.Read`は0以下などが保存用の`ValidateDetail`で不正となり、入力値の意図しない1補正をしない。明細フォーム検証も原子的 | **承認済み変更・実装確認** |
| 率値引の不正値 | 旧`NormalizeDiscountRate`は不正値を0、範囲外を**0～100へクランプ**する | 新`ExactDecimal`は不正な文字を拒否し、保存用検証で範囲外を拒否。**見た目だけ0／100に直して登録しない** | **承認済み変更・実装確認** |
| 科目の重複追加・統合 | 旧版は既存科目を編集先に選ぶと［統合］に切替え、明細を合流させる（元メモと税率は消える） | 新`UpdateReceiptCategoryActions`は重複追加を無効化し、`ReceiptEditorState.AddCategory`も重複拒否。**統合UIは存在しない**。旧版の情報欠落を自動移植しない | **旧機能の採否要判断** |
| 科目・支払方法の取引日有効性 | 旧レシートの科目/支払方法候補は原則マスタ全件から構成し、画面の取引日で有効期間を絞る実装はない | 新`RefreshReceiptEditorChoices`は**取引日に有効な科目を追加候補**とし、支払方法は取引日に有効な行＋**現在選択の期限外行を保持**、警告する | **新版の業務制約・実装確認** |
| 明細対象人物の有効期間 | 旧版は人物マスタをフォームへ表示し、選択した人物を明細へ付ける | 新`RenderReceiptDetailPeople`も取得した全人物をチェック候補へ出す。**取引日時点で無効な人物の新規チェックをUIで制限・警告する処理はここにはない**。保存APIの期間検証と併せて入力体験を受入確認する | **現UIの検証候補** |
| 既存明細の非課税・課税0%の復元 | 旧版は明細`TaxTarget`とグループ税率に分かれ、0%と非課税が入力モデル上同じ構造ではない | 新`ReceiptTaxSelection`は**未選択／非課税NULL／課税0%**を区別し、`LoadReceiptDetailForm`は保存済み数値税率がその日の候補にまだなくても元の値を専用候補に追加して保持 | **新保存モデル・実装確認** |
| 非課税科目の新規初期税率 | 旧グループ追加後は科目の`DefaultTaxTarget`をチェック欄へ反映し、旧率の初期選択は原則8／10%の固定分岐 | 新正本は`Exempt`科目なら履歴なしでも**非課税を初期選択**。ただし現在の`OpenReceiptEditorAsync.Defaults`は科目の`IncomeExpense`だけ読み、税選択を**全科目一律Unselected**で返す | **6-D4未接続（仕様と現UIの差）** |
| 店名・所在地・品名の入力候補 | 旧`RegisterForm`は店・所在地・品名の**履歴候補付きComboBox**を利用 | 新XAMLの親は単純`TextBox`、明細品名も単純`TextBox`。Client/APIに`SuggestAsync`はあるが、**今のWinUIには履歴検索・候補の表示呼出しがない** | **6-D5未接続** |
| 店名変更時の所在地クリア | 旧版は別の店名へ変更しても以前の所在地が非空なら保持。新仕様DEC-187は**別店名への確定変更時に所在地をクリア** | 現`ReceiptParentTextChanged`→`SyncReceiptHeader`は**ShopNameとShopLocationを各欄の値で同期するだけ**。店名変更で所在地をクリアする分岐も、店名確定の状態追跡もない | **6-D5未接続（仕様と現UIの差）** |
| 店名による支払方法・税設定補完 | 旧`ShopNameComboBox_Validated`は店の履歴から支払方法・税込/税抜などを補完し、手動済みを維持 | 新`ReceiptParentInputState`は税込／税抜・端数方式の手動/自動由来を保持できる。しかし**現WinUIは`AutofillAsync`を呼ばず、支払方法の自動補完もない** | **6-D5未接続（基盤あり）** |
| 税額・参考額・レシート保存の画面接続 | 旧版は明細・グループ・レシート合計を計算してから保存。振替や参照先の金額も更新 | 新`ReceiptDetailRow`は品名・価格・値引・税表示を用意。**参考額・合計は計算表示されず**、`ReceiptRegister/Update/Reuse`はXAMLで無効のまま。APIとClientの保存機能は先行実装済み | **6-D4/D5未接続（意図的段階実装）** |
| 全期間検索と入力途中の不正年月 | 旧一覧は年・月の直接変更、現新版は全期間検索と店名検索を追加 | `ReceiptSearchState.ApplyText`は**AllPeriods=trueでもMonthTextの正当性を先に必須チェック**。WinUIはAllPeriods時に年月欄を無効化する。**無効な年月下書き→全期間ON→店名変更→適用**の順では、全期間なのに店名検索を拒み、無効年月欄へフォーカスしようとする可能性がある | **新版の再現テスト追加を推奨（未実機確認）** |
| 全体保存前の離脱 | 旧レシートフォームの［閉じる］は未反映明細を含む変更の共通確認がなく、元モデルへ変更が残り得る | 新`LeaveReceiptEditorAsync`は未保存/不正下書きを検出し**明示破棄か編集継続**に限定。保存ボタンが未接続の現段階では正本DEC-163の「保存して移動」までは提供しない | **6-D5未接続／破棄保護は実装確認** |

### 優先対応

1. **現コード上の再現候補**：全期間ON時の不正年月下書き＋店名検索をWindows ClientテストおよびWinUI Smokeで再現してから、**AllPeriodsでは年月を適用条件にしない**など既存確定仕様と整合する修正を判断する。既存の`ReceiptSearchStateTests`には「不正年月から全期間へ切替えて`Query().Month == all`」のテストはあるが、**その状態で`ApplyText`して店名を確定するテストはない**。
2. **工程6-D4**：税率履歴を全件取得・キャッシュし、非課税科目を履歴なしでも非課税選択、当日有効率・保存済み率の保持、正式計算の参考額表示を接続する。固定8/10%や未選択でごまかさない。
3. **工程6-D5**：店名・所在地・品名のAPI候補・入力確定と、店名変更による所在地クリア・手動設定保護付き自動補完、保存／競合／再送を接続する。
4. **後続判断**：旧グループ統合の採否と、人物の有効期間外チェックのUIフィードバック。旧版の問題を自動的に再現しない。

**スコープ外**：旧運用DBへのアクセス・移行テスト、まだ存在しない振替／残高／支払メモの新画面、旧WinFormsと新WinUIの実機UI同時実行。**本書は監査文書のみ**で、製品コード・仕様の正本・テストは変更しない。

## 根拠コード・テスト

- 旧入力：[レシート画面](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)、[旧一覧](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_List.vb)、[旧マスタSQL](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、[旧式評価](../HouseholdAccountsBeta/HouseholdAccountsBeta/Logic/IntegerExpression.vb)
- 新WinUI：[編集・親入力](../src/Monetail.WinUI/MainWindow.ReceiptEditor.cs)、[明細・人物](../src/Monetail.WinUI/MainWindow.ReceiptDetails.cs)、[レシート検索](../src/Monetail.WinUI/MainWindow.Receipts.cs)、[XAML](../src/Monetail.WinUI/MainWindow.xaml)
- 新Client：[編集遷移](../src/Monetail.Windows.Client/Receipts/ReceiptEditorState.cs)、[明細入力](../src/Monetail.Windows.Client/Receipts/ReceiptDetailInput.cs)、[親の自動値由来](../src/Monetail.Windows.Client/Receipts/ReceiptParentInputState.cs)、[一覧・カーソル](../src/Monetail.Windows.Client/Receipts/ReceiptListState.cs)、[検索フォーム](../src/Monetail.Windows.Client/Receipts/ReceiptSearchState.cs)
- 検証済み：[編集中の3択xUnit](../tests/Monetail.Windows.Client.Tests/ReceiptEditorTransitionsTests.cs)、[入力xUnit](../tests/Monetail.Windows.Client.Tests/ReceiptListStateTests.cs)、[明細操作Smoke](../tests/Monetail.WinUI.Smoke/ReceiptDetailsScenario.cs)、[キーボードSmoke](../tests/Monetail.WinUI.Smoke/ReceiptDetailKeyboardScenario.cs)、[科目・親情報Smoke](../tests/Monetail.WinUI.Smoke/ReceiptEditorOperationsScenario.cs)
