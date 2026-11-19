# テストの構成・実行と結果

件数・実行方法・採用結果の正本。旧VB.NET版の検証と、新版WinUI 3・Clientの検証を区別する。過去の詳細は[CI検証記録](ci-verification-records.md)と凍結済みの[履歴](history.md)、旧Windows版の手動確認は[専用チェックリスト](legacy-manual-checks.md)を参照する。実装のない画面を検証済みとは扱わない。

| 対象 | 件数 |
| --- | ---: |
| 旧版の計算 | 35 |
| SQLite移行（税端数・更新版） | 10 |
| 旧版Windowsの実SQLite・画面・起動設定 | 98 |
| ASP.NET Core API | 2175 |
| 登録・ログイン・管理・家計簿招待画面のJavaScript（Node・合成DOM/WebAuthn） | 63 |
| Windows Client（接続・認証・保存・家計簿・人物・参加者・招待・支払方法・科目・税率履歴・レシート読取/書込・補完・編集モデル・共通計算/税率表示・振替読取/書込/編集モデル・スモーク実行制御） | 1194 |
| Windows Presentation（レシート/振替一覧VM・振替編集VM・投影・Command・共通ページング・復帰） | 122 |
| Windowsの実DPAPI・保存 | 3 |
| WinUI 3の製品起動・画面スモーク（専用入力／既存全画面の2プロセス・36シナリオ） | 2 |
| CIの実行対象選択・成功結果の再利用・配布条件・税率JSON検証（Python） | 81 |
| 合計 | 3783 |

## 振替編集画面のView・Presentation分離 第1段階（PR #211）

[TransferEditorViewModelTests](Monetail.Windows.Presentation.Tests/TransferEditorViewModelTests.cs)の新規10件で、同一Editor/Draft・初期未変更、未確定文字列/旧ID・版、保存可否/同一支払方法エラー、期間/権限付き候補と選択保持、マスタ不在時の入力保護、読み取り専用/競合latest非自動採用、7操作の通知、Window状態のライブ参照を確認する。新規ライブラリ/汎用基盤、Client/API/DBの変更はない。

