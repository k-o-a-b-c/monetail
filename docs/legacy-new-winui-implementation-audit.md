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

## 人物・支払方法・科目マスタの画面／レシート横断比較（追加）

この節は旧`DataAccess`のマスタ読込・旧レシート入力と、**新WinUIの管理画面実コード**・レシート入力を横断照合した。名前の同一性、順序、現在日と取引日、権限を分けて記録する。前節のレシート明細比較とは別に、**マスタ変更による表示結果の変化**に着目した。

| 観点 | 旧版の実装 | 新版の実装と判断 | 分類 |
| --- | --- | --- | --- |
| 本人の判定キーと重複一致 | 旧`ReadUsers`は`account_name = Environment.UserName`の行を読み、該当する行が複数なら**最後に見つかった人物だけ**を先頭へ移す。複数一致は専用エラーにならない | 新版は認証アカウントに紐付いた**人物ID**で本人を判定し、人物一覧を本人優先で表示。ログイン名や人物表示名の文字列一致を使わない | **旧版の曖昧さを解消・実装確認** |
| 支払方法の人物名表示と内部名 | 旧`ReadPaymentClassification`は本人以外の支払方法の`PaymentClassification.Name`自体に**「（人物名）」を前置**する。一度キャッシュされた表示名がモデルに混在 | 新版は`PaymentName(ledger, paymentId)`で表示時の名称を求め、`PaymentMethodInfo.Name`の実データを前置文字列で上書きしない。人物対応が変わっても表示名の計算で切り替える | **表示維持＋モデル改善** |
| 支払方法一覧の人物グルーピング | 旧支払方法マスタの取得順は`p.seq,p.code`で、本人の分を先頭へ移した**平坦な一覧**。本人以外には人物名の前置で区別する | 新`RenderPayments`は**人物見出しごとのグループ**を作り、本人グループを先頭にして人物内を`DisplayOrder`で並べる。人物の並び順と人物内口座順を別々に管理 | **承認済み変更・管理UI実装済み** |
| マスタ一覧における有効期間 | 旧人物・科目・支払方法の読込ではマスタをほぼ全件キャッシュし、WinForms入力欄では当日有効な行だけを絞る処理がない | 新の人物`Render`、支払方法`RenderPayments`、科目`RenderCategories`は**`DateTime.Now`の今日**で通常表示を絞り、**「無効も含む」**切替で全件を表示できる。履歴をマスタから暗黙に消さない | **新版追加・管理画面確認** |
| レシート入力での有効期間基準 | 旧レシートフォームのマスタComboBoxは旧`Read`の候補をそのまま渡す | 新`RefreshReceiptEditorChoices`はマスタ管理画面の「今日」ではなく**取引日**で支払方法・新規追加科目を判定する。**今日無効でも過去の取引日に有効なら入力候補になる**ため、管理画面に見えないことは使用不可を意味しない | **重要な新業務仕様・実装確認** |
| 期限外の保存済み支払方法 | 旧レシートフォームは有効期間で既存値を排除する実装がない | 新`RefreshReceiptEditorChoices`は取引日に無効でも**現在選択中の支払方法だけは候補に残して警告**する。変更なしの既存値が日付変更で黙って別口座へ置換されないが、保存時にはAPI検証が必要 | **保存値保護・実装確認** |
| 期限外の科目を持つ既存レシート | 旧フォームは全科目を使い、既存グループの分類を表示する | 新版は**科目追加候補は当日の取引日条件でフィルタ**する一方、**既存グループ一覧はマスタ全件から名称を解決**する。期限外で新規追加できない科目でも、過去の保存済み科目が画面から消えない | **保存値保護・実装確認** |
| 登録済みレシートの科目表示順 | 旧レシートはグループの`seq`順で読み込み、旧科目マスタは`m_group_classification.seq`順で利用する | 新`RenderReceiptGroups`は表示時に**現在の科目マスタ`DisplayOrder`**を参照しソートする。マスタの並び替えで、保存済み明細自体を更新しなくてもレシート画面の**科目の見た目の順番だけ変化**し得る。新版API仕様の「科目マスタ順」と整合 | **承認済み変更・表示順と保存値を区別** |
| 科目追加時の収支・税初期値 | 旧科目マスタには`default_tax_target/reduced_tax_target`があり、`RegisterForm`は当該フラグと固定8/10%から明細初期値を決める | 新`CategoryDialog`は**収支区分`Expense`・税区分`Standard`**を新規科目の初期値とし、業務上の各明細の実税率はその後に履歴から確定する設計。**現WinUIのレシート側は履歴未接続**で新規明細税率は未選択 | **新規マスタ初期値の変更・6-D4接続待ち** |
| 科目・支払方法の更新権限 | 旧マスタ編集用`MasterMaintenanceForm`は未完成で、現在の旧版にはAPIの家計簿ロールがない | 新人物は**管理者が全員を追加/削除**、本人は許可範囲の編集。支払方法は**Adminは全人物、Editorは本人所有分**。科目は**家計簿Adminのみ**変更できる。UIのボタン無効化とサーバー権限制御を別層で持つ | **新版固有の権限モデル・実装確認** |
| マスタの並び替えと保存レコード | 旧版は`m_user.seq`・`m_payment_classification.seq`・`m_group_classification.seq`で並び順を決め、操作用管理画面は未完成 | 新版の専用順序画面では行IDの並びを送信し、**人物は共通順・支払方法は人物別順・科目は家計簿全体順**として区別。表示だけの本人優先順は保存順へ書き戻さない | **新版追加・実装確認** |
| 既存レシート編集時のマスタ更新反映 | 旧マスタはSQLite接続の生存期間中キャッシュされ、別画面更新での鮮度保証がない | 新`ReceiptMasterChanged`は**レシートの支払方法・科目追加候補、一覧の口座表示名**を再描画するが、**開いているレシートの登録済み科目行・明細人物チェック欄までは再描画しない**。同画面を開いたままマスタが更新された場合の表示鮮度は実画面／キャッシュ通知で検証が必要 | **新版の更新境界・未再現の検証候補** |

**根拠**：[旧マスタSQL](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)／[旧科目入力](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)／[新版人物一覧](../src/Monetail.WinUI/MainWindow.xaml.cs)／[新版人物権限](../src/Monetail.WinUI/MainWindow.Persons.cs)／[新版支払方法管理](../src/Monetail.WinUI/MainWindow.Payments.cs)／[新版科目管理](../src/Monetail.WinUI/MainWindow.Categories.cs)／[新版人物順序](../src/Monetail.WinUI/MainWindow.PersonOrder.cs)／[新版支払方法順序](../src/Monetail.WinUI/MainWindow.PaymentOrder.cs)／[新版科目順序](../src/Monetail.WinUI/MainWindow.CategoryOrder.cs)／[新版編集候補・マスタ通知](../src/Monetail.WinUI/MainWindow.ReceiptEditor.cs)／[新版通知・一覧](../src/Monetail.WinUI/MainWindow.Receipts.cs)／[科目マスタ仕様](ledger-core-schema.md)／[レシートAPI仕様](ledger-receipt-api.md)。

**優先判断**：マスタ管理画面の「今日有効」とレシート入力の「取引日に有効」を同一フィルタと誤解しない。**実装上の検証候補**（人物チェック欄・登録済み科目行の表示鮮度）は、該当イベントの発生条件を再現したテストで確認してから改修する。

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