WinUIは既存期待値を維持し、移管したコントロールの取得を新Viewの名前スコープへ、UI専用のprivate診断と支払方法候補型を新所有者へ変更する。transfer-editorへ名前スコープ/同一正本とCommandの接続の2確認を追加。連続401では同じViewModelも保持すること、終了時には閉じる前に保持した同じViewのVM参照が解除されることを追加検証する。入力・保存・流用・削除・結果不明・409・401・フォーカス/表示位置・IME/Enterの既存シナリオは全件対象のまま。責務・状態・依存/行数と後続範囲は[構成正本](../docs/architecture-winui.md#振替編集画面のmvvm分離第1段階)を参照する。

ローカルReleaseは新規VM10件→Presentation全122件が成功。新規C#の対象書式検査、XAML/XML解析、文書ローカルリンク、`git diff --check`も確認した。Client全1,194件・Presentation全122件は[成功ジョブ135311795904](https://github.com/k-o-a-b-c/monetail/actions/runs/44450117251/job/135311795904)で確認し、以後のWinUI専用変更では同じfingerprintの成功を再利用した（親run全体はWinUIのコンパイル失敗）。非変更のAPI/DB/Docker/旧Windows等もCI選択に従って既存成功を再利用する。

最終ソース`90009df`の[Tests 44470519863](https://github.com/k-o-a-b-c/monetail/actions/runs/44470519863)が成功。製品/検証用WinUI x64 Release（各29件の既存WUI2011警告・0エラー）、製品起動、全36シナリオ/763確認（振替401復帰70・競合23・入力18・レシート一覧50・振替一覧52）、個別3シナリオ、不正指定拒否/計測、スクリーンショット取得が成功した。追加警告は残っていない。

途中で列挙型の名前衝突/終了時の旧参照、x:Bind補助関数のFallbackValue、閉鎖済みWindowを検索する追加テストの取得方法を修正した。基点mainの[Tests 44444662278](https://github.com/k-o-a-b-c/monetail/actions/runs/44444662278)は既存スモークが0xC000027Bで異常終了しており、変更前の成功は統合前の[Tests 44442894444](https://github.com/k-o-a-b-c/monetail/actions/runs/44442894444)を参照する。途中の[Tests 44452623688](https://github.com/k-o-a-b-c/monetail/actions/runs/44452623688)では既存の保存行表示待機が一度失敗し、失敗時だけ状態を出す診断を追加した。次の[Tests 44453532291](https://github.com/k-o-a-b-c/monetail/actions/runs/44453532291)では同試験を含む記録済み25シナリオは成功したが、既存の全体120秒上限で終了した。最終実行では両方通過したが、これらの一時失敗の原因は未特定であり、根本解決とは扱わない。検証条件・待機/全体タイムアウトは変更していない。実Windows手動操作、物理IME・タッチ・高DPI・スクリーンリーダーと実API接続は未検証。

## 一覧の破棄統一・旧個別検索の削除（PR #210）

[ListDisposalTests](Monetail.Windows.Presentation.Tests/ListDisposalTests.cs)の新規8ケースで、レシート/振替の初回・追加取得中のDispose、通信取消、同じ行コレクションの消去/選択解除、遅延した成功/失敗、新VM再生成、二重Disposeを確認する。[共通基底](Monetail.Windows.Presentation.Tests/CursorListViewModelTests.cs)へキャンセル通知中に旧応答が完了する1ケースも追加した。既存のSuspend/Resume・失敗後手動再試行・保存行探索/復帰の期待値は維持する。

旧個別検索Command/下書き通知のテストは整理し、共通ApplySharedText→各VM.StartAsyncで確定値・検索中の再検索・旧応答破棄を検証する。Clientの年月/人物/支払方法・保存後条件調整も同じ共通検索で初期設定する。WinUIはVMの削除済みプロパティの代わりに共通キーワードTextBoxと実際のHTTPクエリを使い、各一覧に二重Dispose後の実ListView表示/選択解除の1確認を追加する。構成と変更範囲は[構成正本](../docs/architecture-winui.md#一覧の破棄と共通検索の入口)を参照する。

基点main `b85fb49`の[push Tests 44440371384](https://github.com/k-o-a-b-c/monetail/actions/runs/44440371384)成功を利用。ローカルReleaseは破棄/共通検索の最小12件→Presentation全111件、Client関連17件、追加したキャンセル通知中の応答完了1件が成功（段階間の重複あり）。新規ファイルの対象書式検査・文書ローカルリンク196件・git diff --checkと、旧個別検索メンバーのコード参照が残っていないことも確認した。

ソース`50a9386`の[Tests 44442894444](https://github.com/k-o-a-b-c/monetail/actions/runs/44442894444)が成功。Client全1,194件・Presentation全112件、Windows実保存、WinUI製品/検証用x64 Release（各29件の既存WUI2011警告・0エラー）、製品起動、全36シナリオ/761確認（receipt-list50・transfer-list52）、個別3シナリオ・全件照合・スクリーンショット・不正指定拒否/計測が成功した。既存401復帰・保存行focus・表示位置復元・IME/キー操作も全件実行に含む。非変更のAPI/DB/Docker/旧Windows等はCI選択/fingerprintによって既存成功を再利用。画面配置・通信契約・外部ライブラリに変更なし。実Windowsでの物理IME・タッチ・高DPI・スクリーンリーダーと実API接続は手動未検証。

## 記録一覧の共通Panel・Footer（PR #209）

[RecordListStateTests](Monetail.Windows.Presentation.Tests/RecordListStateTests.cs)の新規2件で、共通表示契約が既存の両VMそのものを参照し、通知/Command/固有エラー/focusを複製しないことを確認する。[RecordListPanelScenario](Monetail.WinUI.Smoke/RecordListPanelScenario.cs)を既存receipt-listへ接続し、仮の残高表示状態でも初期読込・0件・失敗/再試行・登録可否・編集開始通知・追加読込/失敗/続行、状態参照交換/解除を検証する。ListViewの高さ制約/ItemsStackPanel仮想化とFooterが同じScrollViewer内にあることも確認する。残高のAPI/画面/業務機能は追加しない。

既存レシート・振替スモークの期待値は維持し、共通部品へ移した要素の取得を新NameScope＋元のAutomationId照合へ変更する。既存の100/300件補充・20行先読み、キーボード/IME、保存行focus/先頭復帰、縦横位置復元、401復帰も全件実行の対象。列見出し・型付きDataTemplateの構造/属性/表示内容は基点mainと一致する。構成と行数/公開境界は[構成正本](../docs/architecture-winui.md#記録一覧の共通表示枠実装済み)を参照する。

基点main `da86c3d`の[push Tests 44430826263](https://github.com/k-o-a-b-c/monetail/actions/runs/44430826263)成功を再利用。ローカルReleaseは既存両VMの最小6件→新規2件込みPresentation全103件が成功した。Client/Presentationが同じ入力の[成功ジョブ135264532911](https://github.com/k-o-a-b-c/monetail/actions/runs/44435860096/job/135264532911)でClient1,194件・Presentation103件を確認し、以後のWinUI専用修正では成功記録を再利用した。Client件数は基点mainも1,194件だったため冒頭の旧記載1,193を訂正した。

最終ソース`969d3e4`の[Tests 44438322161](https://github.com/k-o-a-b-c/monetail/actions/runs/44438322161)が成功。製品/検証用WinUI x64 Release（各29件の既存WUI2011警告・0エラー）、製品起動、全36シナリオ/759確認（共通部品の追加11確認込み、receipt-list49・transfer-list51）、個別3シナリオ・全件照合・スクリーンショット・不正指定拒否/計測が成功した。初回のx:Bind補助メソッド宣言と追加テストの名前空間指定を修正し、追加スモークで検出した状態参照null解除時の読込表示残留は明示的なFallbackValueで修正した。状態交換/解除と通知未指定も検証し、期待値・タイムアウトは緩和していない。対象C#書式検査・XAML/XML解析・文書ローカルリンク192件・git diff --checkが成功。非変更のAPI/DB/Docker/旧Windows等はCI選択/fingerprintに従って既存成功を再利用する。実Windowsでの物理IME・タッチ・高DPI・スクリーンリーダーと実API接続は手動未検証。

## レシート・振替一覧の401画面判定とアクセシビリティ（PR #207）

レシート一覧の認証失効通知は振替と同じくホーム表示中に限ってログイン画面へ遷移する。既存のホーム内401復帰に加え、管理画面に切り替えた後のレシート一覧取得が認証コンテキスト失効を返しても画面を横取りしない実WinUIスモーク1確認を追加した。レシート見出しLevel2と0件案内のPolite live regionを実コントロールで検証する1確認も追加。検索、ページング、保存行探索の機能は変更しない。

レシート・振替の登録・編集Commandをともに`AllowConcurrentExecutions=true`へ統一。旧HTTP要求を取り消した後、旧Task完了を待たずに再操作できる。実際の二重起動は引き続き各Window入口の`receiptOpening`/`transferOpening`ガードで防ぐ。レシートの[既存取消後再操作テスト](Monetail.Windows.Presentation.Tests/ReceiptViewModelTests.cs)を維持し、振替の登録・編集の[新規2ケース](Monetail.Windows.Presentation.Tests/TransferPresentationTests.cs)で同じ契約を検証する。

[Tests 44401903671](https://github.com/k-o-a-b-c/monetail/actions/runs/44401903671)でClient1,193件・Presentation101件（新規2件）、WinUI 3製品/スモークReleaseビルド、製品起動と全36シナリオ（アクセシビリティ/画面遷移の追加2確認込み）、シナリオ選択・計測、CI証跡が成功。非変更のAPI等はCIの既存成功結果を再利用。実Windowsでのスクリーンリーダー・物理IME/タッチ・高DPIの手動確認は未実施。

## レシート一覧のfocus取得・保存行探索

[ReceiptListFocusTests](Monetail.Api.Tests/ReceiptListFocusTests.cs)・[安全性/実行計画](Monetail.Api.Tests/ReceiptListFocusSecurityTests.cs)に実HTTP/隔離SQLiteの35ケースを追加。対象自身を含む日時/ID境界と1/99/100/101/200/201件、通常cursorによる続き、全フィルターAND、全ロール、別家計簿/条件外/削除済みの同一404、D形式/重複/cursor併用の400、共有120回、取得途中の認証失効/参加解除/対象削除/保存先障害、DB不変、既存索引/キーセット/101件取得を確認する。既存の振替用クエリ受理範囲を広げず、focusReceiptIdの混入も拒否する。

[Client10件](Monetail.Windows.Client.Tests/ReceiptFocusTests.cs)でfocusのD形式送信・全条件/省略・応答cursor・空ID/cursor併用の拒否・認証更新後の要求不変・型付き400/404/自動再検索なし、focus応答の先頭不一致（空/別ID/2行目だけ一致）の拒否を確認する。[ReceiptSavedListTests](Monetail.Windows.Presentation.Tests/ReceiptSavedListTests.cs)15件と両VMの[キャンセル2件](Monetail.Windows.Presentation.Tests/PagingAdapterTests.cs)を追加し、既存82件を維持。300件内の選択/300件外のfocus、1回だけの404復旧、focus後のcursor/先頭復帰、検索競合、旧応答拒否、中断再開/予算維持、401/通信失敗後の明示再試行とキャンセル後の非反復を検証する。

ローカルReleaseは既存Presentation82件→追加後95件→最終99件、API最小1件→追加35件、Client追加7件→先頭検証込み10件が成功（段階間の重複あり、失敗/skipなし）。[ReceiptSavedRowScenario](Monetail.WinUI.Smoke/ReceiptSavedRowScenario.cs)を既存receipt-listへ接続し、保存行250/450の実選択/可視範囲・要求回数・focus時だけの先頭復帰・404復旧・GETだけの実行を確認する。既存の振替も共通の選択表示経路を使い、同じスモーク条件を維持する。

基点main `098369a`の[push CI 44287377637](https://github.com/k-o-a-b-c/monetail/actions/runs/44287377637)は既存レシートのホイール試験で失敗。前回PR #205の診断により、操作直前からoffset/previousがともにScrollableHeightと一致し、既に最下端だったことを確認した。試験の準備位置を半viewport手前へ変更し、下へ移動できる事前条件を追加した。実ホイール1回で次ページを取得する期待値/タイムアウト・製品ホイール処理は変更していない。これは試験開始条件の修正であり、製品のホイール不具合を修正済みとは扱わない。変更前の関連成功は[PR #205のCI](https://github.com/k-o-a-b-c/monetail/actions/runs/44282081990)を参照する。

API/テストが同じソース`eeb77ea`の[APIジョブ134751871074](https://github.com/k-o-a-b-c/monetail/actions/runs/44289794988/job/134751871074)でAPI2175件（19分24秒）、Node検証・Dockerビルド・既存/サーバー/配布アーカイブの起動・データ保全スモークが成功した。親run全体は既存input-controlsのEnter後フォーカス試験で失敗。API全件を中断/重複実行しないため単独の[WinUI 44290766908](https://github.com/k-o-a-b-c/monetail/actions/runs/44290766908)も実行したが、既存transfer-suggestionsの実キー入力待ちで失敗し、どちらも追加のレシート探索試験には未到達だった。

両既存失敗には、期待値・待機上限・製品入力処理を変えず、失敗時のフォーカス/値一致/前面Windowだけを示す診断を追加した。実データや入力全文は記録しない。これらの原因は未特定であり、再実行成功だけで解決扱いにしない。成功したAPIジョブの完走を待って追加差分をpushし、同じAPI入力の成功記録を再利用した。最終ソース`ad36cde`の[Tests 44293296944](https://github.com/k-o-a-b-c/monetail/actions/runs/44293296944)は成功し、Client1193件・Presentation99件・実DPAPI3件、製品/検証用WinUI x64 Release（各29件の既存WUI2011警告/0エラー）、製品起動、全36シナリオ/745確認が成功。receipt-listは追加8確認込み36確認、transfer-list50確認、レシート/振替401復帰・保存後focus・通常画面回帰を含む。個別3シナリオ・全件照合・PNG4枚・不正指定拒否/計測も成功した。API/Docker/計算照合は同じ入力の先行成功を再利用し、影響外の旧Windows等は既存CIの選択規則に従う。文書ローカルリンク621件とgit diff --checkも確認した。保存UI・保存後条件解除・DB/Migration・外部ライブラリは対象外。実Windows手動操作・物理IME/タッチ/高DPI・実API接続は未検証。構造/計測は[構成正本](../docs/architecture-winui.md#レシート保存行探索の追加と振替との共通化)を参照する。

最終書式検査で新規APIテスト2行のインデントの取りこぼしが判明し、`43a7147`で先頭空白だけを修正した。コード・文字列・期待値の不変と、対象書式検査の終了コード0を確認。[Tests 44294924196](https://github.com/k-o-a-b-c/monetail/actions/runs/44294924196)でAPI2175件（25分）を再実行して成功し、変更のないClient/WinUI等の成功は再利用した。書式確認を長いCI開始前に完了できなかったため、API全件の追加実行が必要になった。

## レシート・振替ViewModelの共通ページング

既存Presentation73件の期待値・検証条件を維持し、[CursorListViewModelTests](Monetail.Windows.Presentation.Tests/CursorListViewModelTests.cs)6件と[PagingAdapterTests](Monetail.Windows.Presentation.Tests/PagingAdapterTests.cs)3件を追加した。単一の行/選択・100/300/20の境界、予算、Continue/Retry Command、中断したカーソルの再開、旧世代の遅延成功/失敗、1回だけのカーソル復旧、未知例外の伝播、両VMで古い応答を名称投影前に拒否すること、focus後の通常カーソル404の非再検索を検証する。振替の保存行探索/再試行/認証/中断復帰とレシートのページング/中断復帰/破棄は既存テストをそのまま使用する。

ローカルReleaseは最小6件→既存73件→追加9件込みの全82件が成功（段階間の重複あり、失敗/skipなし）。最初の最小実行はサンドボックスのMSBuild名前付きパイプが拒否され、権限付き実行で成功した。ローカルNuGet監査情報の取得にNU1900が出たため、Windows CIの警告と区別する。構造の数え方と共通化しない責務は[構成正本](../docs/architecture-winui.md#レシート振替のviewmodelページング共通化実装済み)を参照する。

基点main `a7c3dc5`の[push Tests 44279501605](https://github.com/k-o-a-b-c/monetail/actions/runs/44279501605)は既存receipt-listのホイール追加取得待機で失敗し、変更前全件成功とは扱わない。PR #204先端の[Tests 44277468709](https://github.com/k-o-a-b-c/monetail/actions/runs/44277468709)は成功している。共通化ソース`7991449`の[Clientジョブ134721865763](https://github.com/k-o-a-b-c/monetail/actions/runs/44281116739/job/134721865763)でClient1183件・Presentation82件が成功した（親run全体は同じホイール試験で失敗）。失敗時だけ操作直前の位置/可視行/読込状態を出す診断を追加し、期待値・タイムアウト・製品スクロール処理は変更していない。

診断追加後のソース`f84195d`の[Tests 44282081990](https://github.com/k-o-a-b-c/monetail/actions/runs/44282081990)で製品/検証用x64 Releaseビルド（各29件の既存WUI2011警告・0エラー）、製品起動、全36シナリオ/737確認、個別3シナリオ・一覧/全件照合・PNG4枚・不正指定拒否/計測が成功した。receipt-list28確認・transfer-list50確認、レシート401復帰30確認・振替401復帰70確認と競合/保存後focusも含む。Client成功記録は同じ入力の先行ジョブを再利用し、影響外のAPI/Docker/旧Windows等は既存のCI選択/fingerprintに従って再実行していない。ローカルの対象書式検査、文書ローカルリンク176件、git diff --checkも成功した。ホイール試験の一時失敗原因は未特定で、今回の成功を根本解決とは扱わない。実Windows手動操作・物理IME/タッチ/高DPIは未検証。API/DB/Clientの契約とWinUIの表示/入力処理、レシートの未接続保存UI/focus APIは変更していない。

## レシート一覧のPresentation MVVM移行

旧`ReceiptListState`のページング/中断復帰23ケースを[Presentation](Monetail.Windows.Presentation.Tests/ReceiptListViewModelTests.cs)・[ページング](Monetail.Windows.Presentation.Tests/ReceiptPagingTests.cs)・[画面往復](Monetail.Windows.Presentation.Tests/ReceiptNavigationTests.cs)へ移管した。共通検索条件/下書きの7ケースは[Client](Monetail.Windows.Client.Tests/ReceiptSearchStateTests.cs)に残す。移管前後の13 Fact・17 InlineData・144 Assertが一致し、DTO列の比較は同じ投影元DTOを比較する。旧クラスを包むAdapterやClient→Presentation参照は追加しない。

[追加9ケース](Monetail.Windows.Presentation.Tests/ReceiptViewModelTests.cs)は単一コレクション/選択、検索下書きの正本、Command通知/転送/検索競合、エラー/再試行、認証例外の保持、名称だけの再表示、登録入口のWindow所有ガード、破棄時の行/選択クリアと旧応答拒否、表示領域に関係する通知だけを検証する。基点mainの振替Presentationは列挙で41件（旧一覧表の40件を補正）、移管23＋新規9で現在73件。Clientは23件を移して1,183件、削除/緩和ではない。

WinUIは既存の期待値を維持して新NameScope/投影型/VMの取得元だけを変更した。[ReceiptMvvmScenario](Monetail.WinUI.Smoke/ReceiptMvvmScenario.cs)・[ReceiptViewportScenario](Monetail.WinUI.Smoke/ReceiptViewportScenario.cs)は同じ`receipt-list`へ接続する。単一ItemsSource/余分なTab停止なし、実Enter/Tab/Shift+Tab/上下、合成IME抑止、100→300件補充/明示続行/リサイズ、無関係な通知で測定しないこと、Commandの有効状態、実ホイールの1回取得、縦横とも非ゼロの位置から編集取消後の同一VM/行/選択/表示位置へ戻ることを確認する。既存AutomationIdに差分はなく、編集開始待ちUIと入力/認証復帰のコントロールはWindowへ残す。

基点main `f9b40df`の[push Tests 44254478282](https://github.com/k-o-a-b-c/monetail/actions/runs/44254478282)成功を採用。ローカルClient1,183件・Presentation71件、破棄/通知修正後の関連9件が成功。修正後のClient1,183件/Presentation73件は[Tests 44259720815の成功ジョブ](https://github.com/k-o-a-b-c/monetail/actions/runs/44259720815/job/134649938308)で確認し、その実行全体のWinUI失敗を成功とは扱わない。以後のSmoke/文書変更ではこれらとAPI/DB・Docker・旧Windows/性能/計算照合等の同じfingerprintの成功を再利用する。

最終製品/スモークソース`8ab909c`の[Tests 44263469744](https://github.com/k-o-a-b-c/monetail/actions/runs/44263469744)が成功。製品/検証用x64 Release（各29件の既存WUI2011警告/0エラー）・製品起動・全36シナリオ/737確認（従来722＋新15）、単独3シナリオ/選択一覧・計測・不正指定拒否・PNG4枚生成が成功した。レシート一覧は28確認/5.935秒、401復帰30確認、メモ26確認。今回ネイティブアクセス違反は観測していない。対象限定の書式検査と`git diff --check`も成功。橋渡し部分は変更した複数文の行だけを改行し、非空白のトークン列が同じことを照合した。

追加スモークで、VMをnullにするだけではx:Bind先の古い行が残るケースを検出した。旧Windowの行クリアをVM.Disposeへ移し、Suspendによる入力退避/画面往復とは区別した。途中には既存401復帰の縦位置差15px、メモIMEのフォーカス確認でも失敗があり、別実行では通過している。両者の原因は未特定であり、解決済みとはしない。入力値を含めない診断を追加し、既存の判定/タイムアウトは維持した。新しい実入力試験は座標だけで送らずPointerMovedの到達を観測し、ホイールを1回送る。レシート全体の保存UIは未接続・無効のままで、保存ボタンからの実POST/PUT/DELETEを検証済みとは扱わない。Windows CIの自動検証と、未実施の実Windows手動操作（物理IME/実タッチ/高DPI/支援技術）を区別する。構造比較と共通Pager候補/残る差異は[構成資料](../docs/architecture-winui.md#レシート一覧のmvvm移行実装済み)を参照。

## 振替一覧のPresentation MVVM移行

[TransferListViewModelTests](Monetail.Windows.Presentation.Tests/TransferListViewModelTests.cs)・[TransferPagingTests](Monetail.Windows.Presentation.Tests/TransferPagingTests.cs)・[TransferSavedListTests](Monetail.Windows.Presentation.Tests/TransferSavedListTests.cs)へ旧Clientの表示状態29件を移管した。共通条件/検索下書き/保存後条件調整の契約は[RecordSearchStateTests](Monetail.Windows.Client.Tests/RecordSearchStateTests.cs)とClient側のTransferPaging/TransferSavedListTestsへ残す。旧3ファイルの26テストメソッドの属性・InlineData・Assert数が移管後も維持されることを照合し、カーソル復旧の選択解除は解除前の選択を明示して強化した。

[TransferPresentationTests](Monetail.Windows.Presentation.Tests/TransferPresentationTests.cs)は新規11件。通知/Command、読取専用の唯一の行コレクション・選択、RecordSearchStateへの未確定原文の窓口、外部変更通知、マスタ名称だけの再表示、認証エラーをWindowへ残すこと、保存行探索の明示再試行/同じカーソルからの再開・同期404の1回復旧、検索中の再検索と旧応答破棄を確認する。検索Commandは既存どおり前の取得待ちでも使用でき、実要求の中断/世代検証はVMで行う。

WinUIの既存確認は新ViewのNameScopeとTransferListItemへ取得方法を移し、期待値を維持した。[TransferMvvmScenario](Monetail.WinUI.Smoke/TransferMvvmScenario.cs)を既存`transfer-list`に接続し、同一ItemsSource、余分なTab停止なし、実Enter/Tab/Shift+Tab/上下キー、合成IME中の検索・外側Enter移動の抑止の5確認を追加。製品と同じView XAML/C#をスモークにリンクする。既存の初回/追加中断・復帰、縦横スクロール/選択、保存行探索/フォーカス、編集の連続401/別アカウント/マスタ再取得、409・結果不明・未保存保護も維持する。

```sh
dotnet test tests/Monetail.Windows.Presentation.Tests/Monetail.Windows.Presentation.Tests.csproj -c Release
dotnet test tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj -c Release
```

CIは既存`windows-client-tests`内で両プロジェクトを実行する。Presentation製品変更は`windows_client`/`winui`、Presentation試験変更は`windows_client`だけを選び、選択・fingerprintの2回帰試験を追加した。既存のmain未検証検出・未分類ファイルの安全側判定・成功記録の再利用は変更しない。共通の選択定義を変更した初回は既存ルールどおり各検証の指紋が変わるため、API/旧Windows/性能も必須CIで確認する。以降のViewModel/Viewだけの修正は非変更部分の成功を再利用する。

基点main `f7fb0e8`の[push Tests 44204197511](https://github.com/k-o-a-b-c/monetail/actions/runs/44204197511)成功を変更前の根拠とする。ローカルClient1,206件、Presentation40件、Python81件が成功。初回のローカル復元/IPCはサンドボックスで拒否されたため許可された実行で再試行し、通信可能な復元後のログにNU1900等の追加警告はない。初回Windowsビルドはx:Bind関数のstatic呼出不一致で失敗し、インスタンスメソッド化で修正した。Viewの継承メンバーとの同名警告も解消した。

ソース`654306d`の[Tests 44207601522](https://github.com/k-o-a-b-c/monetail/actions/runs/44207601522)で製品/検証用x64 Release・起動・全36シナリオ/702確認（旧697＋新5）と単独3シナリオ/選択一覧/計測/不正指定拒否/PNG4枚生成が成功。両ビルドは各29件の既存WUI2011警告/0エラー、ネイティブアクセス違反は観測していない。保存行探索/検索Commandの境界補足後、MVVM移行時の製品/テストソース`0f19853`の[Tests 44210963664](https://github.com/k-o-a-b-c/monetail/actions/runs/44210963664)も成功。Client1,206件/Presentation40件とWindows全件を再確認し、非変更API/DB・Docker・旧Windows/性能/計算照合は同じ指紋の成功記録を再利用した。前の全体CIではAPI2,140件・Node・Docker/配備スモーク・旧Windows等も成功している。`git diff --check`と対象の書式検査も成功。構造比較は[構成資料](../docs/architecture-winui.md#実装の所有者と構造比較)を参照する。WinApp CLI/WinUIを実行できないLinuxのためWindows CIを使用し、実Windowsでの物理IME・タッチ・高DPI/支援技術等の手動操作は未検証。

### TransferListViewの観測・表示要求の簡素化

[TransferViewSimplificationScenario.cs](Monetail.WinUI.Smoke/TransferViewSimplificationScenario.cs)を同じ`transfer-list`へ接続し、20確認を追加した。巨大な合成表示領域で100→300件の補充上限/明示続行、Rows/Size変更後の測定、無関係なVM通知での予約なし、復元時の操作意図解除を確認する。実入力用には画面内へ配置を戻し、SendInputによるホイール/標準垂直Thumbのドラッグ/Endキー、合成DirectManipulation開始後の移動で100件ずつ1回だけ追加することを検証する。保存行250件目/450件目の可視範囲表示と探索要求数、標準ButtonのCanExecute通知、権限/編集中の起動待ち/取消後の有効状態も確認する。既存の縦横復元/選択/画面切替/401/IME/Tabの確認は維持した。

基点`b71c7fa`の[Tests 44211936108](https://github.com/k-o-a-b-c/monetail/actions/runs/44211936108)成功を採用。今回のローカルPresentation40件（失敗/skip 0）、対象限定の書式検査、`git diff --check`が成功した。ソース`2e44d43`の[Tests 44242928618](https://github.com/k-o-a-b-c/monetail/actions/runs/44242928618)で製品/検証用x64 Release（各29件の既存WUI2011警告/0エラー）・製品起動・全36シナリオ/722確認が成功。振替一覧は50確認/9.788秒。個別3シナリオ・選択一覧/全件照合・計測・不正指定拒否・PNG4枚生成も成功した。非変更のClient/API/DB/Docker/旧Windows/性能/計算照合等は既存CIの同一fingerprintの成功を再利用した。

自動補充の追加試験で、Loaded時の保持参照が現在のScrollViewerと一致せず監視できない状態を検出し、製品の接続をレイアウト確定後へ変更した。実入力試験は標準垂直Thumbの明示指定、画面内座標の確認、操作間の製品RestorePosition経路、移動も含むSendInputへ修正している。期待件数・既存判定・タイムアウトは緩和していない。途中の[Tests 44240087770 初回](https://github.com/k-o-a-b-c/monetail/actions/runs/44240087770/attempts/1)では、追加試験到達前に既存`input-controls`のEnter後フォーカス確認が1回失敗した。失敗ジョブの再実行と最終全件では通ったが、この一時失敗の原因は未特定であり、解決済みとは扱わない。物理IME/タッチ、高DPI・支援技術等の実Windows手動操作は未検証。タッチ開始は合成呼出であり、実タッチの検証ではない。

## WinUI：科目一覧UserControlの分離

既存[CategoriesScenario.cs](Monetail.WinUI.Smoke/CategoriesScenario.cs)は全確認・期待値を維持し、移管した7コントロールの取得元だけをCategoryListViewのNameScopeへ変更する。既存ソースを正規化して取得元/追加試験呼出以外に差分がないことも確認した。[CategoryListScenario.cs](Monetail.WinUI.Smoke/CategoryListScenario.cs)は同じ`categories`シナリオに接続し、AutomationId・余分なTab停止なし・実Tab/Shift+Tab順・Spaceでの期間切替・選択保持・追加GET/書込なしを確認する。製品と同じView XAML/コードを検証用へリンクする。

基点main `aeb1a18`の[push Tests 44028991687](https://github.com/k-o-a-b-c/monetail/actions/runs/44028991687)成功を変更前の根拠とする。ソース`1c88272`の[Tests 44047430871](https://github.com/k-o-a-b-c/monetail/actions/runs/44047430871)で製品/検証用x64 Release（各29件の既存WUI2011警告/0エラー）、製品起動、全36シナリオ/697確認（今回3確認追加）、単独3シナリオ、一覧/全件照合、PNG4枚生成、不正指定拒否・計測が成功した。科目は48確認/6.894秒で、CRUD・並び順・競合・401復帰を含む。既存の画面遷移・他画面の認証復帰も維持する。

[初回CI 44046570118](https://github.com/k-o-a-b-c/monetail/actions/runs/44046570118)は追加試験の全件数を3件と誤指定して待機失敗した。架空データは4件のため実際の全件数へ修正し、再検証で成功した。製品コード/既存期待値/タイムアウトの変更はない。Client/API/DBなど非変更部分は既存CI選択/fingerprintで成功結果を再利用し、ローカル`git diff --check`も成功。構造の集計条件は[構成資料](../docs/architecture-winui.md#科目一覧の分離一覧だけ)。LinuxでWinApp CLI/WinUI実行不可のためWindows CIを使い、実Windows手動操作/物理IME/タッチ/高DPI等は未検証。

## WinUI：家計簿選択UserControlの分離

[LedgerChoiceScenario.cs](Monetail.WinUI.Smoke/LedgerChoiceScenario.cs)を既存`connection-flow`へ接続する。未選択/ボタン無効、元のAutomationId/単一選択、一覧クリアと再設定、実上下キー・Tab/Shift+Tab・Enter、ID通知と家計簿openが各1回、同一Window、開く失敗時の選択保持、401時の一覧/選択解除を実Workspace/合成HTTPで確認する。既存の選択→ボタンクリックによる起動も維持する。テストはUserControl自身のNameScopeから内側を取得し、表示確認の型をFrameworkElementへ一般化する（同じVisibility条件を維持）。製品と同じUserControl XAML/コードを検証用プロジェクトへリンクする。

基点main `0e246d2`の[push Tests 44011743170](https://github.com/k-o-a-b-c/monetail/actions/runs/44011743170)成功を変更前の根拠とし、非変更Client/API/DB等は既存CI選択/fingerprintに従って再実行を避ける。ソース`023497b`の[Tests 44015105260](https://github.com/k-o-a-b-c/monetail/actions/runs/44015105260)は初回で成功。製品/検証用x64 Release（各29件の既存WUI2011警告/0エラー）、製品起動、全36シナリオ/694確認（専用入力201＋既存493、今回10確認追加）、単独3シナリオ、一覧/全件照合、PNG4枚生成、不正指定拒否・計測が成功した。`connection-flow`は39確認。既存のレシート/振替401、競合・未保存保護、管理画面の回帰も含む。検証条件/タイムアウトの変更や失敗後の再実行はなく、ローカル`git diff --check`も成功。構造の比較値・集計定義は[構成資料](../docs/architecture-winui.md#家計簿選択画面の分離第1段階)を参照する。LinuxではWinApp CLI/WinUI実行不可のためWindows CIを使用し、実Windowsの手動操作・物理IME/タッチ/高DPI等は未検証。

## WinUIスモークの選択実行・計測

[run.ps1](Monetail.WinUI.Smoke/run.ps1)は`-Scenario`で1つだけ選択し、`-ListScenarios`で名前とスイートを表示する。指定なしは従来どおり専用入力→既存画面の2プロセスで全件実行し、各120秒の上限・失敗時の非ゼロ終了・`-Capture`による画像と既存ファイル名を維持する。直接exeを起動する場合も、`--scenario`なしでは全件を選ぶ。CIの全件範囲は減らさない。

```powershell
dotnet build src/Monetail.WinUI/Monetail.WinUI.csproj -c Release -r win-x64 -p:Platform=x64 -o "$env:TEMP\monetail-app"
dotnet build tests/Monetail.WinUI.Smoke/Monetail.WinUI.Smoke.csproj -c Release -r win-x64 -p:Platform=x64 -o "$env:TEMP\monetail-smoke"
$paths = @{
    AppPath = "$env:TEMP\monetail-app\Monetail.WinUI.exe"
    SmokePath = "$env:TEMP\monetail-smoke\Monetail.WinUI.Smoke.exe"
    OutputPath = "$env:TEMP\monetail-smoke-results"
}
& tests/Monetail.WinUI.Smoke/run.ps1 @paths -ListScenarios
& tests/Monetail.WinUI.Smoke/run.ps1 @paths -Scenario transfer-reauthentication
& tests/Monetail.WinUI.Smoke/run.ps1 @paths -Scenario input-controls
& tests/Monetail.WinUI.Smoke/run.ps1 @paths
# 既存の撮影・参加者復帰反復も維持する。
& tests/Monetail.WinUI.Smoke/run.ps1 @paths -Capture
& tests/Monetail.WinUI.Smoke/run.ps1 @paths -Scenario participant-authentication-recovery -RepeatParticipantRecovery
```

[App.Scenarios.cs](Monetail.WinUI.Smoke/App.Scenarios.cs)に36シナリオを明示登録する。各シナリオの既存の合成接続/Window初期化を使い、初期ホーム/管理画面と操作ヘルパーの確認は依存があるため`shell`にまとめる。シナリオ内部の確認項目は個別化しない。共通Windowは、単独の入力コントロール試験が自分のWindowを閉じた際に、結果記録前にアプリが正常終了するのを防ぐために保持する。従来の`--transfer-editor-only`とCI用`--existing-only`で2プロセスの分割を維持する。

[SmokeScenarioExecution.cs](Monetail.WinUI.Smoke/SmokeScenarioExecution.cs)は名前の選択と`Stopwatch`計測だけを行う。`smoke-result.json`の既存項目に`scenarios`を追加し、各行に`name`・`suite`・`passed`・`elapsedMilliseconds`・`checks`（このシナリオで成功した確認数）を記録する。実行中は全体`passed:false`で途中結果を保存し、全件成功後だけtrueとする。例外/キャンセルでも失敗行と経過時間を記録して例外を伝播する。ネイティブクラッシュ/強制終了では進行中の完了時間は得られず、最後に書けた結果と既存診断ログを確認する。時間は各シナリオ本体の壁時計経過で、ビルド/製品起動/共通Window準備は含まない。

[verify-scenario-selection.ps1](Monetail.WinUI.Smoke/verify-scenario-selection.ps1)で、一覧と通常全件結果の名前/順序一致、計測と確認数の整合、入力コントロール/振替認証復帰/初期画面の単独起動、初期画面4枚のPNG出力、不正指定の非ゼロ終了と確認未実行をWindows CIで検証する。選択/計測の純粋部分は既存ClientのxUnit構成へ同じソースをリンクして11件検証し、リンク先のCI選択/fingerprintにも回帰2件を追加した。既存製品シナリオの確認条件・期待値、MainWindow、private参照は変更していない。

基点main `d6279d4`の[Tests 43958262780](https://github.com/k-o-a-b-c/monetail/actions/runs/43958262780)成功を変更前の根拠とした。ローカルは選択/計測の11件とPython77件が成功。初回の最小ビルドはテストの`using Xunit`不足で失敗し、補完後の11件で成功した。[初回CI 43976416296](https://github.com/k-o-a-b-c/monetail/actions/runs/43976416296)でClient1234件・API2140件・Node63件・Python77件、旧Windows/実DPAPI/計算照合/性能/Docker/配布スモークも成功。CI選択の共通処理を補足したため初回は全対象を確認し、APIテスト部分は20分8秒だった。

最終ソース`4eb0d3efaec92c0de3200529490168e07886e7ed`の[Tests 43980263516](https://github.com/k-o-a-b-c/monetail/actions/runs/43980263516)はWinUI製品/検証用x64 Release（各30件の既存WUI2011警告/0エラー）・起動・全35シナリオ/646確認（振替163＋既存483）・一覧と全件の照合・単独3シナリオ・shellのPNG4枚・不正指定拒否・計測が成功。追加変更と非関係のClient/API等は先行成功のfingerprintを再利用した。途中のWinUI実行1回は最終ガード反映に伴い自動中断し、成功とは数えていない。既存IME確認の一時失敗は今回再発していないが、原因解決とは扱わない。

| 最終CIの計測対象 | 経過時間 |
| --- | ---: |
| 全35シナリオ本体の合計 | 99.235秒 |
| connection-flow（全件内） | 15.128秒 |
| transfer-reauthentication（全件内） | 7.801秒 |
| input-controls（個別） | 2.577秒 |
| transfer-reauthentication（個別） | 7.703秒 |
| shell（個別・PNG4枚を含む） | 1.714秒 |

この1回のCI環境での参考値であり、性能SLAや一般的な短縮率を示さない。実Windows手動操作/物理IME/タッチ/高DPIや画像の表示品質の手動受入は未実施。製品コード・private参照の整理は対象外。

## WinUI：レシート401認証復帰

[ReceiptRecoveryScenario.cs](Monetail.WinUI.Smoke/ReceiptRecoveryScenario.cs)を`receipt-reauthentication`として追加。既存の2プロセス/各120秒の構成を維持し、専用入力側（互換用suite名`transfer-editor`）で実行する。新規/既存の同一モデル・親入力・未反映下書き・入力コントロール・人物CheckBox・旧版・未確定日時/数値・選択/表示位置、取消/連続401、別本人/接続先、権限喪失、取得失敗→手動再試行、遅延税率応答/終了時破棄、詳細GET/書込なしを確認する。HTTPの401を共通`RunAsync`へ渡す経路であり、**未接続の保存APIから発生する401の実経路や結果不明/409を検証済みとは扱わない**。

基点main `67c4ca1`の[push Tests 44002600030](https://github.com/k-o-a-b-c/monetail/actions/runs/44002600030)成功を変更前の根拠とする。ローカルは保存基準の最小1件と編集モデル/税率/Sessionの関連102件（重複あり）が成功。最初の試行はMSBuildの実行環境制約で停止し、プロセス間通信を許可した実行で成功した。Client全1,235件は[Tests 44006086987のClientジョブ](https://github.com/k-o-a-b-c/monetail/actions/runs/44006086987/job/133781823194)で成功し、以後のWinUI専用変更では再実行せず成功結果を参照した。最終ソース`53ad865`の[Tests 44009865747](https://github.com/k-o-a-b-c/monetail/actions/runs/44009865747)で、製品/検証用x64 Release（各29件の既存WUI2011警告/0エラー）、製品起動、全36シナリオ/684確認（専用入力201＋既存483、新しいレシート復帰30確認）が成功。既存の単独3シナリオ・一覧/全件照合・PNG4枚生成・不正指定拒否・計測も成功した。非変更API/DB等は既存CI選択/fingerprint規則で再利用する。

途中の検証で、新規試験の参照不足、共通操作で無効化した後の位置取得、未実体化入力欄のreadonly設定漏れ、未通知TextChangedによる未保存確認漏れを修正した。コピー試験は既存の振替試験と同じキーイベント処理待ちへ統一した。タイムアウト延長・判定条件の緩和は行っていない。既存IMEの一時失敗・ネイティブアクセス違反は最終CIで再発していないが、過去の原因解決とは扱わない。実APIの保存操作起因401・未完了書込の実経路・物理IME/実Windows手動操作/高DPI等は未検証。

## WinUI：画面表示と編集破棄の分離（第1段階）

[NavigationLifetimeScenario.cs](Monetail.WinUI.Smoke/NavigationLifetimeScenario.cs)を既存`transfer-reauthentication`に接続する。ログイン表示だけでは家計簿/認証コンテキストを変更しないこと、未承認遷移のガード、記録ホーム一覧のリセット/家計簿解除と振替・レシート編集の独立性、同じモデル/入力コントロール/未確定文字列/旧版の保持、明示破棄、詳細GET/保存なしを確認する。レシート401復帰を新規実装するテストではない。

既存の連続401/別本人/別接続先/マスタ失敗と再試行/結果不明/409/通常離脱、および人物・科目・支払方法・参加者の復帰シナリオは削除・緩和しない。基点main `44ebeed`の[push Tests 43984012725](https://github.com/k-o-a-b-c/monetail/actions/runs/43984012725)成功を変更前の根拠とし、非変更Client/API/DB等はCIのfingerprint規則に従って再利用する。ソース`eeac84a`の[Tests 43995028903](https://github.com/k-o-a-b-c/monetail/actions/runs/43995028903)で製品/検証用x64 Releaseビルド（各30件の既存WUI2011警告/0エラー）、製品起動、全35シナリオ/654確認（振替171＋既存483、今回8確認追加）が成功。連続401・通常離脱・結果不明・409・各管理画面の復帰を含む。既存の単独3シナリオ、一覧/全件の照合、PNG4枚生成、不正指定拒否・計測も成功した。再実行はなく、既存IME確認の一時失敗・ネイティブアクセス違反は今回再発していないが、過去の原因解決を意味しない。ローカルは`git diff --check`成功。実Windows手動操作・物理IME/タッチ/高DPI等は未検証。

## WinUI：振替401後の既存画面復元（第4-B）

[TransferRecoveryScenario.cs](Monetail.WinUI.Smoke/TransferRecoveryScenario.cs)を追加。実Workspace/Transport・合成HTTP・実OAuthループバックを使い、新規/更新/流用/削除401、ログイン取消、同じWindow/コントロール/モデル/Draftと不変要求の同一性、同じ本人/接続先だけの復帰、マスタ再取得、詳細GET/自動保存なし、同じJSONの明示再送を確認する。未確定日時/金額・補完由来・文字選択/スクロール、マスタ503→一時readonly→再試行・名称だけ更新、別本人/接続先での破棄、Viewer/人物未対応Editor/保存済み片側が他人所有/参加解除での書込禁止、結果不明再送中401、進行中候補の中断・遅延応答破棄・終了時のメモリ破棄も対象。通常ナビゲーションの未保存保護と第4-A等の既存回帰は省略しない。

PR #187の連続401修正では、人物再取得中の再401を新規/更新/流用/削除で確認し、最初の復帰情報と未完了要求の同一性・入力/選択由来/旧版・同じJSONの明示再送を検証する。支払方法再取得中の再401は、未確定日時/金額・補完由来・元のWindow/コントロールを保持し、最初のフォーカス/文字選択/スクロール位置へ通常復帰する経路と、マスタ503後の手動再試行経路を検証する。ソース`09e665ccb4db708f3f9565fad8aebe458b168554`の[Tests 43954227708](https://github.com/k-o-a-b-c/monetail/actions/runs/43954227708)が成功。製品/検証用x64 Release（各30警告/0エラー）・起動・振替163確認＋既存483確認＝646確認が成功した。既存の409最新採用、ログアウト/家計簿切替保護も含み、既存IME確認の一時失敗は今回は再発しなかった。原因は未特定のままとし、解決済みとは扱わない。Client/API/DB等の非変更部分は従来の成功fingerprintを再利用した。

Clientは既存baselineから保存済みの両支払方法IDを読み取るだけの2プロパティを追加。入力変更/最新採用の前後で権限評価の基準を保つ[回帰1件](Monetail.Windows.Client.Tests/TransferEditorStateTests.cs)が成功した。ローカルReleaseは最小1件→編集/版/キャッシュの関連104件、追加の認証Session20件が成功（最小との重複あり、失敗/skipなし）。変更前のmain `084787d3c9b1bf7ebe3102b4dc5360cc258690f1`の[push Tests 43895342659](https://github.com/k-o-a-b-c/monetail/actions/runs/43895342659)を採用し、非変更部分はCI選択/fingerprintに従って再利用する。

ソース`af2ae3e7456fe5a41af6eb6785d2fd9b8ee0c5d1`の[Tests 43907437675](https://github.com/k-o-a-b-c/monetail/actions/runs/43907437675)が成功。製品/検証用x64 Release（各30警告/0エラー）・製品起動・振替149確認＋既存483確認＝全632確認が成功した。Client全1223件はClient最終変更を含むソース`aef66ce`の[Tests 43902209985・Clientジョブ](https://github.com/k-o-a-b-c/monetail/actions/runs/43902209985/job/133421706208)で成功し、以後のWinUI専用変更では成功fingerprintを再利用した。API/DB/旧Windows/性能等の非変更部分もCI選択条件に従って再利用した。

検証途中、[Tests 43906321524](https://github.com/k-o-a-b-c/monetail/actions/runs/43906321524)の既存レシート科目メモの合成IME Enter確認が1回失敗した。該当する製品/試験コードと判定条件は変更せず、最終CIでは成功したが原因解決とは扱わない。新規試験ではレイアウト確定前の末尾位置を期待していた誤りを途中位置での退避/復帰比較に修正し、終了時の状態破棄は最後のWindow閉鎖による検証プロセスの正常終了と分離して確認する。タイムアウト延長・既存確認の削除/緩和は行っていない。

入力値の復元用コピー/シリアライズ/ディスク保存や振替詳細の追加GETは導入していない。復帰メタデータは本人/家計簿/接続先とフォーカス/文字選択/スクロールのみ。通常のログアウト/家計簿切替は既存離脱保護を通し、401専用の画面切替だけを許可する。DB/API/残高連動/改行貼り付け/外部ライブラリは変更なし。WinAppCLIはLinuxで利用できず、既存標準WinUIコントロールとWindows CIで検証する。実API/NAS・実パスキーブラウザー、物理IME/タッチ、高DPI/ハイコントラスト等の手動受入は未実施。

## WinUI：振替409競合からの復旧（第4-A）

[TransferConflictScenario.cs](Monetail.WinUI.Smoke/TransferConflictScenario.cs)で、PUT/DELETEの409後の2択、旧入力/版/応答latestの保全、初期2択からの採用と警告欄からの採用、破棄確認取消、読み取り専用での実Ctrl+C・スクロール・Ctrl+Enter禁止、8項目の明示採用と追加GETなし、新要求ID/最新revisionでの更新・削除を確認する。合成HTTPは実Workspace/Transportを通し、APIの版照合を模倣して行版が違えば409を返す。request_conflict・403・404・401・結果不明の表示分離と最新採用非表示は[既存の安全性試験](Monetail.WinUI.Smoke/TransferSafetyScenario.cs)でも検証する。通常CRUD・候補/補完・フィルター・レシート等の回帰は省略しない。

既存Clientモデル/DTOは変更せず、ローカルReleaseで最新採用の最小1件と編集モデル/受信revision/権限の関連106件が成功（重複あり、失敗/skipなし）。変更前は基点main `362d31acc5f910bbe6d9ae65ab177bbcecd954cd`の[push Tests 43879330803](https://github.com/k-o-a-b-c/monetail/actions/runs/43879330803)を採用し、Client全件/API/Docker/旧Windows/性能等はCI選択/fingerprintに従って成功結果を再利用する。

第4-A当初ソース`bd1be4f82eac865d87f72704f1c12122050e3b99`の[Tests 43883023127](https://github.com/k-o-a-b-c/monetail/actions/runs/43883023127)が成功。WinUI製品/検証用x64 Releaseビルドは各30警告/0エラー（追加CS8602なし）、製品起動、振替側95確認＋既存483確認＝全578確認が成功した（追加24確認）。初回スモークは競合前の未ロード入力への設定で停止し、最小成立の準備だけ既存のLoaded/フォーカス確認へ合わせた。待機上限・判定条件は緩和していない。既存x:Bind等の警告は残る。

［最新を読み込む］のTab順修正では、PUT/DELETE競合後の実Tab（戻る→警告直後の最新読込→日付）とShift+Tabの逆移動、移動だけでは採用・送信しないことを追加確認する。通常編集・最新採用後は日付→時刻→振替名→元→先→金額→メモ→更新の実Tab順と未変更判定を維持する。ソース`aa9ea172bc90f9abf26ed6056542cafaef660a07`の[Tests 43889435986](https://github.com/k-o-a-b-c/monetail/actions/runs/43889435986)で製品/検証用x64 Release（各30警告/0エラー）・起動・振替99確認＋既存483確認＝全582確認が成功。非変更のClient等は前回成功結果を再利用した。

API/DB/Client契約・外部ライブラリの変更なし。当該第4-Aの対象外だった401後の編集復元は上記第4-Bで接続済み。残高キャッシュ連動は対象外。実API/NAS、物理IME/タッチ、高DPI/ハイコントラスト/スクリーンリーダーの実Windows手動受入は未実施。LinuxでWinAppCLIは利用できず、既存標準Button/ContentDialog/InfoBarとWindows CIで照合する。旧版の競合MessageBox/再読込通知から、新版正本の応答latest採用へ変更する。

## WinUI：振替専用入力・候補補完・保存（第3工程）

[TransferEditorRulesTests.cs](Monetail.Windows.Client.Tests/TransferEditorRulesTests.cs)・[TransferSavedListTests.cs](Monetail.Windows.Client.Tests/TransferSavedListTests.cs)にClient26件を追加。ローカルReleaseは最小2件→追加と既存一覧の関連56件→不一致条件だけの解除1件が成功（段階間に重複あり）。Admin/Editor/Viewer・人物未対応、人物/支払方法の期間両端、入力不変、保存後の共通年月と独立AND/名前条件調整、店名下書き/確定値の保全、先頭300件での発見/未発見とfocus・cursor・先頭復帰・404の1回復旧・旧応答破棄を検証する。Clientソース/テストを含む`10e57346530cf457ba565c388115ea1f1984fe17`の[Tests 43835870141](https://github.com/k-o-a-b-c/monetail/actions/runs/43835870141)で全1218件（失敗/skipなし）が成功。

[入力とCRUD](Monetail.WinUI.Smoke/TransferEditorScenario.cs)・[結果不明/権限/競合](Monetail.WinUI.Smoke/TransferSafetyScenario.cs)・[候補/実キー](Monetail.WinUI.Smoke/TransferSuggestionScenario.cs)・[保存後focus/詳細取得](Monetail.WinUI.Smoke/TransferFocusScenario.cs)を追加。合成HTTPでも実際のWorkspace/Transportを通し、POST/PUT/DELETE、確認取消と警告統合、読み取り専用のコピー/操作非表示、応答喪失後の同一JSON再送と二重登録防止、401/403/404/409の入力/旧版保全、候補プレビュー/Esc/Tab/Enter・IME保護・旧補完破棄、未保存離脱、保存行の途中開始/先頭復帰と詳細GETの二重起動/取消を確認する。

`run.ps1`は製品を一度起動確認し、新しい振替入力と既存全画面を別の検証プロセスで実行する。どちらも従来の120秒上限を維持し、既存回帰を省略しない。実画面試験でスクロール境界のTab順と`TransferWriteUncertainException`受信漏れを修正。合成操作も実フォーカス/入力通知後の状態・Unspecifiedの秒精度日時に整え、既存の判定条件を緩和していない。保存準備では利用者入力の通知を配送してから値を検証し、未確定候補の表示文字列を業務入力へ読み戻さない。

レビュー前ソース`5efc9919f0661e45b44aef3f152740d120cef983`の[Tests 43845775781](https://github.com/k-o-a-b-c/monetail/actions/runs/43845775781)が成功。製品/検証用のWinUI x64 Releaseビルド（30警告/34警告・各0エラー）、製品起動、振替入力57確認＋既存483確認＝全540確認が成功した。当初残っていたnullable参照警告2箇所は下記レビュー対応で解消。本作業の実行で0xC0000005は観測していないが、過去のアクセス違反の根本原因解消を証明するものではない。

変更前は基点main `6958ae9a5ed242fcf6eb3f52111b354ca1e21f70`の[push Tests 43828563302](https://github.com/k-o-a-b-c/monetail/actions/runs/43828563302)成功を採用。Clientと影響外のAPI/Docker/旧Windows/性能等はCIの選択/fingerprintに従って成功記録を再利用し、影響外の全件をローカルで再実行していない。API/DB/Migration・外部ライブラリは変更していない。

実API/NAS、実Windowsの物理IME・タッチ・高DPI/ハイコントラスト等の手動確認は未実施。実キー/マウス注入は合成CIの確認であり、実機手動の受入済みとは扱わない。LinuxではWinAppCLIを使用できず、[公式GalleryのAutoSuggestBox](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Samples/AutoSuggestBox/AutoSuggestBoxPage.xaml.cs)のイベントと既存SDKによるWindowsビルドで照合。残高連動は[実装境界](../docs/ledger-transfer-ui.md#第3工程の実装境界)のとおり次工程。

### PR #185のレビュー対応

同一支払方法のエラーを振替先直下へ分離し、選択保持・指定文言・修正時解除・保存ボタン/Ctrl+Enterの禁止を実画面で検証する。Windowの明示的なnull検証でCS8602を解消し、API正本と旧版比較資料の実装状態を同期した。

メモは実クリップボード/Ctrl+VでCR・LF・CRLFを検証する。当初の全文保持要求に対し、標準1行TextBoxは1行目に切り詰め、Paste捕捉後のText設定でも同じ結果となった（[初回](https://github.com/k-o-a-b-c/monetail/actions/runs/43853482727)、[捕捉後](https://github.com/k-o-a-b-c/monetail/actions/runs/43854108283)の失敗）。2026-11-16の利用者承認に従い独自Paste処理を撤去し、標準動作・元クリップボード不変・貼り付けだけで送信しないこと・全削除時のnullを検証する期待値へ変更した。追加試験は明示破棄で編集離脱を完了し、次の合成Windowへ閉鎖確認を持ち越さない。既存の実ダブルクリック試験はWindowをOSの作業領域内へ配置し、対象の画面座標も確認する（待機上限・成功条件は維持）。TextBox全体の改行除去方式は[保留](../docs/59-input-controls-design.md#貼り付け時の改行処理保留)。API/Clientへ直接渡す改行入りメモは引き続き拒否する。

Clientには同一IDとCR/LF/CRLFの入力/保存済みID/版の保全4件を追加。ローカルは最小4件と関連144件が成功（重複あり）。Clientソース/テストが同じ`06effd34464062ebb185f42dd8f05371627049e4`の[Clientジョブ133253929662](https://github.com/k-o-a-b-c/monetail/actions/runs/43854108283/job/133253929662)で全1222件が成功（親run全体は当時の貼り付け試験で失敗）。

レビュー修正の最終ソース`0d75d4b84c56f910c71d5b211ed9a148f047ee06`の[Tests 43858400897](https://github.com/k-o-a-b-c/monetail/actions/runs/43858400897)が成功。製品/検証用WinUI x64 Releaseビルドは各30警告/0エラー（対象CS8602は0件）、製品起動と振替入力71確認＋既存483確認＝全554確認が成功した。既存のx:Bind等の警告は残る。実API/NAS・物理IME/タッチ/高DPI/ハイコントラストの手動確認は未実施。

## WinUI：振替一覧・記録ホームの共通フィルター

導入時はClientに新規27件を追加した。現在の表示状態は[TransferListViewModelTests](Monetail.Windows.Presentation.Tests/TransferListViewModelTests.cs)等へ移管し、共通検索契約は[RecordSearchStateTests](Monetail.Windows.Client.Tests/RecordSearchStateTests.cs)等へ残す。共通条件と独立した店名/振替名の下書き、年月境界、0/1/99/100/101/201件、API順/カーソル/選択、300件補充上限・続行、二重取得、手動再試行、invalid_cursorの1回復旧、検索変更・中断/復帰・旧応答破棄を検証する。ローカルReleaseは最小2件→関連30件→追加の旧検索応答1件が成功（段階間に重複あり、失敗/skipなし）。関連検証の初回はコピー元型名が残ったコンパイルエラーで未実行となり、既存共通検索モデルへ修正した。

[TransferListScenario.cs](Monetail.WinUI.Smoke/TransferListScenario.cs)・[TransferNavigationScenario.cs](Monetail.WinUI.Smoke/TransferNavigationScenario.cs)で初期配置、6列表示、共通AND条件、独立した文字列検索、狭幅1行/横スクロール/画面外への標準フォーカス移動・UIA、一覧スクロールとの独立、0件/読込/失敗/再試行、401/404、初回と追加取得の中断・同一条件での復帰/選択/非先頭スクロール保持を追加。振替/残高の登録入口は無効のままで、振替一覧GETだけを呼ぶ。スモーク作成中の失敗は、検証プロジェクトの新規ソース参照漏れ、名前のない既存ボタンの検索方式、通信完了直後の描画未確定での寸法確認を修正した。寸法は`UpdateLayout()`後に確認し、判定条件やタイムアウトは緩和していない。

変更前は基点main `1d35a04a6d4fa030291eeab1bfc7a55aea8d2182`の[push Tests 43776723558](https://github.com/k-o-a-b-c/monetail/actions/runs/43776723558)成功を採用。Clientソース/テスト`d40771254b07e32d457a1e4c538574ff1b596fb0`の[Tests 43816241940](https://github.com/k-o-a-b-c/monetail/actions/runs/43816241940)で全1192件（失敗/skipなし）が成功し、最終ソース`f706ad3a61fa05765fae1844b1bda0c6bb135bb2`の[Tests 43817480270](https://github.com/k-o-a-b-c/monetail/actions/runs/43817480270)でWinUI製品/検証用x64 Releaseビルド・製品起動・既存449確認を含む全483確認が成功。両ビルドは各30警告/0エラー。Client・API・Docker・旧Windows・性能等はCIの選択/fingerprintに従って成功記録を再利用した。API/DB/外部ライブラリは変更せず、実運用データへ操作していない。

実API/NAS接続、実Windowsでのマウス/タッチ/Tab、Light/Dark/ハイコントラスト・高DPIの手動確認は未実施（Darkと標準フォーカス/UIAは合成スモークで確認）。Linux環境にはWinAppCLIがなくWindows用実行物も使用できないため、標準コントロールを既存SDKのWindowsビルド/スモークで検証した。振替入力・保存後focus・残高API・スプリッター/比率保存は後続。

## Windows Client：振替編集状態モデル

PR #182追加修正：空白だけの振替名では補完を開始せず、入力や選択を変更しない。null/空文字・ASCII空白/改行・全角空白・NBSP・EM SPACE/NNBSP・混在空白、文字の前後のUnicode空白を保持する回帰9ケースを追加。ローカルReleaseで編集モデル全94件が成功（失敗/skipなし）。`string.IsNullOrWhiteSpace`を使い、Trimや正規化は追加していない。

修正ソース`22204798f8039e0c114eaf2b56ffd217b9c1e776`の[Tests 43771823054](https://github.com/k-o-a-b-c/monetail/actions/runs/43771823054)でClient全1165件・実DPAPI3件、WinUI製品/検証用Releaseビルド・製品起動・既存449確認が成功。両WinUIビルドは各30警告/0エラー（警告対象のXAML等は未変更）。修正前はPR先端`2d3fd52`の[Tests 43770644447](https://github.com/k-o-a-b-c/monetail/actions/runs/43770644447)成功を採用し、影響外の検証は既存の選択・fingerprintで成功記録を再利用した。API・JSON契約は変更していない。

[新規/既存](Monetail.Windows.Client.Tests/TransferEditorStateTests.cs)・[保存準備/入力保全](Monetail.Windows.Client.Tests/TransferEditorSaveTests.cs)・[補完](Monetail.Windows.Client.Tests/TransferEditorAutofillTests.cs)・[競合/読み取り専用](Monetail.Windows.Client.Tests/TransferEditorConflictTests.cs)に85ケースを追加。6項目/ID/版の保持、未選択、未確定日時/金額の生文字列、メモnull/空文字/空白、値を戻した際の未保存解消と由来だけの変更の除外を確認する。手動/保存済み選択の保護、自動値の再補完/両nullでの解除、条件変更して戻した場合/別モデル/新しい要求文脈の旧応答拒否、関係しない時刻/金額/メモ入力中の保全も検証。登録/更新/流用Snapshot、削除ID/版、日時警告、最新8項目の確認付き採用、403/404から競合採用による書込復帰の禁止、各失敗時の状態保全を確認する。

ローカルReleaseは最小2件→新規84件→追加した未入力保全1件と既存振替書込28件の計29件が成功（段階間に重複あり、失敗/skipなし）。基点main `14062ba0250f558702164d0dacc7b54d17f6cf2f`の[push Tests 43768604262](https://github.com/k-o-a-b-c/monetail/actions/runs/43768604262)成功を変更前の根拠とする。入力検証は既存書込の処理を内部共用し、要求生成・requestId発行はしない。HTTP/保存・認証復帰・UI/入力確定・マスタ取得/残高更新は後続。API/JSON/DB/Migration・WinUI/XAML・外部ライブラリは変更していない。

ソース`47a3d1c538ed11b873d03edb4616de36143200a3`の[Tests 43770181840](https://github.com/k-o-a-b-c/monetail/actions/runs/43770181840)でClient全1156件（失敗/skipなし）・実DPAPI3件・Python75件、WinUI製品/検証用Releaseビルド・製品起動・既存449確認が成功。両WinUIビルドは各30警告/0エラー（警告対象のXAML等は今回未変更）。影響外のAPI/Docker/旧Windows/性能等はCIの選択・fingerprintに従って成功記録を再利用し、ローカル全件検証の重複実行は避けた。変更文書のローカル参照先187件と差分検査も成功。実API/NAS接続・実Windowsの手動操作と、後続の振替WinUIからのモデル接続は未検証。

## Windows Client：振替書込3種類

PR #181の追加修正：[TransferRevisionTests.cs](Monetail.Windows.Client.Tests/TransferRevisionTests.cs)の16ケースで詳細GET・PUT/DELETEの409 latestがUUID v4を受理し、v7/v1・不正variant・空UUIDを拒否すること、振替/支払方法のv7 IDは引き続き受理することを確認。ローカルReleaseで新規16件と既存振替読取/書込273件が成功（失敗/skipなし）。受信revisionの共通検証だけを強化し、API・JSON契約は変更していない。

修正ソース`d4d5aa096e8cc5e8568258393808b0bffd4140bf`の[Tests 43759628036](https://github.com/k-o-a-b-c/monetail/actions/runs/43759628036)でClient全1071件・実DPAPI3件、WinUI製品/検証用Releaseビルド・製品起動・既存449確認が成功。変更前はPR先端`4078950`の[Tests 43758316174](https://github.com/k-o-a-b-c/monetail/actions/runs/43758316174)成功を採用し、影響外のAPI/Docker/旧Windows/性能等は既存CIの選択・fingerprintに従って成功記録を再利用した。両WinUIビルドは各30警告/0エラー（警告対象のXAML等は未変更）。実API/NAS接続・実Windowsの手動操作は未検証。

[基本送信](Monetail.Windows.Client.Tests/TransferWriteTests.cs)・[入力](Monetail.Windows.Client.Tests/TransferWriteInputTests.cs)・[成功/競合/エラー](Monetail.Windows.Client.Tests/TransferWriteResponseTests.cs)・[再送/認証/通信](Monetail.Windows.Client.Tests/TransferWriteRetryTests.cs)に合成HTTPの128ケースを追加。POST/PUT/DELETEのURL/項目/UUID v4、ローカル秒精度・Int32・UTF-16/空白/全改行文字、空本文/201/204とLocationの同一家計簿・D形式・オリジン・生パス検証、固定要求/別操作や家計簿への流用拒否、409最新8項目と必須null/型/対象ID・request_conflict、403/404権限と429/503を確認する。認証更新後の本文不変、アカウント変更/再度401、通信断/タイムアウト/キャンセル/不正成功後の明示再送、振替409を含む64KiB境界のContent-Lengthあり/なしも検証する。

ローカルReleaseは最小2件→追加123件（122成功/Location1件失敗）→修正と強化の関連23件→既存振替読取・レシートを含むClient全1055件が成功（段階間に重複あり、最終の失敗/skipなし）。失敗は通常のLocation読取がURIの`/x/../`を正規化していたため、生ヘッダーを検証して修正した。既存テストの判定は緩和していない。基点main `31d2e2a89091556f7afc05af7e715f8bfe0b75bf`の[push Tests 43756692482](https://github.com/k-o-a-b-c/monetail/actions/runs/43756692482)成功を変更前の根拠とする。API/DB/Migration・WinUI/XAML・外部ライブラリは変更せず、実運用データへ操作していない。実API/NAS接続・実Windows手動操作は未検証。編集モデル・保存後の一覧/残高キャッシュ失効は後続工程。

ソース`30c3f1feef0c83d2cea7c5c6ffcf35d8f6784300`の[Tests 43757927110](https://github.com/k-o-a-b-c/monetail/actions/runs/43757927110)でClient全1055件（失敗/skipなし）、実DPAPI3件、Python75件、WinUI製品/検証用Releaseビルド・製品起動・1シナリオ449確認が成功。両WinUIビルドは各30警告/0エラー（警告対象のXAML等は今回未変更）。API/Docker/旧Windows/性能等は既存の選択・fingerprintに従って成功記録を再利用し、影響外の全件検証は再実行していない。変更文書のローカル参照先161件と差分検査も成功した。

## Windows Client：振替読取4種類

[基本応答](Monetail.Windows.Client.Tests/TransfersWorkspaceTests.cs)・[クエリ](Monetail.Windows.Client.Tests/TransferReadQueryTests.cs)・[厳密JSON/境界](Monetail.Windows.Client.Tests/TransferReadBoundaryTests.cs)・[ストリーム/サイズ](Monetail.Windows.Client.Tests/TransferReadStreamingTests.cs)・[業務エラー](Monetail.Windows.Client.Tests/TransferReadErrorsTests.cs)・[認証/通信](Monetail.Windows.Client.Tests/TransferReadAuthenticationTests.cs)に合成HTTPの145ケースを追加。4種類のURL/省略と空文字/元表記/不透明cursor/focus、7/8項目と必須NULL・不正型/重複プロパティ/ID、ローカル秒精度・Int32/UTF-16境界、100件・元文字列候補と重複・両null補完を検証する。認証更新時の同一GET、別アカウント/家計簿の分離、403/404権限情報と429/503、通信断/実10秒タイムアウト/利用者キャンセルも確認する。64KiB超の一覧/詳細はストリームで成功し、候補/補完/エラーはContent-Lengthあり/なしとも64KiB制限を維持する。

ローカルReleaseは最小2件→追加141件→強化した境界8件→既存レシートを含むClient全927件が成功（段階間に重複あり、最終の失敗/skipなし）。関連試験の初回はテスト準備のJSON文字列のC#構文エラーで未実行となり、JSON生成へ直して実行した。新規ケース数はテスト検出でも145件と照合。既存の件数表778件とは別に、現在の全件実行結果927件で表を更新した。レシートClient・API/DB・WinUIは変更せず、自動再検索/補完適用は追加していない。実API/NAS接続・実Windowsの手動操作は未検証。

基点main `6e27aa9746716723732d842d7d071ca101d34e3a`の[push Tests 43748582010](https://github.com/k-o-a-b-c/monetail/actions/runs/43748582010)成功を変更前の根拠とする。ソース`9c22ff5e6ef6fa3b701bb8733e310dd0efd5731b`の[Tests 43750933370](https://github.com/k-o-a-b-c/monetail/actions/runs/43750933370)でClient全927件・実DPAPI3件・Python75件、WinUI製品/検証用Releaseビルド・製品起動・1シナリオ449確認が成功。両WinUIビルドは各30警告/0エラー（警告対象のXAML等は今回未変更）。API/Docker/旧Windows/性能等は既存の選択・fingerprintに従って再実行せず成功記録を再利用した。変更文書のローカル参照先153件・差分検査も成功。既存のテスト判定・タイムアウトは緩和していない。

## 振替自動補完GET

[基本応答](Monetail.Api.Tests/TransferAutofillTests.cs)・[選択/期間](Monetail.Api.Tests/TransferAutofillSelectionTests.cs)・[入力](Monetail.Api.Tests/TransferAutofillInputTests.cs)・[権限/途中変更](Monetail.Api.Tests/TransferAutofillSecurityTests.cs)・[家計簿分離](Monetail.Api.Tests/TransferAutofillIsolationTests.cs)・[SQL計画](Monetail.Api.Tests/TransferAutofillQueryPlanTests.cs)に実HTTP/本番DI/隔離SQLiteの86件を追加。完全一致/表記ゆれ非一致、更新UTC/IDの順序と取引日時・作成日時・使用回数の非優先、無効履歴のスキップと同一履歴の両側、指定元だけを応答しないこと、両支払方法/所有人物の期間境界、Editorの双方所有、全権限・指定元の403/404/期間外の両null、厳密クエリ/UTF-16/日付/UUIDを確認する。

候補取得後と指定元確認中の役割/人物対応変更、認証失効/参加解除を検証し、書込可能な状態が続いても古い候補を両nullにして再検索しないことをSQL回数でも確認。空履歴・期間外指定元でも最後の権限確認を省略しない。共有120回枠、保存障害の情報非公開、DB/監査/版/再送記録の非変更、他家計簿の同一振替IDと指定元の存在情報非公開を検証する。

1,002履歴（新しい1,001件が使用不可）の実SQLを捕捉し、Admin/Editorと指定元あり/なしの4条件で古い有効な履歴を取得。EXPLAIN QUERY PLANで既存索引のSEARCH・全表SCAN不使用・更新UTC順の一時B-treeを確認する。LIMIT 1は有効条件を適用した後の結果1組だけに使用し、履歴探索の件数上限は追加しない。

ローカルReleaseは最小2件→追加86件→既存振替CRUD/候補/レシート補完・共有権限処理の代表12件が成功（段階間に重複あり、失敗/skipなし）。変更前は基点main `8a6fd1533bb58aa2c7e4be4aacb673b989c038a7`の[push Tests 43708775155](https://github.com/k-o-a-b-c/monetail/actions/runs/43708775155)成功を採用。全APIのローカル重複実行は行わず、影響外のClient/WinUI/旧Windows/性能等はCIの既存選択・fingerprintに従って成功記録を再利用する。DB/索引/Migration・Client/WinUIは変更せず、実運用DBには操作していない。実NAS/実Windows接続・実運用データ規模の性能は未検証。

ソース`cc33f27aa4d28be0373238c60021eeaa1b14afd8`の[Tests 43712768379](https://github.com/k-o-a-b-c/monetail/actions/runs/43712768379)でAPI全2140件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合、Dockerビルド/起動/サーバー/配布アーカイブ、更新・DB/PFX等のバックアップ/復元保全が成功。変更文書のローカル参照先ファイル445件・差分検査も成功した。

## 振替名候補GET

[基本応答](Monetail.Api.Tests/TransferSuggestionsTests.cs)・[検索](Monetail.Api.Tests/TransferSuggestionsSearchTests.cs)・[入力](Monetail.Api.Tests/TransferSuggestionsInputTests.cs)・[アクセス](Monetail.Api.Tests/TransferSuggestionsSecurityTests.cs)・[SQL計画](Monetail.Api.Tests/TransferSuggestionsQueryPlanTests.cs)に実HTTP/本番DI/隔離SQLiteの50件を追加。q省略/空/部分一致/0件、100/101候補とhasMore、201同名履歴の重複除去と古い一致の検索、MAX更新UTC/元文字列BINARY順（BMP外文字を含む）、英数字だけの同一視と元表記の保持、かな/記号/互換文字/その他の文字種の非同一視、空白/SQLワイルドカードの文字どおりの検索、UTF-16境界・不正クエリを検証する。全ロール/人物未対応、同一振替IDがある別家計簿、401/404/共有120回枠、取得後の認証/参加/役割/人物対応変化、保存障害、並行検索、DB非変更も確認する。役割/人物対応が変わっても参加・参照権限を維持する場合は200、参加解除は404、認証失効は401とする。

実際にAPIが発行したSQLを捕捉し、1,001振替のqなし/ありの両方でEXPLAIN QUERY PLANを検証。既存`ix_ledger_transfers_transaction`による家計簿検索、GROUP BY/ORDER BYの一時B-tree、集約後のLIMIT 101・OFFSET/COUNT不使用を確認する。新しい索引・列・独自の性能SLAは追加しない。

ローカルReleaseは最小2件→追加50件→既存振替GET/POST/PUT/DELETEとレシート候補の代表5件が成功（段階間に重複あり、失敗/skipなし）。変更前は基点main `b745e047d35ede0181d65d1bbab40e62cac569f0`の[push Tests 43702421867](https://github.com/k-o-a-b-c/monetail/actions/runs/43702421867)成功を採用。全APIのローカル重複実行は追加せず、影響外のClient/WinUI/旧Windows/性能等はCIの既存選択・fingerprintに従って成功記録を再利用する。自動補完・Client/WinUI・DB/索引/Migrationは変更せず、実運用DBには操作していない。実NAS/実Windowsからの接続・実運用データ規模の性能は未検証。

ソース`0b6288e43b6982d5aac9ae77a676bb30bb2553f8`の[Tests 43705873585](https://github.com/k-o-a-b-c/monetail/actions/runs/43705873585)でAPI全2054件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合、Dockerビルド/起動/サーバー/配布アーカイブ、更新・DB/PFX等のバックアップ/復元保全が成功。変更文書のローカル参照108件・差分検査も成功した。

## 振替DELETE：削除

[正常系](Monetail.Api.Tests/TransferDeleteTests.cs)・[入力](Monetail.Api.Tests/TransferDeleteInputTests.cs)・[権限](Monetail.Api.Tests/TransferDeleteAccessTests.cs)・[再送](Monetail.Api.Tests/TransferDeleteReplayTests.cs)・[原子性/競合](Monetail.Api.Tests/TransferDeleteSafetyTests.cs)に実HTTP/本番DI/隔離SQLiteの72件を追加。厳密な2項目JSON/UUID v4、全ロール/両所有権/家計簿分離、版競合と最新8項目の公開前権限確認、実DELETE時の原子的版条件、同時DELETE/PUT、同一/異内容/別操作者/操作/対象/版/家計簿の再送、再起動・所有支払方法削除/人物対応解除後の成功再送、両書込段階の失敗・権限/認証/対応/所有権変化での全表rollback、CSRF/Origin/共有20回枠を検証する。削除後の詳細404/一覧除外と、他振替/人物/支払方法/レシート等の保持を確認。未実装ルートの既存試験はDELETEだけを実装済みに変更し、詳細POSTの拒否は維持する。

ローカルReleaseは最小2件→追加72件→対象外振替の保持を強化した正常ケースと既存GET/POST/PUTの代表7件が成功（段階間に重複あり、失敗/skipなし）。全APIのローカル再実行は行わず、下記の最終CIで確認した。影響外のClient/WinUI/旧Windows/性能等は既存の選択・fingerprintに従って成功記録を再利用する。実運用DBには操作していない。実NAS/実Windows接続・実運用データでの検証は未実施。候補/補完・Client/WinUI・DB/索引/Migrationは変更していない。

基点main `4dd0414b8834b2ceafc7f7b6e7f5be305c1bc507`の[push Tests 43697002173](https://github.com/k-o-a-b-c/monetail/actions/runs/43697002173)成功を変更前の根拠とする。ソース`0e3b0bc4d3e13be047ae760a2ae06955cbcbd89a`の[Tests 43697845272](https://github.com/k-o-a-b-c/monetail/actions/runs/43697845272)でAPI全2004件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合、Dockerビルド/起動/サーバー/配布アーカイブ、更新・DB/PFX等のバックアップ/復元保全が成功。変更文書のローカル参照129件と差分検査も成功した。

## 振替PUT：更新

[TransferUpdateTests.cs](Monetail.Api.Tests/TransferUpdateTests.cs)・[入力](Monetail.Api.Tests/TransferUpdateInputTests.cs)・[権限](Monetail.Api.Tests/TransferUpdateAccessTests.cs)・[安全性](Monetail.Api.Tests/TransferUpdateSafetyTests.cs)・[再送](Monetail.Api.Tests/TransferUpdateReplayTests.cs)に102件を追加。既存の本番DI/実HTTP/隔離SQLite fixtureを使い、8項目の厳密JSON、UUID v4/UTF-16/ローカル日時/金額/メモ、変更前後4か所の所有権、各ロール、別家計簿、両マスタの期間端点を検証する。通常更新の監査2項目変更/作成監査保持、別操作者、無変更時の振替UPDATE禁止・空文字/null同値、原子的な旧版条件、最新8項目、競合時の非公開、並行保存/同時再送、異内容/版/対象/操作者/操作の要求ID競合、再起動後再送、権限/認証/人物対応/所有権/期間変化と途中失敗の全表ロールバック、既存20回共有枠・CSRF/Originを確認する。未実装ルート保護の当時の試験はPUTだけを実装済みに変更した。DELETE実装後は上記の削除試験へ置き換え、詳細POSTの拒否を維持する。

ローカルReleaseは最小2件→追加85件（83成功/2失敗）→失敗2件と新規17件（18成功/1失敗）→該当1件成功。初回2失敗は試験準備の人物作成応答参照を既存の一覧形式へ修正。次の1失敗は旧空文字メモの模擬値が現行CHECKで拒否されたため、既存GET試験と同じくテスト準備専用接続だけでCHECKを一時無効化し、API接続では制約を維持した。製品側の条件や既存試験は緩和していない。段階間には重複があり、既存POST/詳細GET/未実装ルート保護の代表7件も成功。全APIのローカル再実行は行わない。

ソース`508664dfc1e64747ec0dd8b96da5280200acd303`の[Tests 43692823414](https://github.com/k-o-a-b-c/monetail/actions/runs/43692823414)でAPI全1932件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合が成功。Dockerビルド/起動/サーバー/配布アーカイブ、更新・DB/PFX等のバックアップ/復元保全も成功。変更文書のローカル参照122件・差分検査を確認した。

変更前は基点main `4b3eb60c9833603eff7213274c71eacd96a5a0a2`の[push Tests 43691688172](https://github.com/k-o-a-b-c/monetail/actions/runs/43691688172)成功を採用。影響外のClient/WinUI/旧Windows/性能等はCIの既存選択・fingerprintによる成功記録再利用に従う。実運用DBは変更せず、実NAS/実Windowsからの接続・実運用データでの検証は未実施。DELETE/候補/補完・Client/WinUI・DB/索引/Migrationは変更していない。

## 振替POST（新規登録）

実HTTP/本番DI/Identity/隔離SQLiteの123件を追加。[正常系/権限/期間](Monetail.Api.Tests/TransferCreateTests.cs)で201/空本文/D形式Location・UUID v7/v4・監査4列・ローカル秒精度・GET結果と、全ロール/双方所有、双方の支払方法/人物の期間両端、UTF-16/Unicode空白/全改行文字・元文字列保持を確認する。[JSON](Monetail.Api.Tests/TransferCreateInputTests.cs)は整数/型/UUID v4/必須7項目/未知/重複/不正本文を検証。[再送/原子性](Monetail.Api.Tests/TransferCreateSafetyTests.cs)は同一成功/異内容/異操作者/異操作/別家計簿、同時送信、再起動/削除済み結果、両INSERT失敗・各保存段階の認証/役割/人物対応/所有権/期間変化での全表rollbackを確認する。所有者変更の模擬はテストの同一Tx内だけで禁止トリガーを外し、rollbackでDDL/データが復元することも確認。本番の制約は維持する。[認証境界](Monetail.Api.Tests/TransferCreateBoundaryTests.cs)は別家計簿・401/404/CSRF/Origin・共有20回制限を検証する。

ローカルReleaseは最小2件→入力/権限/期間等81件が成功。安全性42件の初回は41件成功、所有者変更模擬の1件が既存の同名制約にも衝突して503となった。模擬データの名前だけを重複しない値に修正し、当該ケースと既存の詳細GET/レシート再送/同時登録を含む関連32件が成功（段階間に重複あり）。製品制約/判定は緩和していない。基点main `b37462be32d09b1bd84733b5f1526ab1b9f555a1`の[push Tests 43687665857](https://github.com/k-o-a-b-c/monetail/actions/runs/43687665857)成功を変更前の根拠にする。

ソース`73ce624d7906235963ebbc3958852898a8e043a9`の[Tests 43689887463](https://github.com/k-o-a-b-c/monetail/actions/runs/43689887463)でAPI全1830件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合、Dockerビルド/起動/サーバー/配布アーカイブ（更新・DB/PFX等のバックアップ/復元保全を含む）が成功。文書ローカル参照114件・差分検査も成功。影響外のClient/WinUI/旧Windows/性能等は既存の選択・成功記録を再利用し、全APIのローカル重複実行は行わない。実運用DB変更は行わず、実NAS/実Windows接続・実運用データ性能は未検証。PUT/DELETE・候補/補完・Client/WinUI・DB/索引/Migrationは変更していない。

## 振替一覧のfocusTransferId（DEC-279）

[フォーカス取得](Monetail.Api.Tests/TransferListFocusTests.cs)27件と[権限/取得途中の変化/実SQL](Monetail.Api.Tests/TransferListFocusSecurityTests.cs)8件の計35件を追加。D形式（大文字許可、小文字応答）・前後空白/N形式/空UUID/重複/cursor併用の400、全検索条件・日時優先・同日時ID BINARY降順・対象先頭・100/101/201件境界、フォーカスなしの通常cursor継続を検証する。不存在/削除済み/別家計簿/各条件外は同じ404本文とし、全ロール・401/404/429/503・全表非変更、対象確認後の参加解除/認証失効/対象削除での非公開/rollback、既存索引と包括的キーセット境界/OFFSET未使用も確認。レシートAPIはfocus指定を引き続き不正クエリとして拒否する。旧一覧の未対応focus拒否ケースは今回の対象拡張に合わせ、不正D形式拒否へ更新した。

ローカルReleaseは最小2件→focus27件→新規セキュリティと既存一覧/詳細を含む関連98件→日時とIDの大小が逆になる順序1件が成功（段階間に重複あり）。関連試験の初回は新規テスト補助のDbCommandパラメーター追加のコンパイルエラーで未実行。プロバイダー共通APIへ直した後に関連98件が成功し、製品制約/判定を緩和していない。基点main `04d8fde86e6bae57dbe3e11fdc5eee7c5fe81868`の[push Tests 43684311319](https://github.com/k-o-a-b-c/monetail/actions/runs/43684311319)成功を変更前の根拠にする。

ソース`3b97cf6b43f906f90d2f7143e5bda44ed81e7a4d`の[Tests 43685670397](https://github.com/k-o-a-b-c/monetail/actions/runs/43685670397)でAPI全1707件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合、Dockerビルド/起動/サーバー/配布アーカイブ（更新・DB/PFX等のバックアップ/復元保全を含む）が成功。文書ローカル参照413件・差分検査も成功。影響外のClient/WinUI/旧Windows/性能等は既存の選択・成功記録を再利用し、全APIのローカル重複実行は行わない。実NAS/実Windows接続・実運用データの性能、将来WinUIの404復帰は未検証。レシートAPI/書込API・Client/WinUI実装・DB/索引/Migrationは変更しない。

## 振替一覧GET

実HTTP/本番DI/Identity/隔離SQLiteの63件を追加。[一覧/検索](Monetail.Api.Tests/TransferListTests.cs)は0/1/99/100/101/200/201件、同日時ID降順、ページ間重複/欠落なし、月末/年末/閏日、元/先をまたぐ人物・支払方法の独立AND、全履歴への検索、英数字だけの全角/半角・大小文字同一視、かな/記号/互換文字の区別、7項目・UUID D/ローカル秒/空文字メモnull・全表非変更を確認する。[カーソル](Monetail.Api.Tests/TransferListCursorTests.cs)は条件/家計簿/SHA-256・q省略/空文字の区別、改ざん/不正payload/別鍵/レシートpurpose拒否、再起動・境界行削除を検証。[権限](Monetail.Api.Tests/TransferListSecurityTests.cs)は全ロール/人物未対応、401/404/429/503・取得後の認証/参加再検証、家計簿間の同一ID/別所有者を含む分離を確認する。`focusTransferId`は400 invalid_inputで拒否する。

[実SQLの計画](Monetail.Api.Tests/TransferListQueryPlanTests.cs)は1,001件の架空データでHTTPから実際のSQL/パラメーターを捕捉し、LIMIT 101・日時/IDのキーセット条件、OFFSET/COUNT未使用、既存一覧索引のSEARCH・一時ソート不要、データ非変更を検証。索引/DB変更・独自SLAは追加しない。既存詳細GETテストの「一覧未追加」確認は今回の実装に合わせて更新し、書込ルート未追加の確認を維持した。

ローカルReleaseは最小2件→一覧63件→実SQL計画と既存振替詳細/レシートの関連44件→同一支払方法IDの別家計簿所有者を加えて強化した分離1件が成功（段階間に重複あり、失敗/skipなし）。基点main `3a6513f8d064ede09cef4d3884dd4ff38f9a6c16`の[push Tests 43680516482](https://github.com/k-o-a-b-c/monetail/actions/runs/43680516482)成功を変更前の根拠にする。全APIは最終CIに集約し、影響外のClient/WinUI/旧Windows/性能等は既存の選択・成功記録を再利用する。

ソース`c4b4c65ca4de95a50378b99e5cb3052672cae785`の[Tests 43681600036](https://github.com/k-o-a-b-c/monetail/actions/runs/43681600036)でAPI全1672件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合、Dockerビルド/起動/サーバー/配布アーカイブ（更新・DB/PFX等のバックアップ/復元保全を含む）が成功。追加件数63はソースとテスト検出でも照合した。文書ローカル参照102件・差分検査も成功。実NAS/実Windows接続・実運用データでの性能は未検証。`focusTransferId`・候補/補完/書込API・Client/WinUI・DB/Migrationは変更しない。

## 振替詳細GET

[TransferApiTests.cs](Monetail.Api.Tests/TransferApiTests.cs)に実HTTP/本番DI/Identity/隔離SQLiteの26件を追加。既存`ReceiptApiTests`のfixtureを共用し、8項目のみ・外側ラッパーなし・UUID小文字D形式・ローカル秒精度・Int32整数・監査情報非公開、NULL/旧空文字メモのnull応答と空白保持、全ロール/人物未対応/他人所有の閲覧、同一IDの家計簿別分離を確認する。不正ID400/field、非参加/不存在404（全体Adminも参加必須）、匿名/期限切れ/失効401、取得途中の参加解除/認証失効/保存障害、共有120回枠429・既存GETのOrigin境界、全表非変更、一覧/書込ルートを追加していないことを検証する。

ローカルReleaseの最小1件が成功。追加26件の初回は25件成功/1件失敗で、別家計簿のテスト準備SQLに必須`display_order`指定が漏れていた。準備SQLだけを修正し、当該ケースと既存読取/DB/認証/初期化関連164件が成功（段階間に重複あり、最終関連検証の失敗/skipなし）。製品制約/テスト判定は緩和していない。基点main `883a374c61cfeaf13567e2032af2cba2302655e9`の[push Tests 43672912611](https://github.com/k-o-a-b-c/monetail/actions/runs/43672912611)成功を変更前の根拠にする。最終全API検証はCIに集約し、未変更のClient/WinUI/旧Windows/性能等は既存の選択・成功記録を再利用する。

ソース`63dd42bf67e161b3214b7ae39f236ab5a4a2f186`の[Tests 43673646591](https://github.com/k-o-a-b-c/monetail/actions/runs/43673646591)でAPI全1609件（失敗/skipなし）、Node63件、Python75件、旧VB実計算24ケース照合、Dockerビルド/起動/サーバー/配布アーカイブの検証が成功。文書内ローカル参照97件・差分検査も成功。実運用DB/NAS・実Windowsからの接続は未検証。振替一覧/候補/補完/書込API・Client/WinUI・DB/Migrationは変更していない。

## 振替DB：確定範囲の実装

[TransferSchemaTests.cs](Monetail.Api.Tests/TransferSchemaTests.cs)に53件を追加し、既存`ReceiptSchemaTests`の本番DI/Identity/家計簿fixtureを共用する。新規SQLiteの13列の型/NULL/既定値・複合PK・5本のRESTRICT FK・4 CHECK・一覧索引を検証。別家計簿だけの支払方法/存在しない参照、双方の参照削除制限、同じ振替IDの家計簿別独立性、同名許可、金額の1/Int32最大/範囲外/保存型、名前/メモの端点・空文字/空白/CR・LF・元文字列保持を確認する。SQLiteのコードポイント/ASCII空白/CR・LF限定という文字列検証の限界も固定し、APIの厳密検証が実装済みとは扱わない。

一覧索引のBINARY降順キーセット走査と一時ソート不要、EFでのローカル秒精度/監査値/更新版の往復、失敗時の全表不変・明示トランザクションのrollback、既存認証/レシートを含む初期化再実行/同時実行のno-op、表/索引がない旧構造の初期化/通常API拒否を検証。UUID/版の発行・原子的版照合・成功再送記録の同時確定は後続APIの検証対象であり、本工程には含めない。

ローカルReleaseは最小2件→振替51件→追加2件を含むDB/認証/レシート関連158件が成功（段階間に重複あり、失敗/skipなし）。基点main `62ddff27cfba097f28b910c1b4cd7ca1d68b1a41`の[push Tests 43668559578](https://github.com/k-o-a-b-c/monetail/actions/runs/43668559578)成功を変更前の根拠にする。影響外のClient/WinUI/旧Windows/性能等は既存CIの選択・成功記録再利用に従い、全APIのローカル重複実行は追加しない。

ソース`cb2e63e3aa350a04e11c7565a4c70a9a64a68b4f`の[Tests 43670354764](https://github.com/k-o-a-b-c/monetail/actions/runs/43670354764)でAPI全1583件（失敗/skipなし）・Node63件・Python75件・旧VB実計算24ケース照合・Dockerビルド/起動/サーバー/配布アーカイブの検証が成功。コンテナでの初期化拒否・更新・DB/PFX等のバックアップ/復元保全も確認した。文書内ローカル参照71件・差分検査が成功。実運用DB/NAS・将来の振替API/画面・旧DB移行は未検証。Migration/実データ変更は行っていない。

## PR #160レビュー修正：税率警告と一般入力エラーの分離

旧「税率履歴は未接続」説明を削除し、その他の未実装表示が現在の実装と整合することを確認。既存の税率警告/一般エラー領域を使用し、新しい表示コンポーネントは追加しない。Client2件・WinUI内の確認を追加し、税率不一致と単価不正の同時表示・独立解消、数量の追加/更新検証エラーと修正後の解消、入力文字列/下書き/選択/フォーカス・ID/版・未保存判定・保存済み明細/正式結果の非変更を検証する。`ValidateDraft`は既存追加/更新の検証だけを再利用し、反映しない。

ローカルReleaseは新規2件→関連85件→Client全778件が成功（段階間に重複あり、失敗/skipなし）。XAML XML構造と差分を確認した。変更前はPR先端`ccc5019`の[Tests 43413565094](https://github.com/k-o-a-b-c/monetail/actions/runs/43413565094)成功と、[WinUI既存438確認の成功](https://github.com/k-o-a-b-c/monetail/actions/runs/43412822117)を採用する。未変更API/DB/共通計算/Docker/旧版等はCIのfingerprintで成功結果を再利用する。

修正ソース`dd4f593cc09b01e1d7a2919189aa11460e37dcb4`の[Tests 43418945076](https://github.com/k-o-a-b-c/monetail/actions/runs/43418945076)でClient全778件・実DPAPI・WinUI製品/検証用Releaseビルド（警告/エラー0）・製品起動・既存438+追加6の計444確認が成功。2種類のエラーの同時表示と独立解消、追加/更新の検証エラー解消、値/下書き/選択/フォーカス/正式結果の保持を実画面で確認した。API等の未変更部分は成功記録を再利用し、判定条件やタイムアウトは緩和していない。実Windowsの人手操作・実IME/高DPI/ARM64・実API/NASは未検証。


## 工程6-D4-C：WinUIの税率・正式計算表示

Client17件とWinUI内の税率/金額確認を追加。税率候補/初期値は空履歴・NULL行/欠落・0%・同率別区分・期間端点を検証し、日付変更でも保存済み/流用/手動税率を保持する。反映済み専用の計算を従来`FromEditor`の未反映拒否と分け、正負・複数税率・値引・税込/税抜・floor/half_up、会計と参考額の粒度差、Int32/Decimal超過、不正/未確定/空グループの未計算表示を確認。再計算でID/版/選択/下書き/未保存判定を変えない。WinUIは実画面で候補/初期値・末尾ゼロ省略・0%/非課税、税設定切替、行/科目/会計合計、日付変更/不正文字列保持・再選択、キャッシュヒット/追加GETなし、取得失敗/明示再試行/401/取消後の旧応答、狭幅・保存ボタン無効/保存API未呼出を検証する。既存の3択・キーボード/IME・人物/管理画面も維持する。

ローカルReleaseは関連14件→編集/キャッシュ/計算関連103件→Client全773件、その後追加3件が成功（段階間に重複あり）。保存済み状態試験の初回1失敗は、元データの8%を試験履歴に明示して修正し、条件を緩和せず再確認した。WinUI初回は検証用csprojへの新partialファイルの登録漏れ、次は既存0%復元試験の履歴前提不足を修正。既存の復元条件を保ち、有効な0%履歴を追加し、値引/正負の正式額も固定期待値で強化した。追加画面試験はTextChangedの処理完了前に候補を確認していたため、モデル日付/判定表示の更新後に同じ条件を検証するようにした。タイムアウト延長/判定条件の緩和はしていない。文書ローカルリンク135件・XAML XML構造・差分検査も成功。

基点main `e271cd66de49081785dd4a8788e743b8f0c5544e`の[push Tests 43396015844](https://github.com/k-o-a-b-c/monetail/actions/runs/43396015844)成功を変更前の根拠にし、未変更API全1520件/共通計算/旧VB24ケース照合/Docker/旧Windows/性能はCIのfingerprintに従って成功結果を再利用する。Client全776件は[Tests 43410885872](https://github.com/k-o-a-b-c/monetail/actions/runs/43410885872)の成功ジョブを採用し、その実行全体のWinUI失敗を成功とは扱わない。

ソース`bf70c8f8000734e4212b657cfdab8045f2f5535e`の[Tests 43412822117](https://github.com/k-o-a-b-c/monetail/actions/runs/43412822117)でWinUI製品/検証用Releaseビルド（警告/エラー0）・製品起動・既存420+追加18の計438確認が成功。未変更Client/実DPAPIおよびAPI/旧版等は同一入力の成功記録を再利用し、全API/影響外のローカル重複実行は追加していない。`0xC0000005`は今回再発していないが、過去の原因解決とは扱わない。

実Windowsの人手操作・実IME/高DPI/複数モニター/ARM64、実API/NAS/実データは未検証。全体保存・競合/認証復帰UI・履歴候補UI・人物按分は未接続。API/DB/税率CLI/GET/キャッシュ保存方式/共通計算式は変更していない。

## 工程6-D4-B：正式レシート計算の共通化

API7件・Client11件・Python2件を追加。既存計算/入力検証113件を維持し、計算/入力/解析/結果/例外を`Monetail.Calculations`へ集約した。API/Clientが同じDLL・共通型を参照すること、BCL以外の製品依存がないこと、基本/マスタ検証の競合時もfield・メッセージ・順序が維持されることを確認する。Clientは同じ旧VB照合24ケースから期待入力を直接構成して変換後の全フィールドと照合し、共通計算の全結果、固定期待値による正負・NULL/0%・複数税率・会計/参考額の粒度差・Decimal/Int32超過を検証する。編集元のID/版/選択/下書き/未保存状態は変更せず、未反映入力を適用/破棄しない。共通ソース変更時のAPI/Client/Docker/Windows保存/WinUI選択・成功指紋と、API条件での旧VB照合起動も検証する。

ローカルReleaseは既存計算/検証113件→新規Client11件→保存API/検証関連364件・Client全759件が成功（段階間は重複あり、失敗/skipなし）。期待入力との完全一致へ強化したClientの24ケース照合1件も成功。Python全75件、変更文書のローカルリンク139件と差分検査も成功。基点main `bdfe1486bb950d46fb5c11fd56dd13f3731f7fd3`の[push Tests 43361651616](https://github.com/k-o-a-b-c/monetail/actions/runs/43361651616)を変更前の成功根拠にした。

ソース`701a792f42291b51d060ef1259655f1e6c6f2c40`の[Tests 43372294747](https://github.com/k-o-a-b-c/monetail/actions/runs/43372294747)でAPI全1520件（失敗/skipなし）、Node・Dockerビルド/起動/サーバー/配布アーカイブ、Windows上のClient759件・実DPAPI、旧VBの実出力24ケースと共通計算の照合、旧計算35件・SQLite移行10件、WinUI製品/検証用Releaseビルド（警告/エラー0）・製品起動・420確認が成功。APIと共通計算のソース差分を確認し、namespace/検証入口以外の計算本体は一致している。`0xC0000005`は今回再発していないが、過去の原因解決とは扱わない。共通CI選択スクリプト変更時の安全側判定を維持し、今回は影響外を含む必須対象も実行した（旧Windows98件・既存ビルド/読取性能も成功）。表のPython件数は税率JSON等を含む全discoverの実測値へ合わせた。

WinUIの金額/税率候補表示・保存ボタンは未接続（D4-C以降）。実Windows手動操作・高DPI/IME/ARM64・実NAS/実データは未検証。人物按分の最終端数等の未決定部分には踏み込まず、既存の監査上の差異を維持する。

## 工程6-D4-CLI：税率履歴の全履歴同期管理コマンド

実CLIプロセス/隔離SQLiteの57件を追加。引数・終了0/1・標準出力/標準エラー・Web非待受、厳密な4項目JSON・日付/Decimal/NULLと課税0%、全履歴差分・決定的な順序・空配列、dry-runの全表非変更、ID/更新版/保存文字列を保持する再適用、区間短縮＋追加、率だけの更新、削除ガードと明示許可、更新/挿入途中の例外による全件ロールバック、不正DB値の拒否、書込ロック失敗・同時適用の直列化、CLI反映後の既存GETと認証/家計簿等の非変更を確認する。`initialize-db`は履歴0件で、通常起動/初期化/再実行でJSONを自動投入しない。

ローカルReleaseは最小1件→関連54件→不正保存値3件が成功（最小は関連54件に含む、失敗/skipなし）。変更前main `a3dbad3ccfbda13dd7c1a9fd56a18eb801fe7e29`の[push Tests 43347266205](https://github.com/k-o-a-b-c/monetail/actions/runs/43347266205)成功を基準とし、影響外のClient/実DPAPI/WinUI/旧Windows/性能はCIのfingerprintに従って再利用する。変更文書のローカルリンク77件・Bash例の構文・差分検査も成功。

ソース`7bc877cc65131d6395aca2a7ef0e77ccb3408208`の[Tests 43349662264](https://github.com/k-o-a-b-c/monetail/actions/runs/43349662264)でAPI全1513件（失敗/skipなし、テスト実行12分6秒）、Node、APIのDockerビルド・起動/サーバー/リリースアーカイブのスモーク、旧VBとの実計算照合が成功。Client/実DPAPI/WinUI/旧Windows/性能は未変更のため成功記録を再利用し、全件のローカル重複実行は追加していない。初回Draft CIは追修正のpushで中断し、その実行を成功とは扱わない。

実運用DB・登録用`config/tax-history.json`への適用は行っていない。実NASの権限/マウント、実データでの同期、Tailscale経由のGETは未検証。D4-A・DB構造/Migration・D4-B/C・WinUIは変更しない。

## 工程6-D4-A：共通税率履歴の全件GET・Clientキャッシュ

新規API10件・Client33件とWinUI内4確認を追加。実HTTP/隔離SQLiteで全区分・過去/将来・空履歴・同率の別区分、4項目だけのJSON・Decimal文字列/NULL・課税0%、Admin/Editor/Viewer・未参加/匿名、取得中の参加解除/認証失効・保存障害の非公開/rollback、共有読取120回、既存GET/書込のOrigin境界を確認する。Clientは期間両端・終了なし、64KiB超の全履歴、厳密な型/日付/Decimal、認証更新、別アカウントの検出、再ログイン/ログアウトと失敗時、同時取得・明示再試行、コンテキスト破棄後の旧応答拒否を検証。接続先変更/Disposeも実際のClientConnectionで確認する。CLI・計算共有・候補/金額表示・DB/Migration・自動投入は追加していない。

ローカルは最小API1件/Client2件→関連API10件/Client33件→Client全748件が成功。新規API試験の初回4失敗は、既存GETのOrigin契約とHTTPSのテスト用Client構成へ合わせて修正した。精度試験はDecimalで正確に表せる小数の文字列一致へ強化し、最終CIで確認した。ローカルAPI全件は実行中の環境再起動後に完了結果を取得できず、不採用（追加の精度テスト実行も中断）。同じ出力先でのビルド競合を避け、再起動後に全件を繰り返さずCIの正常終了結果を採用する。

ソース`51126c02153577471d23ddecc4bae5f2c7728875`の[Tests 43322696503](https://github.com/k-o-a-b-c/monetail/actions/runs/43322696503)でAPI全1456件（失敗/skipなし）、Node、APIのDockerビルド・起動/サーバー/リリースアーカイブのスモーク、旧VBと新版APIの実計算照合が成功。税率JSON/DBの自動投入・既存データ書換えは行わない。ソース`cd207f25de4eab432f8a77fb17136629adfb7fe2`の[Tests 43321885948](https://github.com/k-o-a-b-c/monetail/actions/runs/43321885948)の成功ジョブでClient/実DPAPI、WinUI製品/検証用Releaseビルド（警告・エラー0）・製品起動・既存を含む420確認を確認し、以後同じ指紋の成功記録を再利用した。実行全体の中断を成功とは扱わない。未変更の旧Windows/性能等も既存CIの判定を維持する。

基点main `d84fd20b881173acdc300e49f4ae2ea2d541b1bb`の[push Tests 43318967258](https://github.com/k-o-a-b-c/monetail/actions/runs/43318967258)はClient/実DPAPI成功、WinUIは既存ReceiptCategoryMemoの合成IME確認で失敗していた。本PRでは再現しなかったが、その原因解消とは扱わず、当該テストの判定やタイムアウトも変更していない。今回`0xC0000005`は再発していないが過去の原因解決とはしない。実API+Windowsの人手操作、実NAS/認証器、実IME/高DPI/ARM64は未検証。

## 旧VBと新版APIの計算結果の直接照合（PR #154）

[共通24ケース](../docs/tests/receipt-parity-cases.json)を`tests/CalculationTests/LegacyParityExporter.vb`で**旧VBの本物の計算コード**へ流し、JSONで金額を出力する。別プロセスの`tests/Monetail.Api.Tests/ReceiptParityTests.cs`が、同じ各グループ・明細の値を新版`ReceiptCalculator`へ流し、**会計合計・収入・支出・グループ参考額・明細参考額・値引後額**を突き合わせる。共通JSONを一方で別の計算式に写経して比べる方式ではない。

CIの`receipt-calculation-parity`ジョブは、APIまたは旧計算のコードに変更があったら実行する。[成功実行](https://github.com/k-o-a-b-c/monetail/actions/runs/43187316579)で、旧VBから24件出力、比較xUnitは**1件成功・失敗0**。通常のAPIテストでは旧結果ファイルが存在しないため、入力JSONの基本検査のみ実行し、**対照比較を済ませたとみなさない**。

ローカルでも両SDK（.NET 8/10）があれば、旧版の結果を生成してから新APIのテストを絞って実行できる：

```bash
dotnet run --project tests/CalculationTests/CalculationTests.vbproj --configuration Release -- --export-parity /tmp/receipt-parity.json
MONETAIL_LEGACY_PARITY_RESULTS=/tmp/receipt-parity.json dotnet test tests/Monetail.Api.Tests/Monetail.Api.Tests.csproj --configuration Release --filter 'FullyQualifiedName~ReceiptParityTests'
```

この比較は**正常値24ケースの金額**に限定する。実際の旧DB内容、日付文字列表現、NULL保存額、対象人物按分、保存時マスタ期間検証、エラー境界や例外種類の完全一致は**未検証**。[照合と移行時の論点](../docs/legacy-new-data-calculation-audit.md)を参照する。

## 工程6-D3：レシート明細フォーム・一覧

Clientへ23ケースを追加（入力文字列18・費用区分5）。新規/復元時の未変更、値戻し、整数式の優先順位・Int32上下限/途中オーバーフロー、無効文字列の保持と原子的な下書き反映、Decimalの暗黙丸め拒否、税率3状態・人物コピー、支出の3費用区分/収入の対象外限定を確認する。ローカルReleaseは最小23件、関連63件、最終Client全714件が成功。最初のコンパイルでは例外のnamespace参照不足を修正した。既存テストの削除・判定緩和なし。

WinUI合成ケースを追加し、新規/既存のフォーム・6列表示、追加/更新/流用/削除/上下移動、既存ID/版/期限外人物、値戻し、別明細/科目切替/科目追加/削除前の3択、取消/不正入力/連続削除/フォーム維持、Enter/Shift+Enter・Alt+Enter・Ctrl+Enter抑止、人物の上下/Space/Tab、一覧の上下/Enterを検証対象とした。IMEは共通ヘルパーの合成状態を確認する方式で、実IME操作を検証済みとは扱わない。初期化済みの実UIが使えることを待つ既存ヘルパーを使用し、待機上限は延長しない。

ソース`73bc16df54c8bdbb9ca6958259da663dd40f4c47`の[Tests 43114978800](https://github.com/k-o-a-b-c/monetail/actions/runs/43114978800)で、WinUI製品/検証用Releaseビルド（警告・エラー0）・製品起動・既存を含む416確認が成功。メモ3欄の追修正は21確認で、標準1行/高さ指定なし、入力反映、Enter/Shift+Enterと追加/更新の非実行、合成IME中/変換終了直後の移動抑止、値戻し、改行を含む保存済み文字列の保持、他項目変更/再描画、明細メモからのAlt+Enterを確認した。共通FormKeyboardを変更せず、入力前の実際のLoaded/レイアウト/フォーカスとTextChangedの反映を観測する。

実行開始後に判明した、Grid.IsEnabled参照・テストの名前空間不足・TextBlockをGridとして検索する不備・合成税率のnullable警告を修正。行選択でItemsSourceを作り直してフォーカスを失う問題は、選択時に既存コンテナーを保持する修正と実キー試験で確認した。明示的な編集終了でテスト間を分離し、入力値を出力しない状態/解析診断を加えた。既存の判定条件と15秒/全体120秒の待機上限は維持する。今回`0xC0000005`は再発していないが、過去の原因解決とは扱わない。

変更前main `0492c30f2612f9444e2cf625ed5b9e90b7fe839e`のpush CIは当初キュー待ちだったため、統合済みPR #146の[WinUI353確認](https://github.com/k-o-a-b-c/monetail/actions/runs/43021712671)と[Client/実DPAPI](https://github.com/k-o-a-b-c/monetail/actions/runs/43017415753)の成功を基準にした。Clientは本PRの[Tests 43110323133](https://github.com/k-o-a-b-c/monetail/actions/runs/43110323133)の成功ジョブ、実DPAPIは[Tests 43110484224](https://github.com/k-o-a-b-c/monetail/actions/runs/43110484224)の成功ジョブを以後のCIで再利用（各実行全体の中断/失敗を成功とは扱わない）。API/Docker/旧Windows/性能等も未変更部分のfingerprintに従って再利用し、修正ごとのClient全件・影響外検証は追加実行していない。

税率履歴の正式取得経路と正式計算のWinUI接続は未整備。新規税率は未選択、参考額・合計は未計算とし、保存済みの非課税/課税0%等は維持する。全体保存ボタンは無効で書込APIを呼ばない。実API接続、実Windowsの人手/IME/高DPI/複数モニター/ARM64は未検証。API・DB・Migration・外部ライブラリの変更なし。

## 工程6-D2：WinUIレシート基本入力

既存の1シナリオへ34確認を追加（合計332確認）。新規のローカル本日/00:00:00・空欄/未選択/周期なしと未変更判定、親情報4行/日本語税設定のAPI値対応、共通日付/時刻補完、狭幅の縦積み、科目の共通順/重複拒否/メモ/フォーカスと確定の分離/削除確認と選択解除、無効な保存操作を検証する。既存詳細の全階層/ID/税率/NULL/日時/周期保持、NULLメモを元に戻した場合の変更解消、未確定日付を含む離脱取消・管理画面/家計簿切替入口の保護も対象。単一選択で詳細GETなし、二重起動拒否、失敗時の手動再試行、中断とリング停止、遅延応答の破棄、別レシートの再起動、401のログイン遷移、追加GETなしの一覧検索/選択/非先頭スクロール復帰を確認する。

最小の新規表示・離脱保護が[Tests 43000058981](https://github.com/k-o-a-b-c/monetail/actions/runs/43000058981)で成立後、上記の関連確認へ拡張した。途中で入力通知前の離脱判定、名前空間衝突、合成応答の日時/Decimal形式、標準ListViewの操作準備とClientSession直列化の試験前提を修正した。既存日付試験は連続Focusの前後で実際のフォーカスとLostFocus完了を観測するようにし、期待値・待機上限・UI Automation共通ヘルパーを緩和していない。

ソース`dccc7b85cf0821fa6ee06a0d9a06fe7022ab7d3d`の[Tests 43005054668](https://github.com/k-o-a-b-c/monetail/actions/runs/43005054668)でWinUI製品/検証用Releaseビルド（警告・エラー0）・製品起動・既存を含む332確認が成功。基点main `9e85648b4ed7461d3015125e97b4069505ae672d`の[push Tests 42991325636](https://github.com/k-o-a-b-c/monetail/actions/runs/42991325636)を変更前の根拠とし、未変更のClient675件・実DPAPI3件・API/Docker/旧Windows/性能等はCIのfingerprintで成功記録を再利用した。ローカルはXAMLのXML構造・文書リンク・差分を確認。LinuxでWinUIビルド/画面を実行したものではない。今回`0xC0000005`は再発していないが、過去の原因解決とは扱わない。

明細フォーム/一覧・API保存・計算・競合/結果不明/編集認証復帰・履歴候補/自動補完UIは未実装。物理ダブルクリック/キー/IME・各DPI/複数モニター・実API接続と保存・ARM64実機の手動確認は未検証。合成画面は同じ製品XAML・イベント/共通入口・標準コントロールを使用し、物理操作と区別する。API・DB・Migration・ClientSession/DTO・外部ライブラリの変更なし。

### PR #146：親情報RadioButton・手動保護・周期入力の追修正

新規は税込/切り捨てを選択済みにし、周期CheckBoxを削除、間隔空欄・表示月と保存用の両列NULLを分離した。Clientへ由来管理/補完保護と周期解析の16ケースを追加（全691件）。同値再選択の手動保護、項目別の補完可否、保存済み値の保護、値を戻した場合の変更解消、空欄/不正/Int32境界を確認する。ローカルの関連16件・Client全691件と、ソース`df178991548e177f186c4e7384c434dc5d8326c9`の[Tests 43017415753](https://github.com/k-o-a-b-c/monetail/actions/runs/43017415753)でWindows Client691件・実DPAPI3件が成功。

既存WinUIケースは期待値を新仕様へ同期し、入力保持・一覧復帰・離脱保護の判定は維持した。初期値/既存復元、同値再クリック/別値選択、手動フラグだけでは未変更、プログラムのIsChecked/自動値/再描画は非手動、周期解除/不正文字列/モデル未変更時の不正入力離脱を追加確認。標準RadioButtonはSpaceで選択し、Enterを独自の選択キーへ変更しない。実キー試験で誤って前提にしたEnter選択と、マウス試験のクライアント/画面座標の不一致を修正し、判定・待機上限は緩めていない。

ソース`b1ec3d1e3ea1010831402ea1869577b62cdf90c7`の[Tests 43021712671](https://github.com/k-o-a-b-c/monetail/actions/runs/43021712671)でWinUI製品/検証用Releaseビルド（警告・エラー0）・製品起動・既存を含む353確認が成功（追修正で21確認追加）。テスト専用のSendInputでマウス押下/解放とSpaceを送り、標準Clickの発火・手動状態を確認する。製品へ入力注入や独自キー処理を追加しない。テストのクリック位置は標準レイアウト変換・実倍率・ClientToScreenで取得し、データ/認証情報を診断へ記録しない。今回`0xC0000005`は再発していないが、過去の原因解決とは扱わない。

基点PR先端`549893c`の[Tests 43005713485](https://github.com/k-o-a-b-c/monetail/actions/runs/43005713485)を変更前の根拠とし、API/Docker/旧Windows/性能等はCIのfingerprintで成功記録を再利用。テストだけの追修正でも成功済みClient/Storageを再利用した。実APIの履歴補完は未接続で、API/DB/Migration/ClientSession/DTO/外部ライブラリは変更しない。実機の人手操作・IME・各DPI/複数モニター・ARM64は未検証。

## 工程6-D1：レシート編集状態モデル

40ケースを追加。新規／既存編集、同じ現在値からの更新・流用登録準備、変更なしの流用登録、親/子IDの維持・除外と安定した画面内キー、科目重複・メモ・削除・再追加、明細の追加/更新/流用/削除/上下移動、対象人物引継ぎ、税率未選択/非課税/0%、初期値の明示更新を確認する。未反映フォームと未保存モデルの分離・元に戻した時の変更解消、切替3択・取消・検証/初期値取得失敗時のデータ保持、未反映入力/空グループの保存準備拒否、入力元・表示コピー・保存用配列とベースラインの分離も対象。既存テストは緩和していない。

ローカルReleaseは最小3件→関連30件→新規40件→最終Client全675件が成功（段階間は重複あり、失敗/skipなし）。ソース`e2e278f2a5cd2f2895e0eab6e0882fa486434fc7`の[Tests 42963348653](https://github.com/k-o-a-b-c/monetail/actions/runs/42963348653)でWindows上のClient675件・実DPAPI3件・WinUI製品/検証用Releaseビルド（警告・エラー0）・製品起動・既存画面298確認が成功。今回`0xC0000005`は再発していないが、過去の原因解決とは扱わない。変更前main `199a3ae53c0f82c7507da50b27c1a1a1ac4f122a`の[push Tests 42957830084](https://github.com/k-o-a-b-c/monetail/actions/runs/42957830084)成功を基準とし、影響外のAPI/Docker/旧Windows/性能はCIのfingerprintによる成功記録を再利用した。文書のローカルリンク・差分検査も成功。

WinUI入力・ダブルクリック・保存ボタン・流用確認・要求保持/結果不明/競合/認証復帰の画面処理は未実装。モデルはHTTP・requestId生成・時計・税計算を持たず、金額/マスタの検証フックを用意する。実APIへの保存/流用登録・実Windowsでの入力手動操作は後続工程で確認する。API・DB・Migration・外部ライブラリの変更なし。

## 工程6-C：レシート保存・削除Client

65ケースを追加。本文なし201/204と不正成功応答、POST/PUT/DELETEの全階層、Decimal文字列/整数/NULL、ローカル秒精度と地域設定、既存ID維持/新規ID省略、入力配列変更後も固定した本文/requestId/revisionの再送、操作/家計簿の分離を確認する。認証更新・別アカウント変更・失効、入力不正/field/master_changed、403/404の権限情報・非公開latest非参照、429/503、結果不明の通信/タイムアウト/不正応答、利用者キャンセルも検証する。PUT/DELETEの64KiB超409は既知長/分割ストリーム双方で詳細GETと同じ全階層検証を通し、他ステータスと既存APIの上限を維持する。

ローカルReleaseは最小3件、再送/大きな競合8件、関連58件、最終Client全627件が成功（途中は重複あり）。ソース`ac752f5895afd46214e9e2307cc2a293786798d2`の[Tests 42947354570](https://github.com/k-o-a-b-c/monetail/actions/runs/42947354570)でClient全627件・実DPAPI3件、WinUI製品/検証用Releaseビルド（警告・エラー0）、製品起動、既存画面298確認が成功。今回`0xC0000005`は再発していないが、過去の原因解決とは扱わない。変更前main `fe04f10cfcbad61e6f8bd5bd9dbe5867ba8d5d97`の[push Tests 42944222522](https://github.com/k-o-a-b-c/monetail/actions/runs/42944222522)成功を基準にし、API/Docker/旧Windows/性能の成功結果はCIのfingerprintで再利用した。

409共通エラー形式の追修正は、`LedgerApiFault.Body`のcode/message/field/latest/permissions（NULLを含む）と同じ構成で8ケースを追加（累計73件）。初回の簡略応答では未検証だった共通項目を受理し、message/409のpermissionsは破棄、fieldは保持する。修正前の最小3件でrequest_conflictが結果不明へ誤分類されることを再現し、修正後の関連24件・Client全635件がローカルで成功。大きな409と不正latestの既存テストも実形式へ変更し、判定を維持した。ソース`ff3236dd62db95347010570c73959678e4cb0f51`の[Tests 42954346851](https://github.com/k-o-a-b-c/monetail/actions/runs/42954346851)でClient635件・実DPAPI3件・両WinUI Releaseビルド（警告・エラー0）・製品起動・既存298確認が成功。変更前のPR先端`8e73899`の[Tests 42948052120](https://github.com/k-o-a-b-c/monetail/actions/runs/42948052120)成功を基準に、非変更部分は成功記録を再利用した。

API・DB・Migration・WinUI画面・外部ライブラリは変更しない。書込は合成HTTPでの検証であり、実APIへの保存/削除、実Windowsでの新規入力・結果不明/競合/再認証の手動操作は未検証/後続工程。

## 工程6-B：レシート一覧とマスタキャッシュ

検索・ページング・キャッシュのClient回帰34ケースを追加（全562件）。年月/店名の下書き分離、月末/年境界、0/1/100/101/201件、20行先読み、初回300件上限と明示続行・補充中断/カーソル復旧後の上限維持、表示領域充足後のボタン非表示、二重要求、選択保持、古い応答、初回/後続失敗と明示再試行、invalid_cursorの一度だけの自動復旧、キャンセル/タイムアウトを検証する。キャッシュは初回取得共有・全期間保持・家計簿分離・破棄後の旧応答拒否・管理画面更新優先・本人IDによる表示名を確認する。

WinUIの同じ製品XAML/コードを用いるシナリオへ、0件、確定/未確定検索、不正年月で無通信、追加読込と選択維持、保存済み額/日時/NULL、期間外の名称、管理画面からのキャッシュ更新、初回エラー/再試行、ログアウト破棄、初回/追加読込中の画面切替と復帰（リング停止・同条件/カーソル再開・先頭以外のスクロール/選択保持）を追加。既存の管理画面の通信件数は起動時マスタ取得1回を区別して厳密比較し、追加GET禁止の検証は維持する。画面のスクロール実観測と物理キー/IME/タッチの手動検証は区別する。

画面切替・復帰の追修正は、変更前PR先端`4378c00`の[Tests 42940005898](https://github.com/k-o-a-b-c/monetail/actions/runs/42940005898)成功を基準とする。新規Client6件とレシート関連128件（重複あり）がローカルで成功。ソース`dcafb8b73a21d6e789f09ba8588b4cf6c4768318`の[Tests 42942577862](https://github.com/k-o-a-b-c/monetail/actions/runs/42942577862)でClient全562件・実DPAPI3件、製品/検証用Releaseビルド（両方とも警告・エラー0）、製品起動、既存画面を含む1シナリオ298確認が成功。初回/追加要求の中断と同条件再開、重複再開/旧応答の拒否、補充回数の維持、再開後の通信エラーは手動再試行のみであることを確認した。WinUIでは非先頭の実ScrollViewer位置を復帰中/成功後にも比較する。今回の画面実行で`0xC0000005`は再発していないが、過去の原因解決とは扱わない。テスト判定・タイムアウト・既存UI Automationヘルパーは緩和していない。

変更前はmain `82a1eefce6d625b92877533cee8db99972a311bf`の[Tests 42916955384](https://github.com/k-o-a-b-c/monetail/actions/runs/42916955384)成功を採用。変更対象外のAPI/Node/Docker/旧Windows/性能はCIのfingerprint判定に従い既存成功を再利用する。実API接続、Windows 11実機の高DPI/モニター移動、物理キー/IME、ホイール/タッチの体感と実ユーザーデータは未検証。過去の`0xC0000005`は今回再発しなかった場合も原因解決とみなさない。

## 検証する範囲

- Clientは合成HTTPと一時保存先を使う。接続確認、OAuth/ループバック、セッション復元、更新と失効、別プロセスの認証変更、設定/トークン保存、家計簿の起動先・作成・切替、人物のCRUD/順保存/競合/再送を確認する。日付・時刻は補完期間・世紀選択・空欄・不正入力・全角・区切り・地域設定を検証する。
- StorageはWindowsのCurrentUser DPAPIを実際に使い、暗号化保存と新しいStoreでの復元、破損時の認証破棄と接続設定保持、平文の拒否を確認する。UIへの依存はない。
- WinUI 3は製品EXEを起動し、同じMainWindowのXAML・各partial・ClientConnection・PersonRowを使う別のスモークAppで合成データを検証する。ホーム、左右の人物管理、リサイズとテーマに加え、接続設定・ブラウザー起動の失敗と再開・認証キャンセル・実ループバックからの合成認可応答・保存ログインの確認と失効・家計簿作成の再送と選択・接続先変更・ログアウト成功／失敗を1シナリオとして確認する。製品Appから起動時の接続・復元を開始し、遅い接続での同じWindow内の待機表示・完了後の停止・タイムアウトも確認する。実データや保存済みの接続先/トークンは使用しない。 人物の削除確認では参照拒否・競合・通信失敗/応答喪失・降格/除外・認証失効を確認する。人物編集では保存・再送・競合・権限喪失・ContentDialogの保存／取消と背後の移動抑止、同一アカウントの再ログイン後の入力/版/要求IDの復元、別アカウント・別APIへの非引継ぎも検証する。参加者の役割/人物対応・除外/競合/権限喪失と、家計簿招待の発行/再送/コピー/取消も同じ画面の実コントロールで検証する。入力部品は同じハンドラーと実コントロールのフォーカスで確認し、物理キー注入や実IME変換の検証とは区別する。
- 科目画面は同じ合成HTTP/WinUIシナリオ内で、基本の一覧・作成・削除を先に確認し、期間フィルター/日本語名/入力初期値/部分編集/作成応答喪失の同一要求ID再送/近傍エラー/参照拒否/全項目競合/全件順の競合/権限/認証復帰を確認する。人物フィルターではNULL・境界日・開始前/終了後・非表示本人・選択解除・全件共通順と保存応答の本人先頭を確認し、表示切替で追加GET・書込しないことを検証する。
- APIは実HTTP・合成アカウント・隔離SQLiteを使い、認証・現在の参加/役割・保存失敗の巻き戻し・同時操作・FK・再送・競合・招待の状態と消費・共有回数枠を検証する。CookieとWindows Bearerを区別する。
- 匿名自己登録は実IdentityのWebAuthn検証、保存各段階とコミットの巻き戻し、同時登録・重複鍵、移動窓・容量・信頼済みIP、再起動／認証リセット、DB初期化と再実行時のデータ保持を確認する。登録後の通常ログイン、Bearerでも自己登録のOrigin・CSRFが必要なこと、招待登録を成功数へ含めないことも対象とする。
- 全体AdminのWeb管理はHTML/JS/一覧/更新の権限、登録状態と招待用途の分離、メモ保存/初期化再実行/再送/ロールバック、画面内再認証と入力保持、五十音の安定順・URL破棄を対象とする。
- 招待はURL初回発行・7日/30分/CLI復旧10分・ハッシュ保存・取消/再発行（CLI復旧・未知用途は取消対象外）・未登録/利用中/鍵残存へのCLI復旧拒否・二重消費・旧方式拒否・管理者権限変更とロールバック・CLI復旧のID/名前/ロール保持を確認する。画面は製品JSをNodeのvmで実行し、フラグメント除去、保存先の制約、キャンセル/通信失敗の再試行、明示ログイン、OAuth戻り先制限を検証する。実ブラウザーの検証とは区別する。
- Dockerは既存の起動確認に加え、Dockerサーバー用Composeのhostネットワーク・loopback限定待受・外部プロキシ非同梱、隔離bridgeでの秘密ファイル読込・非root起動・PFX再生成拒否・DB/管理者/PFXの保持・転送HTTPSとHost境界を確認する。実際の外部HTTPS終端へは接続しない。
- 旧VB.NET版の計算・DB・画面・移行・性能は既存のテスト構成を維持する。実データ・利用者環境への適用を自動検証済みとは扱わない。

## 登録モード設定の追加検証

`RegistrationSettingsTests` の13ケースは、既定招待制・有効値・不正値・通常API起動時の設定固定・不正値の起動拒否・DB非初期化を対象とする。追加時のPR #101単独の[CI 41644938668](https://github.com/k-o-a-b-c/monetail/actions/runs/41644938668)は成功。これらは下記の自己登録API回帰にも含め、未設定で自己登録を403にすることも確認した。

## 採用結果

レシート工程6-AはClient読取99ケースを実検出。一覧5条件/7項目/0・100件/nextCursor、詳細全階層/ローカル秒精度/JSON整数/Decimal文字列/非課税null・課税0%、候補3種/補完NULL、型付き400/invalid_cursor/403/404/429/503、認証更新/別アカウント/通信失敗/キャンセルを確認する。最小3件成功後、境界/共通Transport73件は71件成功・2件がテストJSONのPascalCase誤生成で失敗したためcamelCaseへ修正。新規91件とClient全520件、最後に追加した不正JSON/補完正常値/整数・Decimal境界8件が成功。既存テストの削除/判定緩和なし。64KiB超の100件をContent-Length有無の両方で読み、100,000文字メモ/150明細も切り捨てず取得する。一覧/詳細成功だけ逐次読取、候補/補完/エラー/既存APIの64KiBは維持。基点main `b29a1c9`の[push Tests 42907167596](https://github.com/k-o-a-b-c/monetail/actions/runs/42907167596)は成功。ソース`8fa8aa682e5249c9302da53cc65daba2db71436d`の[CI 42914258440](https://github.com/k-o-a-b-c/monetail/actions/runs/42914258440)でClient全528件・Windows実DPAPI3件（失敗/skipなし）、WinUI製品/スモークのReleaseビルド（警告/エラー0）・製品起動・既存画面スモークが成功。API/Docker/旧版等の非変更範囲は基点mainと成功記録を再利用した。実API接続/実Windows手動操作/大規模な実運用データは未検証。DB/外部依存・WinUI・書込・画面キャッシュ・下書き変更なし。

レシート工程5は新規96ケースを実際に検出。候補3種/空条件/0・100・101件/全履歴/元文字列重複/UTF-8 BINARY順/親更新UTC/正規化と完全一致スコープ、本人所有/両有効期間/保存ロール/人物未対応の補完、DEC-274の取得中変更12通り、入力・401/404/429/503・家計簿分離・並行要求を対象とする。最小3件→機能51件→権限/入力等55件（既存税率履歴11件を含む）→初回性能1件→計画見直し後の関連24件→既存一覧/詳細GET3件が成功。基点main `f0646c0`の[push Tests 42897073557](https://github.com/k-o-a-b-c/monetail/actions/runs/42897073557)は成功。ソース`3f12982d32354f32ffa7b6f3357034d9c6275166`の[CI 42900968012](https://github.com/k-o-a-b-c/monetail/actions/runs/42900968012)で全API1444件（失敗/skipなし）・Node63件・CI選択検証・Dockerビルドと通常/server/配布アーカイブの起動・永続化検証が成功。全APIのローカル重複実行は避け、非変更範囲の成功記録を再利用した。文書リンク・差分検査も成功。

架空1万レシート/10万明細の実HTTPを各条件5回計測（中央値/標本p95、n=5の参考値、SLAではない）：店名空12.9/13.8ms・店名検索20.6/26.6ms・所在地空10.5/12.9ms・所在地検索9.0/11.2ms・品名空9.0/9.6ms・品名不一致10.2/10.3ms・自動補完9.7/10.2ms。初回の品名不一致242.2/286.1msは明細からの走査だったため、CROSS JOINで店名/科目を先に既存索引で絞る計画へ修正（結果も再検証）。実SQLのEXPLAINは家計簿索引→グループの科目UNIQUE索引→明細のgroup索引を確認し、集約・整列にはSQLite内部の一時B-treeを使う。新索引・DBモデル/初期化/Migration・検索キー保存列・集約表・依存追加なし。空白候補の除外案・URL全体のバイト長・性能SLAは未確定のまま。実運用DB/実NAS/WinUIは未検証。

レシート工程4は実HTTPの一覧71ケースを追加。0/1/99/100/101/200/201件、同日時BINARY ID降順/日時境界、月末/年末/全期間、所有者/支払方法/正規化店名のAND、7フィールド/NULL/保存額、カーソルの条件・家計簿束縛/改ざん/再起動、認証/参加の取得後再検証、共有参照枠を検証する。最小3件→検索/カーソル60件→権限/性能9件→追加境界2件が成功。月のInvariant表現・既存詳細GET・取得後のアクセス再検証の最終11件も成功。10万件の隔離SQLite/実HTTPで先頭23.7ms、深い同日時キーセット31.0ms、月15.8ms、所有者16.9ms、支払方法15.7ms、正規化検索の全件不一致245.2msを計測（当実行環境での単発値、性能保証ではない）。全条件で既存日時索引を使い、一時ソートなし。追加索引・DBモデル/初期化/Migration変更なし。基点main `5f61a48`の[push Tests 42887074931](https://github.com/k-o-a-b-c/monetail/actions/runs/42887074931)は成功。ソース`9458ca74f9ec60b0ad73597708a43d5552bdd14c`の[CI 42889329526](https://github.com/k-o-a-b-c/monetail/actions/runs/42889329526)で全API1348件（失敗/skipなし）・Node63件・CI選択検証・Dockerビルドと通常/server/配布アーカイブの起動・永続化検証が成功。全APIのローカル重複実行は避け、影響外の成功記録を再利用した。文書リンク・差分検査も成功。実NAS/実運用DBは未検証。

レシート工程3は`ReceiptApiTests`の実HTTPケース121件を追加。詳細の全階層/NULL/配列/UUID/順序、本文なし201/204、子ID維持/移動/差分削除、無変更PUTの親書込なし、POST/PUT/DELETE再送/操作者/現在ロール/内容衝突、版CAS/同時保存、master_changedの24通り、全階層/操作記録の障害ロールバック、保存/競合情報取得後の権限・本人対応・認証再検証、共有回数枠、マスタ期間と使用済み科目の保護を対象とする。最小3件成功後、基本57件、重点32件が成功。計算を含む関連231件の初回は230件成功し、重複JSONテストの生成がUnicodeエスケープで一致しない1件を補正した。既存の科目/支払方法を含む回帰279件と最終重点5件は成功。監査日時を新規時に同値へ固定した後の最小3件も成功。ソース`0478aff916eb363418a9128b80ab0beae79ae315`の[CI 42823947462](https://github.com/k-o-a-b-c/monetail/actions/runs/42823947462)で全API1277件（失敗/skipなし）・Node・CI選択検証・Dockerビルドと通常/server/配布アーカイブの起動・永続化検証が成功。全APIのローカル重複実行は避け、影響外は基点main `01a8ba4`の[push Tests 42815233196](https://github.com/k-o-a-b-c/monetail/actions/runs/42815233196)と成功記録を再利用した。DBモデル/初期化/Migrationの変更なし。実運用DB/実Windows/実NAS/実パスキー端末は未検証。

レシート工程2は計算/検証113ケースを追加。Decimalの値引Ceiling・税込/税抜・正負・floor/half_up・税率別会計合計・グループ/明細参考額・非課税NULL/課税0%、Int32境界/相殺/Decimalオーバーフロー、数量/率/日時/UTF-16長/周期、科目別費用区分、取引日税率/期間端/空白期間/履歴削除を固定値で確認する。初回最小3件成功後に拡張し、関連195件（新規112＋既存DB83）と、マスタ未取得ガード追加後の新規113件が成功。入力・マスタを自動補正/保存せず、人物按分の最終端数は未実装。ソース`569f6f055a92e1ffd08d43fc21212ff395942d63`の[CI 42811307085](https://github.com/k-o-a-b-c/monetail/actions/runs/42811307085)で全API・Node・CI選択検証・Dockerビルドと通常/server/配布アーカイブの起動・永続化検証が成功。全APIのローカル重複実行は行わず、影響外は基点main `c12a262`の[push Tests 42808134892](https://github.com/k-o-a-b-c/monetail/actions/runs/42808134892)と成功記録を再利用した。文書リンク・差分検査も成功。実API接続/実Windows/実運用DBは未検証。

PR #137のInt32制約補強は、`display_order`を整数0〜2,147,483,647、非NULLの`recurrence_interval`を整数1〜2,147,483,647に限定した。追加16ケースで、小数/文字列/BLOB/上限超過/NULLの拒否、下限・上限のEF読込、周期2列のNULL一致・全単位の維持、拒否時のDB不変、旧CHECKの初期化/通常利用時の拒否を確認する。ローカルの重点32件と、レシート80件を含む初期化・構造の関連105件が成功。既存ケースの判定は維持し、構造改変テストだけ新しいCHECKの式に追随させた。ソース`3ba012047a61f8eb33353639e52b542d0a0fcb34`の[CI 42796001100](https://github.com/k-o-a-b-c/monetail/actions/runs/42796001100)で全API・Node・CI選択検証・Dockerビルドと通常/server/配布アーカイブの起動・永続化検証が成功。全APIのローカル重複実行は行わず、影響外は修正前PR先端`a633164`の成功記録を再利用した。初期化方針・Migrationなしの境界は変更しない。

レシートDB基盤は`ReceiptSchemaTests`の追加64ケースで、新規ファイルの現行モデル初期化、4表の列/複合PK/FK/RESTRICT/UNIQUE/CHECK/索引、同じIDの別家計簿利用と家計簿間参照拒否、金額・数量・日時・周期・列挙値の境界、Decimal TEXT精度と非課税NULL/課税0%、監査アカウント保護、子先行削除、親更新版競合、初期化再実行/同時実行、旧構造/制約/索引改変と参照破損の拒否・データ不変、全体ロールバック、キーセット用索引の逆走査を確認する。既存の旧科目スキーマ拒否テストは、参照先として実際の複合PKを再現するよう補正し、拒否・DB不変・通常API拒否の判定は維持した。ローカルの重点67件（新規64件＋既存構造3件）とAPI全1027件が成功。ソース`01762e1ef70f82aac3d7baed8fc87a1ec3a317a2`の[CI 42791719303](https://github.com/k-o-a-b-c/monetail/actions/runs/42791719303)でAPI全件・Node・CI選択検証・Dockerビルド・通常/server/配布アーカイブの起動と永続化検証が成功。影響外は基点main `81ff287`の[push Tests 42789683243](https://github.com/k-o-a-b-c/monetail/actions/runs/42789683243)と成功記録を再利用し、WinUI/Client/旧版/性能等は再実行していない。文書のローカルリンク・差分検査も成功。工程1のDB基盤だけを対象とし、計算・業務保存API・WinUI・実データ移行は未実装。実NAS・実運用DBは未検証。

パスキー自動命名/名前変更（DEC-143）は追加API53ケース（構成963件）とWeb10ケース（構成63件）。既存共有フレームワークのCBOR解析、9 AAGUID・ネットワーク順/混在順・未知/ゼロ/未設定/解析失敗・不定長/入れ子/重複/末尾データ、初回/公開/追加/復旧の実Identity検証後の命名、正常な登録を妨げないフォールバック、名前変更の本人/CSRF/Origin/直近5分境界/入力/同名/再保存/共通回数枠、保存失敗/途中認証変更の全体ロールバック、アカウント/全鍵メタデータ/セッションの保持、実署名カウンター更新との並行保存、改名後の同じ鍵でのログインを確認する。途中でUserManagerのアカウント更新が停止状態を戻すケースを検出し、名前保存を既存パスキーストアだけへ限定した。Webは現在名/最後の鍵/Trim/UTF-16/HTTP失敗の入力保持/同じダイアログでの再確認/明示保存/取消を確認する。ローカルの最終関連51件とNode63件が成功。ソース`15820400eec61178404e8261735f74b6a7be18c7`の[CI 42448809239](https://github.com/k-o-a-b-c/monetail/actions/runs/42448809239)でAPI全963件・Node63件・Dockerビルド・通常/server/配布アーカイブの起動と永続化検証が成功。変更前と影響外は基点main `1bf4507`の[push Tests 42443999636](https://github.com/k-o-a-b-c/monetail/actions/runs/42443999636)とCIの成功記録を再利用し、Client/WinUI/旧版/性能等は再実行していない。構成合計1671件を全件実行した意味ではない。DBモデル/初期化/Migration・IdentityのWebAuthn検証・外部ライブラリの変更なし。MITの出典/ライセンスをpublishへ同梱する。実ブラウザー/実認証器/実NASと保存先UIでの操作は未検証。

PR #133のInvoke調査では、失敗[42437180469](https://github.com/k-o-a-b-c/monetail/actions/runs/42437180469)のUIスレッド上で未接続（Loaded=False/Root=True）のButtonをUIAへ渡した記録を確認した。テスト基盤の未接続/非表示ボタン操作を修正したが、**元の0xC0000005のネイティブ原因・因果関係は未特定**。製品/API/Client/DBは変更しない。`InvokeAsync`は毎回現在の対象を取得し、UIスレッド・Loaded・XamlRoot/host・有効/可視状態の確認と、Peer→Invokeパターン→実行を同じUIターンで行う。待機時間/上限は変更せず、操作を再送しない。無効ボタンの読み取り検証と押下を区別し、人物削除後の再表示も実際に見える戻るボタンを経由する。診断は対象識別子・状態・スレッド・呼出箇所・各ネイティブ段階・ダイアログ開閉だけとし、Content/Tag/Text・HTTP本文・接続先・認証情報を記録しない。

調査中の参加者認証復帰は[42441799329](https://github.com/k-o-a-b-c/monetail/actions/runs/42441799329)で5セット、[42442464782](https://github.com/k-o-a-b-c/monetail/actions/runs/42442464782)で5セット、[42442825873](https://github.com/k-o-a-b-c/monetail/actions/runs/42442825873)で通常1セット、最終で1セットの**計12セット（各セットに同一/別アカウントの2経路）**が通過し、0xC0000005は再現せず。前3実行は後続の別のテストで失敗したため全体成功とは扱わない。初回[42441556316](https://github.com/k-o-a-b-c/monetail/actions/runs/42441556316)はUIAに不要な寸法条件の拒否、[42442246317](https://github.com/k-o-a-b-c/monetail/actions/runs/42442246317)は非同期置換のビルドエラーで、参加者反復を実行していない。最終ソース`255f4bb98c3ff80e80858c7dc7ba46a1515ad6f6`の[CI 42443115034](https://github.com/k-o-a-b-c/monetail/actions/runs/42443115034)は両Releaseビルド・製品起動・全画面275確認が成功。331件のネイティブInvokeは全件復帰し、6件の意図的な不正対象（未接続・別スレッド・非表示・無効・閉鎖後2件）をPeer作成前に拒否。参加者では前回Closed前のOpenedを検出しなかった。過去のネイティブ故障箇所を確定するダンプはなく、根本解消とは扱わない。Windows x64 CI以外の実機/手動/IME/DPIは未検証。影響外はPR先端の[Tests 42437796915](https://github.com/k-o-a-b-c/monetail/actions/runs/42437796915)の成功記録を再利用し、API/Client/旧版/性能を再実行していない。

科目管理と人物期間フィルター（DEC-142）はソース`0a1e172305680acc836158e5a6952ce5fe54fc14`の[CI 42437515048](https://github.com/k-o-a-b-c/monetail/actions/runs/42437515048)で、WinUI製品/検証用Releaseビルド・製品起動・新旧画面の合成HTTPシナリオ1件（270確認）が成功。ローカルでは既存の科目Client関連106件と差分検査が成功した。API/Client/DB/旧版は変更せず、基点main `8c073c2`の[push Tests 42430919394](https://github.com/k-o-a-b-c/monetail/actions/runs/42430919394)とCI判定の成功記録を変更前・影響外の根拠として再利用した。構成合計1608件を全件再実行した意味ではない。初回[42436823052](https://github.com/k-o-a-b-c/monetail/actions/runs/42436823052)はケース追加に伴い全体60秒で打切り、待機上限を120秒へ変更した（各条件15秒・検証項目/判定は維持）。次の[42437180469](https://github.com/k-o-a-b-c/monetail/actions/runs/42437180469)では既知の0xC0000005が参加者認証復帰のInvoke中に再発し、直前ボタンはLoaded=False/Root=True。参加者テストも表示済みボタンの選択/Loaded待機へ揃え、最終実行では再発なし。ネイティブ層の根本原因は未解決。実API・実Windows手動操作・実IME/物理キー・125/150/200%/モニター間DPI・ARM64は未検証。

初期マスタの固定設定検証を32ケース追加。税区分4件のコード/正式名称、Windows OAuthのClientId/種別/ConsentType/秘密・鍵・個別Settings/リダイレクト/権限/PKCE要件、JSONの不正値・集合の過不足を検証する。配列の順序/書式/重複を変えた正常系と、ConcurrencyToken・表示名/翻訳・Properties等の運用値変更も確認し、再初期化と通常APIでDB不変・不一致時の拒否を確認。ローカルの対象34件（既存正常系2件を含む）と既存初期化/構造25件が成功。ソース`8617c5a8201d8ed702f1f4d15e2817922b0f0239`の[CI 42427632942](https://github.com/k-o-a-b-c/monetail/actions/runs/42427632942)でAPI全910件・Node/Python・Dockerビルドと通常/server/配布アーカイブ起動が成功。変更前の成功根拠は[CI 42384924696](https://github.com/k-o-a-b-c/monetail/actions/runs/42384924696)と、その製品検証元[CI 42383801568](https://github.com/k-o-a-b-c/monetail/actions/runs/42383801568)。Client/WinUI・Windows保存・旧版/性能は未変更の成功記録を再利用。API契約・科目仕様・EFモデル・外部ライブラリは変更せず、実DBは変更していない。

現行モデルによるDB初期化・科目期間/収支編集は、ソース`b732021f2aba03ec8f36a02968078f909a47bc88`の[CI 42383801568](https://github.com/k-o-a-b-c/monetail/actions/runs/42383801568)でAPI全878件・Client全429件・Windows保存3件・WinUI製品/検証用ビルドと既存画面・Node・Python・Dockerビルド/通常/server/配布アーカイブ起動が成功。段階別Migration/Up-Downテストを、新規初期化/全表・PK/FK/UNIQUE/CHECK/索引/3トリガー/初期値の照合、再実行・同時実行、旧形式/欠損/参照破損/不正ファイルの拒否、失敗時の全体ロールバック、登録済み認証・各マスタ/税履歴/版の不変へ置換した。期間の両端・同日・省略/null・使用済み収支変更・期間を含む再送とClient DTO/認証再試行も確認。ローカルは影響対象247件、追加/CLI等51件、最終重点6件、Client102件と追加/重点13件が成功（重複あり）。基点main `b584cca865596e781068660b475d669a5c6790c0`の[CI 42373904027](https://github.com/k-o-a-b-c/monetail/actions/runs/42373904027)を変更前の根拠とし、未変更の旧Windows・計算/移行SQL・性能は成功記録を再利用。構成合計1576件を全件再実行した意味ではない。旧API DBの変換機能はなく、実DBの初期化・変更は行っていない。実NAS/実ブラウザー・新しい科目操作の実Windows手動確認は未実施。

科目Clientに合成HTTPの98ケースを追加。5操作/全コード/有効状態/空配列/UTF-16/部分更新/行版と順序版、通信再試行とrequestId、全操作の認証更新と本文保持、更新中の配列変更/別アカウント変更、2形式の409と対象ID、403/404の権限情報と非公開latest非参照、503を含む業務エラーと不正応答、認証失効/再ログイン/取消を確認する。ローカルReleaseは初回の新規97件と追加・重点を含む関連104件（重複あり）が成功。ソース`522c368be36303ac34d610b2d54988b94ffb9a83`の[CI 42372634577](https://github.com/k-o-a-b-c/monetail/actions/runs/42372634577)でClient全421件、Windows保存3件、WinUI製品/検証用ビルドと既存画面の起動・操作、Pythonが成功。基点main `64a159ca6f112668f42f247241df63d038c762f1`（PR #129マージ）の[CI 42371937136](https://github.com/k-o-a-b-c/monetail/actions/runs/42371937136)成功を変更前の根拠とし、API/Docker・旧版・性能の成功記録は再利用。構成合計1548件を全件再実行した意味ではない。API/DB/Migration・WinUI画面・外部ライブラリの変更なし。科目画面は未実装で、実API接続・実Windowsでの科目操作は未検証。

科目並び替えAPIに実HTTP/隔離SQLiteの61ケースを追加。無効科目・収支混在・欠番/同順位からの再採番、0件/1件/同順保存時の新版、家計簿Admin限定と全体権限の分離、入力/家計簿境界、全行属性/他の更新版の保持、旧版/同時保存/追加削除との競合、DB条件付きUPDATE、途中障害・降格/除外/認証失効の全体ロールバック、競合情報の保護と共有書込枠を確認する。ローカルReleaseで新規61件と既存の科目CRUD/支払方法並び替え147件が成功。ソース`95993d19b911c225732fe7375718cecfcf588ab8`の[CI 42369954389](https://github.com/k-o-a-b-c/monetail/actions/runs/42369954389)でAPI全858件・Node・Python、Dockerビルドと通常/server/配布アーカイブの起動検証が成功。基点main `a24574d20f6d09d23b88adf6fed23dd45a7729e8`（PR #128マージ）の[CI 42366871026](https://github.com/k-o-a-b-c/monetail/actions/runs/42366871026)成功を変更前の根拠として再利用し、変更前の全件確認と影響のないWindows系・旧版・性能テストは再実行していない。構成合計1450件を全件再実行した意味ではない。DB/Migration・CRUD仕様・Client/WinUI・レシート・既存税計算の変更なし。実運用DB・実Windowsからの操作は未検証。

科目CRUD APIに実HTTP/隔離SQLiteの108ケースを追加（構成API797件）。参加者の閲覧/家計簿Adminのみ更新・全体Adminとの分離、家計簿境界、正規化/UTF-16/無効科目の重複、収支/税区分/boolean/JSON/更新版入力、同時作成/再送/行更新、作成後の改名・削除と再送、RESTRICTと想定外DBエラー分類、独立した順序版、保存前/確定前の降格・除外・認証失効とロールバック、競合情報の失効後非公開、既存20/120回枠の共有を検証する。将来の取引参照はテスト専用FK表のみで再現し、改名時のJOIN表示と保存済み税区分/率の非更新を確認する。対象をローカルで確認し、ソース`684367bba852badcc809cafff8cc962231c4eaa4`の[CI 42361091410](https://github.com/k-o-a-b-c/monetail/actions/runs/42361091410)でAPI全797件・Node・Python、Dockerビルドと通常/server/配布アーカイブ起動検証が成功。影響のないClient/WinUI・旧版・性能テストは再実行していない。DBモデル/Migration・Client/WinUI・レシート・既存税計算は変更していない。基点main `92cab63`の[CI 42354526041](https://github.com/k-o-a-b-c/monetail/actions/runs/42354526041)成功を変更前の根拠とする。

科目設定の移設（DEC-139）は科目/税DB対象48ケースがローカルで成功。旧仕様の費用・発生区分/周期の組合せ20ケースを、新仕様の収支区分INSERT/UPDATE8ケースと、8列/モデル/Designer一致・DB上の収支変更可能性・適用済み旧構造の非自動変換3ケースへ置換したため、API構成は698→689件。名称・家計簿分離・参照・版・税区分/税率履歴・移行往復/全経路の既存検証は維持している。同じMigration IDが記録済みの隔離DBでは、Migrateしても旧11列・税履歴/科目/版が変わらず、initialize-dbの構造照合が拒否することを確認した。実DBは変更・初期化していない。ソース`3e7fbd6e0cecafdc1fc157685034a54524de33d0`の[CI 42351364987](https://github.com/k-o-a-b-c/monetail/actions/runs/42351364987)でAPI全689件・Node・Python、Dockerビルドと起動検証が成功。影響のないClient/WinUI・旧版・性能テストは再実行していない。変更前の根拠はmain `bfb9ce13563e4a74781b40bd44b1f310304d6d61`の[CI 42313249586](https://github.com/k-o-a-b-c/monetail/actions/runs/42313249586)成功を採用する。

科目・共通税区分/税率履歴のDB基盤（DEC-137・138）に57ケースを追加。API全697件の実行と、追加した初期API DB→最新版の全経路保持1件、Node53件、Python判定69件がローカルで成功（現構成API698件）。Nodeは作業環境で子プロセスの集計がファイル単位になったため`--test-isolation=none`で全53ケースを確認した。新規/既存API DB、旧列・認証/パスキー・人物・支払方法の保持、MigrationのUp→Down→Upと再適用、FK/UNIQUE/CHECK・無効科目の重複・将来の複合FK参照、初期値の組合せ、UTF-16境界、行版と家計簿版の独立、Decimal精度、期間両端・終了なし・全重複形・更新・同時挿入・トランザクション破棄を検証する。過去Migrationのテストは対象Migrationを明示し、人物表等を比較から除外せずに保持検証する形へ変更した。加えて初期API DBから最新版までの全経路でも、認証・パスキー・全体権限・家計簿参加/人物対応と全旧列の保持を確認する。最終ソース`3e9487f29a70879c5882c0337e2044f48d580014`の[CI 42294710994](https://github.com/k-o-a-b-c/monetail/actions/runs/42294710994)でAPI全698件・Node・Pythonが成功。Docker/起動検証は製品入力が同じ初回ソース`1a364d3`の[CI 42293545583](https://github.com/k-o-a-b-c/monetail/actions/runs/42293545583)の成功記録を再利用した。基点main `c0f6fee8581707b9806d302cb7811188d29b5acd`の[CI 42233302048](https://github.com/k-o-a-b-c/monetail/actions/runs/42233302048)ではAPI/Dockerが成功し、WinUI起動検証は既存の失敗。本変更でWinUI・旧版・税計算は変更しない。科目API・WinUI・取引テーブル・実データ移行、税履歴の実データ投入と管理運用は未実装。実運用DBへの適用は未検証。

配備用の名称をNAS固有からDockerサーバー用へ変更し、共通要件とTailscale構成例を分離した。旧リリースのアーカイブ名はそのまま保持し、新リリースで`monetail-docker-api-vX.Y.Z.tar.gz`を使用する。実装と配布構成の検証結果はPR #125のCIで確認する。

過去のPR #124ではNAS配布を既存のhostネットワークTailscaleコンテナ前提へ変更した。配布物とComposeからTailscale service・Serve設定・認証キー・state管理を削除し、APIのloopback待受と設定・データ保持を検証する。従前の検証結果は当時の構成の履歴として残す。

GHCR専用スクリプトのCI選択を最適化し、回帰8件を追加。Python全68件と差分検査がローカルで成功した。対象2ファイルの単独・混在変更、追加/変更/実行属性/削除での製品fingerprint維持、例外外への改名と共通/新規スクリプトの安全側判定、mainの成功記録再利用・記録欠落・過去の未検証API変更を確認する。変更前の根拠は基点main `ca21a81d6345eae2003efad69f53f580b1d065fa`の[Tests 42179499790](https://github.com/k-o-a-b-c/monetail/actions/runs/42179499790)成功を再利用する。

初回GHCR配布失敗の修正では、Registry検証13件を追加し、判定・配布条件の全60件とactionlintがローカルで成功。単一manifest/OCI index/provenance、固定参照・digest・platform不一致、既存タグ、404のRegistryエラー種別、401/403/429/500・redirect・通信断・タイムアウトと認証先制限を確認する。失敗実行[42174933547](https://github.com/k-o-a-b-c/monetail/actions/runs/42174933547)はamd64のpush成功後、ローカル`RepoDigests[0]`の形式検証で停止していた（その取得値自体はログになく断定しない）。公開済みの作業用イメージを読み取り、修正スクリプトでamd64のprovenance付きindexとarm64の単一manifestからpushログと一致するdigestを取得し、既存タグ・CPU誤指定の拒否と未公開版の404も確認した。両イメージのmanifest統合は`imagetools create --dry-run`で検証した。新しいタグでの実push・最終manifest公開・Releases作成はマージ後に未検証として残し、`api-v0.1.0`は変更していない。API本体・Dockerfile・NAS構成は未変更で、基点mainの[Tests 42173513238](https://github.com/k-o-a-b-c/monetail/actions/runs/42173513238)成功を変更前の根拠として再利用する。

GHCR/NAS配布はmainの同一SHAのpush Tests成功だけを採用し、PR/fork/別ブランチ/別SHA/失敗・取消の結果と不正バージョンを拒否する。新規5件を含む判定47件、actionlint、Bash構文、開発/NAS/配布用Composeがローカルで成功。最新APIのDockerビルドと通常/NAS/配布アーカイブ起動を確認し、初回再実行拒否、ソースなし配置、コンテナ再作成・停止バックアップ・隔離復元後のDB/DPキー/PFX/Tailscale state用ファイルのSHA256一致と管理者の保持が成功。環境の公開プロキシCAは一時DockerfileのBuildKit secretでビルド時だけ渡し、製品Dockerfileは変更していない。ソース`a2654e8a5896f4aa8f7336011bfce68bc24efc84`の[CI 42165673600](https://github.com/k-o-a-b-c/monetail/actions/runs/42165673600)で判定47件・API641件・Node53件・Client323件・Storage3件・旧版の計算/移行/保存/画面/ビルド/性能・WinUI製品/検証用ビルドと既存画面が成功。Docker/NAS/配布アーカイブ検証も成功し、今回はCI判定の変更に伴い全対象を実行した。GHCRへの実push・GitHub Releasesの実公開/匿名pull・ARM64ネイティブ実行はタグ配布後の確認が必要。実NAS・tailnet/Serve・実Windowsログイン・実バックアップ復元は未検証。

DEC-136の支払方法画面はソース`8c35292d8fa10cfb3f9d05b08f05272fadd15e6b`の[CI 42032155217](https://github.com/k-o-a-b-c/monetail/actions/runs/42032155217)でWinUI製品/検証用Releaseビルドと既存・新規画面1シナリオが成功。人物別グループ/本人優先/本日両端/全期間切替/空見出し除外、Admin/Editor/未対応Editor/Viewer、ダイアログ入力/作成再送/削除/行競合、全件並び替え/未知の追加行を含む409/取消/メニュー移動、認証復帰/降格/別アカウント非引継ぎ、Enter移動/900pxリサイズを合成HTTPで確認した。並び替え409への対象人物全行の追加は承認済みのAPI変更で、Clientに不整合応答の拒否3件を追加。API全641件・Client全323件・Storage3件・Node53件・Dockerビルド/既存/NAS起動は[CI 42029128688](https://github.com/k-o-a-b-c/monetail/actions/runs/42029128688)の成功ジョブを再利用（この実行全体は下記WinUI異常で失敗）。ローカルClient323件・API並び替え39件・差分検査も成功。構成合計1206件を全件再実行した意味ではない。DB/Migration・外部ライブラリ変更なし。CIのDPIは100%。実API接続・実Windows手動操作・実IME/物理キー・125/150/200%やモニター間DPI移動は未検証。

**既存ネイティブ異常の再発：** [CI 42029128688](https://github.com/k-o-a-b-c/monetail/actions/runs/42029128688)と[CI 42030851084](https://github.com/k-o-a-b-c/monetail/actions/runs/42030851084)で、PR #118と同じ人物再ログイン確認のAutomation Invoke中に0xC0000005が再発。後者の`ui-invoke.txt`では直前の保存ボタンがLoaded=False/Root=Trueだった。人物ダイアログのLoaded/操作可能状態を待機し、表示済みボタンを優先して取得するよう検証基盤を補強した。テスト項目・判定・UI Automation操作は維持し、診断ログも保存する。最終CIでは再発しなかったが、ネイティブ層の根本原因・再発可能性まで解決したとは扱わない。

支払方法Clientは新規67件で5操作のHTTP契約、作成下書きの正規化/requestId維持、全操作の認証更新、部分更新の省略/null、全期間と空所有者の版・同順位/欠番の保持、3形式の409最新情報、権限表示/最新権限/回数制限、入力・不正応答/競合対象の拒否、別アカウント検知/認証失効/取消を確認する。ローカルReleaseでClient全320件・差分検査が成功。API/DB/Migration/WinUI画面・外部ライブラリは変更していない。ソース`876bd41c3819fa66d9aa08b4ba539c74de23b67f`の[CI 42001480827](https://github.com/k-o-a-b-c/monetail/actions/runs/42001480827)でClient320件・Storage3件・WinUI製品/検証用ビルドと既存画面1シナリオが成功。初回の未変更の人物再ログイン画面確認でボタンInvoke時にネイティブアクセス違反0xC0000005が発生し、失敗ジョブだけを同一ソースで再実行して成功した（原因未特定、コード/テストの緩和なし）。未変更のAPI等は成功結果を再利用し、構成合計1203件を全件再実行した意味ではない。実Windowsの支払方法画面・実API接続は未検証（今回は合成HTTP）。

DEC-132支払方法並び替えAPIに39ケースを追加。権限/本人対応/認証境界、他家計簿/所有者/配列/UUID検証、有効期間外を含む全件再採番、所有者間の独立性、0件/1件/同順再保存の新版、対象人物のみの409最新順、同時保存、DB条件付き版更新、2行目の保存失敗・降格/対応変更/除外/認証失効の全体ロールバックを検証する。ソース`aa2209c8e533bf6bbd48bb7210d7442dade7e81f`の[CI 41978635513](https://github.com/k-o-a-b-c/monetail/actions/runs/41978635513)でAPI全641件・Node53件・Dockerビルド・既存/NAS起動確認が成功。ローカルReleaseの追加39件・Node53件・差分検査も成功。未変更のWindows系・旧版は既存成功結果を再利用し、構成合計1136件を全件再実行した意味ではない。DB/Migration・外部ライブラリ・WinUIの変更なし。実運用DBへの適用は未検証。

PR #116レビュー修正は、所有者の人物削除後の作成再送409・異内容再送409・新規要求の所有者必須400、現在のViewer/人物未対応Editor/除外済みの再送拒否とDB不変を確認する4ケース、APIの所有者UUID順・人物共通順・本人位置が異なる一覧を本人対応あり/なしで確認する2ケースを追加。ローカルReleaseでAPI全602件・Node53件・差分検査が成功。GET配列順は変更せず、表示側で本人優先・人物共通順にする契約を正本へ明記した。DB/Migration・画面の変更はない。関連CIはpush後に確認する。

DEC-135支払方法CRUDはソース`11762d6ce7039e183dd919234d0bd7a0256f1700`の[CI 41964968258](https://github.com/k-o-a-b-c/monetail/actions/runs/41964968258)でAPI596件・Node53件、Dockerビルドと既存/NASスモークが成功。新規API44ケースで全期間/空所有者の一覧、Admin/Editor/Viewerと現在の人物対応、名前/UTF-16/日付/部分更新、他家計簿、同時再送、旧版での並行編集/削除、DB条件付き更新の拒否、保存直後の権限/対応/認証変化と全体ロールバックを確認。参照拒否はテスト専用複合RESTRICT FKで409と順序版維持を検証し、本番の業務表は作成していない。ローカル新規44件・最終重点3件（重複あり）とReleaseビルド/差分検査も成功。PR #115はマージ済みで最新main79ba6dcを基点とし、DB/Migration/外部ライブラリ変更なし。未変更対象は既存成功を再利用し、構成合計1091件を全件再実行した意味ではない。将来の取引表追加時には参照FKと期間検証の接続が必要。WinUI・並び替えPUT・旧版データ移行・ポイントは対象外。実運用DBへの配備は未検証。

PR #115の支払方法Migrationテストは、既存人物の家計簿ID/人物ID・名前・有効期間・表示順・人物編集revisionを旧列のまま比較し、人物以外の既存表（認証情報・パスキーを含む）も比較する。支払方法の有無2ケースで実IMigratorによるUp→Down→Up、旧DDL/データの保持、再Upの全DDL一致・FK/UNIQUE/CHECK/所有者固定・UUID v4の個別再生成・再初期化での版維持・モデル一致を確認した。ローカルReleaseの関連移行25件、最終の往復2件と差分検査が成功。追加の往復2ケースにより、PRのAPI構成は既存3ケースを含め552件、全体1047件（全件実行の結果ではない）。本番コードは変更していない。

**Downのデータ消失リスク：** `AddLedgerPaymentMethods.Down`は支払方法表を削除するため、データがあっても全支払方法が失われる。再Upしても復元されず、人物の支払方法順序版も新UUIDになる。既存人物の旧列・認証データは保持されるが、この往復はバックアップ復元の代わりにならない。実データがあるDBでDownする前には、支払方法データを含むバックアップと復元手順が必要。これは現在のDownの挙動を検証・記録するもので、本番DBで実行したものではない。

PR #111の招待エラー文言を正本どおりに修正し、既存4ケースで表示全文の一致を検証した。ローカルNode53件・差分検査が成功。件数の増加やAPI/DB変更はない。

PR #111レビュー修正はソース`bf7d5e44de1902808a0f070803e61522e74d22a0`の[CI 41806291878](https://github.com/k-o-a-b-c/monetail/actions/runs/41806291878)でAPI547件・Node53件、Dockerビルドと既存/NASスモークが成功。追加8ケースでログイン状態別の暫定戻る文言/遷移、一時情報削除、終端エラーの操作非表示、通常通信失敗の導線維持、別アカウント→明示ログアウト/再ログインの間の記録保持・元の本人だけの明示GET再開を確認した。既存OAuthの回帰も成功。ローカルNode53件・差分検査も成功。業務API/DB/外部ライブラリは変更せず、未変更対象は以前の成功を再利用。構成合計1042件を一度に再実行した意味ではない。実ブラウザー・実パスキー端末・実NASは未検証。

DEC-117のWeb家計簿招待受領はソース`f6647b8a7690b404e413bf5ca778453c966b1d15`の[CI 41799288188](https://github.com/k-o-a-b-c/monetail/actions/runs/41799288188)でAPI547件・Node45件、Dockerビルドと既存/NASスモークが成功。追加HTTP2件・JS17件で匿名ページのHTTPS/CSP/no-store、明示参加・参加済み/利用不可、ログイン復帰とOAuth維持、応答喪失・再読み込み・404未確認・同じ本人の明示GET・別アカウント拒否・保存不能・トークン削除を確認した。ローカルもHTTP2件・Node45件が成功。業務API/DBの契約は変更せず未変更対象は既存成功を再利用。構成合計1034件を同時再実行した意味ではない。実ブラウザーのsessionStorage/履歴・Cookie・パスキー端末・実NASは未検証。Webレシート一覧とWindows招待受信は対象外。

DEC-116のWinUI 3参加者・家計簿招待管理はソース`81b33682d0a615648c65691de4c28e8280095d81`の[CI 41785660852（attempt 2）](https://github.com/k-o-a-b-c/monetail/actions/runs/41785660852/attempts/2)で成功。製品/スモークのReleaseビルドは警告・エラー0、製品起動と実コントロールの1シナリオ174確認が成功（合成HTTP・データ、RasterizationScale=1、撮影なし）。API順の維持・内部ID非表示、本人の役割/除外禁止と人物対応保存、候補の無効化、競合の入力/版保持と明示切替、除外確認、403後の入場制限、招待の新しい発行順・実クリップボードコピー・取消・応答喪失時の同一要求ID、成功応答一覧の利用、同じ本人の認証復帰と別アカウントへの非引継ぎを確認した。Client253件・DPAPI3件は[CI 41784414918](https://github.com/k-o-a-b-c/monetail/actions/runs/41784414918)の各成功ジョブを再利用し、以後Clientソースは不変。ローカルは新規Client27件、Releaseビルド、文書リンク/差分検査が成功。初期のPopup配置とスモークの仮想化行待ちを修正した。最終ソースのattempt 1では既存の人物再認証のAutomationPeer.Invoke中に単発のネイティブクラッシュ（0xC0000005）があり、WinUIジョブだけの同一ソース再実行は成功、原因は未特定。API/DBは変更せず、未変更対象も既存成功を再利用するため、構成合計1015件を同時に再実行した意味ではない。Windows 11実機の高DPI/モニター間移動、物理キー/IME、実サーバーの認証・招待運用は未検証。ブラウザーの家計簿参加画面は未実装。

DEC-115の全体Admin向けWebアカウント管理はソース`c785125d661b87f2ba7ccae300cb7b928bdeb6c1`の[CI 41769793422](https://github.com/k-o-a-b-c/monetail/actions/runs/41769793422)でAPI545件・Node28件・判定42件、Dockerビルドと既存/NASスモークが成功。追加35 APIケースと13 JSケースでHTML/JS/APIの権限、登録方式2種類・状態4種類、履歴/CLI復旧との分離、メモの上限/空欄/非公開/重複、移行による全既存表の保持・モデル一致、保存中の権限失効と原子性、5分境界と画面内再認証、要求ID再送・五十音/欠損/ID順・ダイアログ/URL破棄を確認した。既存の初期Admin・招待/緊急復旧・通常ログイン・Windows OAuthも回帰成功。初回CIは新設メモ表を含む既存スナップショット期待値1件が失敗し、更新後に成功した。ローカルは重点62件・モデル一致1件・保存/登録34件（対象の重複あり）とNode28件、文書リンク・差分検査が成功。未変更のWindows等は既存CI成功を再利用し、当時の構成合計988件を全件再実行した意味ではない。実ブラウザー・実パスキーデバイス・実NASは未検証。全体ロール変更・停止/削除・Web復旧は未実装。

PR #105の追加修正はソース`30f223183a2f7eb8aebbb4247284087633dc5942`の[CI 41747788416](https://github.com/k-o-a-b-c/monetail/actions/runs/41747788416)でAPI510件・Node15件・判定42件、Dockerビルドと既存/NASスモークが成功。追加8ケースで通常/初期管理者招待だけの取消、CLI復旧リンクと未知用途の不変、取消後の復旧WebAuthn成功、未登録の一般/初期管理者と利用中/鍵残存へのCLI復旧発行拒否・DB不変を確認した。ローカルは対象44件＋追加2件が成功。既存の緊急復旧・再登録・ログイン・Windows OAuthもAPI回帰に含む。未変更対象は以前のCI成功を再利用し、当時の構成合計940件を全件再実行した意味ではない。DBスキーマの追加変更はなく、実端末/実NASは未検証。

DEC-050・113の招待リンク/登録画面と利用者確認済みのCLI緊急復旧（DEC-114）は、ソース`429010cb0646a6f1b6a110c3b23860ee599ed5d3`の[CI 41744295834](https://github.com/k-o-a-b-c/monetail/actions/runs/41744295834)でAPI502件・Node15件・判定42件、Dockerビルドと既存/NASスモークが成功。復旧ではNULLを含む表示名と認証ID・ロールを保ち、自動ログインしない。初回ソース`dda6882`の[CI 41742917219](https://github.com/k-o-a-b-c/monetail/actions/runs/41742917219)は共通CIへNode検証を追加したため当時の全構成930件と旧版ビルド/性能・Compose・WinUIも成功し、後続では変更のない対象の成功を再利用した。同時点の932件は追加2ケース込みの構成合計で、最終回に全件を再実行した意味ではない。ローカルでも既存API484件・対象37件・最終の保持/取消3件とNode15件、actionlint・JS構文・文書リンク・差分検査が成功。実ブラウザー・実パスキーデバイス・実NAS配備は未検証。Web家計簿画面への遷移、CAPTCHA、退会、本人向けの自己復旧は未実装。

DEC-112自己登録はソース`e64a7164acccccc1a791eddd4ea77315530f76b1`の[CI 41731192843](https://github.com/k-o-a-b-c/monetail/actions/runs/41731192843)でAPI484件・判定42件が成功した。新規38ケースに加え、既存の招待登録・管理CLI・ログイン・Windows OAuth・家計簿APIを回帰確認した。製品APIは先行ソース`46ad9b6`と同一で、Dockerビルド・既存/NASスモークは[CI 41730305252](https://github.com/k-o-a-b-c/monetail/actions/runs/41730305252)の成功を再利用した。ローカルは新規37件、全API483件、その後追加したBearer境界と既存テストの補強を含む重点15件が成功。APIのReleaseビルド、文書リンクと差分検査も成功した。旧版・Client・Storage・WinUI等は変更がなく再実行していない。899件は構成合計であり今回の実行件数とは区別する。実ブラウザー・実パスキーデバイス・実配備は未検証。登録画面・CAPTCHA・退会・全パスキー喪失時の自己復旧は未実装のまま。

Docker NAS配備はソース`3f5bd7d249d70580e5d2de7464b3dcbe7c364a81`の[CI 41643374059](https://github.com/k-o-a-b-c/monetail/actions/runs/41643374059)で、構成848件、Compose設定、APIのDockerビルドと既存/NASスモーク、旧版ビルド・性能計測が成功した。今回は共通CIと判定処理の変更に伴い全対象を実行した。ローカルでもAPI433件・判定42件・Compose・actionlint・Bash構文と両Dockerスモークが成功。ローカルのDockerビルドには作業環境の公開プロキシCAをBuildKit secretでビルド段階だけへ渡したが、配布用Dockerfileは変更せず、CIではそのまま成功した。Tailscale公式イメージv1.104.4の取得とCLIも確認した。実NAS、実tailnet/Serveの証明書取得更新、実パスキー/Windows接続、実バックアップ復元は未検証。[NAS手順](../docs/deployment-docker-nas.md)の実機確認を行う。

人物の共通順設定と認証後の復元はソース`de2e0aebec793c5d8f5d60e90c69295bedf217e2`を[CI 41605779743](https://github.com/k-o-a-b-c/monetail/actions/runs/41605779743)で確認。判定41件とWinUIスモーク1シナリオが成功し、製品/スモークのReleaseビルドは警告・エラー0。共通順と通常の本人先頭表示、移動と取消の通信なし、全IDの一括保存、競合時の最新一覧への直接置換、通信失敗/応答喪失、権限変更を検証した。さらに同じ本人での再ログイン後の未保存順・元の全体版の維持と非自動保存、認証待ち中の人物増減、降格/除外後の保存禁止、未参加時のGET抑止、別アカウント/接続先への非引き継ぎも成功。API・Client・Storage・旧版等は変更がなく再実行していない。構成合計847件は変わらず、スモーク内の確認項目を個別のテスト件数に加算しない。実キー・IME・実端末DPI・実ブラウザーのパスキーは未検証。

### 過去の確認（索引）

以下は**当時の個別検証**であり、現在のテスト件数や全機能の成功を意味しない。コミットの完全なSHA、再実行の範囲、失敗・未検証事項を含む原文は[過去のCI検証記録](ci-verification-records.md)に保持する。

| 対象・詳細 | 当時のソース | CI | 特記事項 |
| --- | --- | --- | --- |
| [人物の共通順（初期操作）](ci-verification-records.md#ci-41598552778) | `04c962a1b3` | [41598552778](https://github.com/k-o-a-b-c/monetail/actions/runs/41598552778) | 当時は並び順保存中の認証失効後復帰が未実装（後続で実装） |
| [人物削除のWindows画面](ci-verification-records.md#ci-41593840818) | `1e320297c3` | [41593840818](https://github.com/k-o-a-b-c/monetail/actions/runs/41593840818) | 人物削除・競合・権限変化のスモーク確認 |
| [NuGetキャッシュと復元](ci-verification-records.md#ci-41586922999) | `c44a148d49` | [41586922999](https://github.com/k-o-a-b-c/monetail/actions/runs/41586922999) | 復元ヒット・再テスト抑制の実測、時間短縮率は保証しない |
| [WinUI構成整理前・画面撮影](ci-verification-records.md#ci-41418236183) | `c44a148d49` | [41418236183](https://github.com/k-o-a-b-c/monetail/actions/runs/41418236183)・[41418289696](https://github.com/k-o-a-b-c/monetail/actions/runs/41418289696) | 撮影は96DPI・合成データ |
| [WinUI構成整理・全体検証](ci-verification-records.md#ci-41424095869) | `8592ed02b4` | [41424095869](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095869)・[41424095689](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095689) | 当時の構成815件 |
| [ホーム上部1行化](ci-verification-records.md#ci-41433846188) | `3b61079676` | [41433846188](https://github.com/k-o-a-b-c/monetail/actions/runs/41433846188)・[41433992670](https://github.com/k-o-a-b-c/monetail/actions/runs/41433992670) | 1行ヘッダーと単発撮影 |
| [ホームの表示・ボタン調整](ci-verification-records.md#ci-41513466165) | `21d3925fb4` | [41513466165](https://github.com/k-o-a-b-c/monetail/actions/runs/41513466165)・[41513601351](https://github.com/k-o-a-b-c/monetail/actions/runs/41513601351) | ライト／ダークと登録表記 |
| [接続・認証・家計簿操作画面](ci-verification-records.md#ci-41519000314) | `b11ecc5b81` | [41519000314](https://github.com/k-o-a-b-c/monetail/actions/runs/41519000314) | 当時は製品起動からの自動復元が未接続 |
| [起動・ログイン復元・待機表示](ci-verification-records.md#ci-41521479704) | `1386b5e460` | [41521479704](https://github.com/k-o-a-b-c/monetail/actions/runs/41521479704)・[41522184140](https://github.com/k-o-a-b-c/monetail/actions/runs/41522184140) | 製品起動、待機表示、タイムアウト、撮影 |
| [人物追加・編集・入力・認証復帰](ci-verification-records.md#ci-41532331268) | `41193cdd94` | [41532331268](https://github.com/k-o-a-b-c/monetail/actions/runs/41532331268)・[41532322990](https://github.com/k-o-a-b-c/monetail/actions/runs/41532322990) | 当時の構成826件、更新後一覧と入力復元 |
| [人物画面の単発撮影](ci-verification-records.md#ci-41532325889) | `41193cdd94` | [41532325889](https://github.com/k-o-a-b-c/monetail/actions/runs/41532325889) | 96DPI画像で表示領域を確認 |
| [人物追加・編集のContentDialog化](ci-verification-records.md#ci-41541869671) | `eeea52d32a` | [41541869671](https://github.com/k-o-a-b-c/monetail/actions/runs/41541869671)・[41541869672](https://github.com/k-o-a-b-c/monetail/actions/runs/41541869672) | ContentDialogの操作・保存・復帰 |
| [ContentDialogの単発撮影](ci-verification-records.md#ci-41541866024) | `eeea52d32a` | [41541866024](https://github.com/k-o-a-b-c/monetail/actions/runs/41541866024) | RenderTargetBitmap撮影（背後は画像外） |
| [CIの選択・成功記録再利用](ci-verification-records.md#ci-41581065430) | `8b82a4ae61` | [41581065430](https://github.com/k-o-a-b-c/monetail/actions/runs/41581065430) | 構成847件をCIで確認 |
| [CIの手動撮影](ci-verification-records.md#ci-41581145112) | `8b82a4ae61` | [41581145112](https://github.com/k-o-a-b-c/monetail/actions/runs/41581145112) | 撮影専用で成功記録を保存しない |
| [成功記録11種類の照合](ci-verification-records.md#ci-41582333257) | `a8b256e852` | [41582333257](https://github.com/k-o-a-b-c/monetail/actions/runs/41582333257) | 判定41件のみ再実行、他は成功記録を再利用 |

## 実行方法（リポジトリ直下）

ブラウザーのスクリプト検証はNode.jsで`node --test tests/Monetail.Api.Tests/registration-ui.test.js`を実行する。DOM・WebAuthn・通信だけを合成し、製品のスクリプト本体を実行する。CIではAPIテストと同じ検証入力に含め、Node検証が失敗した場合はAPIの成功記録を保存しない。

```sh
# .NET 10 / LinuxまたはWindows
dotnet test tests/Monetail.Api.Tests/Monetail.Api.Tests.csproj --configuration Release
dotnet test tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj --configuration Release
# .NET 10 / Windows
dotnet test tests/Monetail.Windows.Storage.Tests/Monetail.Windows.Storage.Tests.csproj --configuration Release
dotnet build src/Monetail.WinUI/Monetail.WinUI.csproj --configuration Release --runtime win-x64 -p:Platform=x64
# 旧版の.NET 8検証
dotnet run --project tests/CalculationTests/CalculationTests.vbproj
python3 -m unittest discover -s tests -p 'test_*migration.py' -v
# CI選択
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

WinUI 3の製品起動と画面スモークは[専用ワークフロー](../.github/workflows/winui.yml)と[run.ps1](Monetail.WinUI.Smoke/run.ps1)で確認する。通常CIでは画像を取らない。撮影はActionsの「WinUI 3 foundation」を手動実行し、対象ブランチと`capture=true`を指定する。撮影専用ブランチへのpushは不要。 参加者の認証復帰だけを5回反復する調査は、Windowsで`run.ps1`へ`-RepeatParticipantRecovery`を指定する（検証用Appの`--repeat-participant-recovery`）。全画面を反復せず、通常CIは1セットとする。`ui-invoke.txt`の同じinvoke番号で`target/create_peer/get_pattern/invoke_provider/returned`を追跡し、ダイアログのOpened/Closing/Closedと照合する。Invoke方式は[公式のButtonAutomationPeer](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.automation.peers.buttonautomationpeer?view=windows-app-sdk-1.8)のInvokeパターンを使用する。

旧Windowsでは`dotnet run --project tests/PersistenceTests/PersistenceTests.vbproj`で画面・実SQLiteを確認する。性能計測は同コマンドに`--measure-receipt-reads CSVのパス`を渡し、通常テストと区別する（[測定条件](../docs/34-receipt-read-performance.md)）。APIのDocker確認は[起動・運用](../docs/42-api-foundation.md)を参照する。

NAS構成の確認はLinux上のDocker・Bash・OpenSSL・Python 3・curlを使い、APIイメージをビルドしてから`bash tests/Monetail.Api.Tests/nas-smoke.sh monetail-api:ci`を実行する。隔離したDB・秘密ファイルとComposeプロジェクトは終了時に削除し、実Tailscaleサービスは起動しない。

## 必要なCIだけを実行する

自動起動はPRの作成・更新と`main`へのpushだけとする。作業ブランチのpushとPRで同じ検証を二重起動しない。WinUI 3の通常検証も[Tests](../.github/workflows/tests.yml)から[再利用ワークフロー](../.github/workflows/winui.yml)を呼び出し、独立したpush/PR起動は行わない。PR更新時は同じPRの古い実行を中止する。

[select_tests.py](../.github/scripts/select_tests.py)が変更と参照プロジェクトから検証対象を選び、[ci_plan.py](../.github/scripts/ci_plan.py)が同じ入力の成功記録を確認する。PRは分岐点以降の全差分を対象にし、最後のコミットが文書だけでも、PR内の未検証・失敗した変更を取り落とさない。改名・削除も扱い、未分類の変更は失敗させる。

| 変更 | 確認対象（同じ入力の成功記録があれば再利用） |
| --- | --- |
| 文書だけ | 軽量なCI判定。PR内に未検証・失敗した変更が残る場合は、その検証を実行 |
| APIソース／テスト | API、製品ソースではDockerも確認 |
| Clientソース | Client・Windowsの実DPAPI・WinUI 3 |
| Clientテストだけ | Client |
| Storageテストだけ | Windowsの実DPAPI |
| WinUI 3／スモーク | WinUI 3のビルド・製品起動・画面スモーク |
| Dockerfile等／Compose／NAS配備 | Dockerfile等はDocker、Compose・NAS配備はDocker＋Compose |
| 旧Windows・計算・SQL等 | 依存するテスト、ビルド、必要なら性能計測 |
| GHCR専用の`registry_image.py`・`test_registry_image.py` | 常時実行するPython検証のみ。製品テストの指紋にも含めない |
| 共通CI・判定処理・SDK／ビルド設定 | 全対象。WinUI専用ワークフローの変更はWinUIのみ |

GHCRの例外は`.github/scripts/registry_image.py`・`.github/scripts/test_registry_image.py`の完全一致だけに限定する。他の新しいスクリプト、選択・成功記録の共通処理やそのテストは従来どおり全対象へ影響する。Python検証は毎回実行するため、この2ファイルを製品の指紋から除いても検証を省略しない。`main`ではGHCRだけのpushでも全入力の成功記録を照合し、未検証の製品変更や記録欠落があれば必要な検証を実行する。

再利用には、各検証に影響するファイルのパス・Gitオブジェクト・実行属性から求めた指紋を使う。製品だけでなくテスト、参照先、CI定義、共通ビルド設定を含め、チェックアウトしたPRのマージ後ツリーで照合する。入力が変われば別の記録となる。既存の未分類ファイルは指紋から黙って落とさず、保守的に全検証へ含める。

[成功記録用アクション](../.github/actions/ci-proof/action.yml)は、各検証が成功した後にだけ、指紋と元のCIへのリンクをActions cacheへ保存する。中断・失敗を成功として保存しない。復元は完全一致だけとし、記録なし・不一致・破損・キャッシュ障害は再実行する。Windowsで保存した記録もLinuxの判定ジョブで照合する。キャッシュは最適化であり、GitHub側で消去されたら検証し直す。再利用の有無と元の成功CIは実行概要へ表示し、再実行していないものを今回の検証件数へ加算しない。

`main`では現在の全対象の成功記録を確認し、以前のpushが未検証でも次のpushで取り落とさない。PR専用キャッシュを`main`の成功記録として流用せず、GitHubのキャッシュ公開範囲に従う。main取り込み後の確認はPRとの二重起動とは区別する。

NuGetパッケージは[共通アクション](../.github/actions/nuget-cache/action.yml)でキャッシュする。OS・アーキテクチャ・ジョブ・実SDK版・プロジェクト/依存設定でキーを分け、並列ジョブが部分的なパッケージ群を同じキーへ保存しないようにする。旧版の`packages.config`用フォルダーも含める。依存設定が変わった場合は既存パッケージを種として復元できるが、NuGetの依存解決とテスト/ビルドは省略しない。成功結果の再利用とは別の仕組みで、キャッシュ障害時も通常の復元へ戻る。

撮影は必要時の手動実行だけで行い、撮影結果を通常検証の成功記録として登録しない。全検証をやり直したい場合はActionsの「Tests」を`force=true`で手動実行する。成功済みの入力でも再実行できる。

## 新版Windowsの手動確認（未実施）

製品起動から接続・ログイン復元・家計簿の行き先へ進む処理を画面へ接続した。人物・科目・支払方法の追加・編集・削除確認・日付入力と共通Enter操作は接続済み。共通順の設定と同一アカウントでの認証後復元も接続済み。時刻入力のUIは未接続。実装後は[43](../docs/43-passkey-authentication-design.md)・[58](../docs/58-ledger-foundation-design.md)・[59](../docs/59-input-controls-design.md)の契約に沿って確認する。ARM64、100／125／150／200%の物理端末、モニター間移動・ハイコントラスト・日本語IME・実キー入力、実ブラウザーのパスキー、本番配備・実DB移行は未検証。

## 旧Windowsで画面を手動確認する手順（利用者環境では未実施）

手順は[旧Windows版の手動確認チェックリスト](legacy-manual-checks.md)を参照する。**利用者環境での実施は未確認**。必ずDBのバックアップ・テスト用コピーを使い、旧アプリと新アプリで同じDBを同時利用しない。
