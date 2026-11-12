# 旧WinForms → 新WinUI／API：機能横断・最終比較台帳

[文書索引](INDEX.md) | [旧版との機能・イベント比較](legacy-new-comparison.md) | [現行WinUI実コード監査](legacy-new-winui-implementation-audit.md) | [計算直接比較・DB境界](legacy-new-data-calculation-audit.md) | [レシート実装工程](ledger-receipt-implementation.md)

## 結論と比較の「終了」の意味

**旧版に存在する業務機能の範囲を漏れなく洗い出し、現新版の実装・仕様との対応と未決定領域を一覧化する静的比較は、今回の台帳で一巡**。旧版の「未完成機能」を完成済みと数えず、旧実装の誤りを新仕様にしない。ただし、**新版未実装の振替・残高・支払メモ・集計などについて、新旧実行結果が一致したという意味ではない**。また旧版と新版の実Windows UI、運用DB、複数PCによる直接対照はしていない。

- **実装確認**：該当コードと関連テストから現時点の機能を確認した。アプリ全体の同等性を示さない。
- **継承**：業務上の意味を維持。表示方法やIDを含めた完全一致ではない。
- **確定変更**：既存の正本にある、利用者確認済みの新版ルール。古い癖へ戻さない。
- **未接続**：API・Clientや編集状態モデルは存在するが、WinUIから操作できない。
- **後続**：新版の当該業務API/画面がまだ存在せず、新旧の実行対照は実施不可。
- **要判断**：旧機能の採否・参照API・端数／表示の正しさについて、承認済みの新版契約がない。無断で旧版を移植しない。
- **要再現**：コード上で問題が生じ得る経路があるが、実行で発生を確定していない。まず既存テストへ小さい再現ケースを加える。

**網羅の単位は画面／入力操作／集計式／読み書き・障害経路。** PR内の他の3つの監査文書と重複する数を足して「合計何件の不具合」とは呼ばない。具体的なDECとAPI外形は正本優先。

## A. 全業務機能の機能別対応表

| 機能の粒度 | 旧版で実装されている処理・条件 | 新版の対応と現段階 | 判定 |
| --- | --- | --- | --- |
| A01 起動・接続 | XML設定の既存SQLiteファイルを検査、列不足なら停止。勝手にDB生成・移行をしない | WinUIからHTTPS APIへ接続し認証。DBはサーバー管理者が明示的に初期化。WindowsからSQLは発行しない | 確定変更・基盤実装 |
| A02 認証・本人 | `Environment.UserName`と人物のアカウント名等の文字列比較が一部にある | パスキーアカウント／家計簿の人物ID対応、Admin/Editor/Viewer権限 | 確定変更・実装 |
| A03 家計簿切替 | 旧版は接続先DBを設定し利用、家計簿単位の招待・参加者はない | 家計簿の作成・切替、参加者／招待管理のAPI・UI | 新規実装 |
| A04 人物マスタ | 読込・本人優先・分類集計の対象者選択。旧マスタ管理画面は未完成 | CRUD・期間・共通並び順・人物IDと本人対応、競合／権限管理 | 新規実装 |
| A05 支払方法マスタ | 所有人物・表示順・本人以外へ人物名を前置する旧表示 | CRUD・人物別順・有効期間、表示時だけ`（人物）名称`を付加。保存名は変更しない | 確定変更・実装 |
| A06 科目マスタ | 収支の分類、旧税対象／軽減既定、旧マスタのseq順 | 科目CRUD・全件並び順・収支区分・初期税区分・有効期間。既存取引の金額を名称変更で書換えない | 確定変更・実装 |
| A07 店舗マスタ | 旧`m_shop`の未使用店も店舗名候補へ出す、科目既定・ポイント既定 | 新店名候補はレシート履歴由来。店舗マスタの同等機能は新版未導入 | **要判断（旧店舗マスタの採否）** |
| A08 税率マスタ／履歴 | 旧`m_tax_rate`コード＋レシートグループ税率＋明細課税フラグ。選択は固定8／10%の分岐もある | 新しい全家計簿共通4区分・日付別履歴はDB基盤とJSON済み。全件GET・同期CLI・WinUI計算／候補は未接続 | 確定変更・D4後続 |
| A09 旧ポイント | テーブル・クラスはあるがフォームは非表示、ポイントの登録／読込処理は未完成 | 新レシートにポイントなし。旧版完成機能として同等性を要求しない | 要判断・採否保留 |
| A10 起動後の一覧構成 | 旧ホームはレシート・振替・残高の3 Gridと3種の年次集計タブ | 新ホームではレシート一覧が利用可。振替／残高一覧、年次集計はまだない | **新版残業務は後続** |
| A11 年月と前後移動 | 一覧タブでは前後月、集計3タブでは前後年。年月は直接入力できる | レシート一覧は指定月／全期間と前後月・年月文字入力。年次画面は後続 | 確定変更・一部実装 |
| A12 一覧の対象人物フィルタ | レシートは支払方法所有者、振替は移動元または先の所有者。残高一覧には適用されない | レシートの支払者・支払方法・店名・年月検索を実装。振替の両側フィルタと残高は後続 | レシート実装／他後続 |
| A13 一覧の取得順・ページ | 旧`GetReceipts`は該当年月の全階層を直接取得。単一画面で再計算 | 新APIは親7項目を最大100件・カーソル。WinUIは先読み・初回300件補充と明示続行 | 確定変更・実装 |
| A14 旧一覧の人物0人警告 | 対象者0人の明細を含むレシートの行を薄桃色に着色 | 新APIの親7項目には明細対象人物情報がない。旧警告をそのまま表示できない | **要判断（旧警告の採否）** |
| A15 保存額／再計算の差分 | 旧一覧は保存済み合計NULLなら比較不可、不一致なら保存額・再計算額・差額をツールチップ表示 | 新一覧は保存済み合計のみ、詳細GETに保存時合計がない。旧方式の比較UIは未設計 | **要判断（監査用表示の採否）** |
| A16 一覧の金額範囲超過 | 旧版は再計算がInt32超過したレシート行のみ「範囲超過」を出し、他は続行 | 新APIは保存時に最終Int32を検証、一覧は保存済み値を表示。旧過去値の読込・再計算エラーは別機能 | 確定変更／旧警告の採否は要判断 |
| A17 レシート入口・復帰 | 旧版は新規・選択行ダブルクリックで別フォーム。保存成功で年月をレシート月へ切替・対象行選択 | 新WinUIはメイン領域を専用編集画面へ切替。保存後は**元一覧条件・位置をできるだけ保持**する仕様、保存ボタンは未接続 | 確定変更・D5後続 |
| A18 親の基本入力 | 日付、時刻、店・所在地、税込/税抜、端数、支払方法、レシートメモ | 入力欄と編集モデルあり。新規は税込・切り捨てを初期選択、税設定の手動状態を保持 | 確定変更・UI実装 |
| A19 日時の省略入力 | 旧`OriginalDateTimeTextBox`は日付を過去補完し不正時に前月や今日へフォールバック。時刻は0時へ戻す | 新`DateTimeInput`／DateInputBox／TimeInputBoxは決定済みの実在日付・過去側・入力保持等を採用。旧入力の偶発的な補正は再現しない | 確定変更・基本接続済み |
| A20 店名／所在地／品名の候補 | 旧マスタ＋本人履歴、店名に応じた所在地／品名、旧最新は取引日基準 | 新APIの部分一致/NFKC/かな統一・更新日時順の候補／補完、Client読取あり。WinUIの候補表示・確定イベント未接続 | 確定変更・D5未接続 |
| A21 店名変更と所在地 | 旧版は店変更後も所在地を残す場合がある | 新版は別店確定時に所在地クリア（仕様確定）、現WinUIでは未接続 | **D5未接続** |
| A22 支払方法・税込・端数補完 | 旧同店履歴を参照。初期選択済みや編集時は上書きしない | 新APIの3項目補完済み。Clientには手動由来の判定があるがWinUIからAutofill未呼出 | **D5未接続** |
| A23 旧科目初期候補 | 店舗マスタ既定科目か、本人の直近履歴の第1グループを使い分ける | 新補完APIは科目を返さない。候補化・採否はまだ決めていない | **要判断** |
| A24 科目グループ | 旧版はレシート内で追加・選択・削除・メモ、他科目との統合もできる | 新版は1科目1グループ、選択中だけ明細表示。重複追加禁止、削除後選択解除。旧統合操作なし | 選択・削除は実装／統合は要判断 |
| A25 明細の編集 | 品名・単価・数量・値引・税対象・対象人物・メモ、行選択でフォームへ読込 | 新版は一覧6列＋フォーム、流用・削除・並び替え・未反映3択、費用／発生区分を追加 | 継承＋確定変更・D3実装 |
| A26 明細の対象人物 | 旧複数選択リスト。追加後や科目切替後も保持、0人は保存可能 | 新チェック領域・選択引継ぎ・保存済み人物保持。初回は0人、取引日での人物期間制約 | 確定変更・UI実装。期間警告は後続検証 |
| A27 旧数式・不正値 | 旧金額式評価は不正を0、数量不正は1、割引率不正を0〜100へ補正 | 新版は不正な入力を保持して検証エラー、勝手に計算上の0/1へ置換しない | 確定変更・Client/UI実装 |
| A28 税額の保持単位と初期値 | 旧グループ税率＋明細課税フラグ。旧過去日入力は固定8/10%初期選択に弱点 | 新版は明細実税率、非課税NULLと課税0を区別、期間別税率と科目から初期選択 | API・編集モデルあり／D4未接続 |
| A29 計算の基本式 | 値引後額・同税率税抜合算・税込・floor/half_up・正負独立集計 | **旧本物のVB実計算と新APIのC#計算を24正常ケースで直接対照し成功** | **限定ケースで一致** |
| A30 会計合計と参考額 | グループ／明細はそれぞれ別丸めで、会計レシート合計と異なる場合がある | 新APIも同一の会計額・参考額の区別を持つ。WinUIで参考額・合計をまだ計算表示できない | 計算実装／D4表示未接続 |
| A31 レシート全体の保存条件 | 旧「いずれかの科目に明細あり」で保存でき、空グループを残し得た。時刻0と日付変更時に確認 | 新APIは全科目に1明細以上、日付別税率・マスタ期間も検証。時刻0等の確認は採否未決 | 確定変更＋一部要判断・D5未接続 |
| A32 レシート書込と競合 | 旧SQLiteトランザクション・`revision_token`、更新時は子全消去再挿入 | 新APIは子ID維持・CAS・全階層latest・requestId冪等再送・確定前権限照合、Clientの不変JSON | API/Client実装／WinUI D5未接続 |
| A33 レシート流用 | 旧元ReceiptのSeqを0にして、そのオブジェクトから即新規保存する | 新版は元ID・版を維持し別スナップショットをPOST、元のレシートを変えない | 確定変更・Clientモデルあり／D5未接続 |
| A34 保存後の更新通知 | 旧レシート・振替保存で一覧再取得と支払メモ推算額の更新通知 | 新版は保存APIのみ実装。振替・残高・支払メモの更新通知は未設計 | **D5＋後続** |
| A35 明細の費用／発生区分 | 旧版は明細のこの区分を保存していない | 新明細に費用区分／発生区分、親に繰返し周期を保存。定期明細なしの周期は拒否、取引自動生成はしない | 新規仕様・UI基本実装 |
| A36 レシートの削除 | 旧版はフォームで確認、版競合時は警告・一覧再読込 | 新API DELETEはrevisionと冪等requestIdを検証。WinUI削除と確認・結果不明・成功後復帰は未接続 | API/Client実装・D5未接続 |
| A37 振替新規／編集／流用／削除 | 旧フォームに全処理、同口座禁止、名称・移動元先・金額・メモ、版競合・保存時確認 | 新版の振替業務テーブル／API／WinUIがない | **後続・実行対照不可** |
| A38 振替名と移動元／先補完 | 旧同名履歴で移動元・移動先を段階補完、更新日降順。本人の関与条件は旧SQL個別 | 新版の振替候補検索API・画面の発動条件・手動保護は未設計 | **後続・要判断** |
| A39 振替の符号と残高 | 旧0円／負額も許可、元から減算・先へ加算。負額は方向逆転 | 新版の振替の金額制約／残高推算契約は未設計 | **後続・要判断** |
| A40 振替一覧と人物フィルタ | 旧一覧はレシートと別Grid、振替は元／先の双方を絞り込み対象 | 新版はレシート一覧のみ。振替一覧の独立表示・人物両側検索は未設計 | **後続・要判断** |
| A41 残高スナップショット | 旧日付単位1件、支払方法別に残高・評価額・メモ、残高未入力行は保存しない | 新版の残高DB／API／UIなし | **後続・実行対照不可** |
| A42 残高入力の検証 | 旧評価額のみ拒否／1口座以上必須／整数入力。日付重複は失焦時と保存時に確認 | 新版はDBで日付一意性と競合保証を決める必要がある。旧UIだけの事前確認では原子性不足 | **後続・要判断** |
| A43 残高推算・反映 | 旧は最終日末残高を基準に翌日〜対象日末のレシート・振替を合算。口座ごとに推算額と手入力を比較し反映可能 | 新版の推算計算・画面なし。元/先変更・日境界・最終日/ゼロ・NULLを固定例にする | **後続・直接対照不可** |
| A44 支払メモ一括入力 | 旧全口座を列挙、推算残高・確認日・［本日反映］・メモ。未記録の口座も表示 | 新版の支払メモ業務API／UIなし | **後続・実行対照不可** |
| A45 支払メモの保存・終了 | 旧全行UPSERT、変更なしでも終了時Yes/No/Cancel。最前面切替あり | 新版の画面離脱・変更検知・競合・上書き防止・最前面機能は未決定 | **後続・要判断** |
| A46 支払メモの推算更新 | 旧レシート等の更新通知時に選択口座か全行を再計算。振替の移動元先変更後の旧口座に通知漏れの可能性 | 新版では変更前後の関連口座を失効させる／全件再取得のどちらかを決める | **旧版不具合候補・後続** |
| A47 分類年次集計 | 旧12か月＋年計、支払者と明細対象人物、部分／完全一致、1人按分 | 新版の人物別期間集計API／UIがない。0人完全一致・按分・負額端数の実行対照は後続 | **旧版計算を調査済み／新版未実装** |
| A48 収支年次集計 | 旧支払方法別の正負独立額、収入/支出/差額を表示切替。会計合計と差が出る場合あり | 新版の収支年次集計API／UIがない | **後続・直接対照不可** |
| A49 残高年次集計 | 旧推算月・評価額切替・実測／推算セル着色。欠測口座を0で月計するケースあり | 新版の残高年次集計API／UIがない | **後続・NULL/0の意味を要判断** |
| A50 年次のUSD換算 | 旧各月の月末付近のJPY/USD中値で個別口座換算、小数2桁、月合計は円合算後換算 | 新版の為替レートマスタ・集計API／UIがない。レート欠損／円合計と個別丸め差の採否が必要 | **後続・要判断** |
| A51 グラフ | 旧年次残高合計の折れ線。欠損USDの月でもプロット時に0へ変換する | 新版なし。欠損と0を同値にしない改善が妥当だが未承認 | **後続・要判断** |
| A52 集計の内訳遷移 | 旧集計表の月・年計セルダブルクリックで`DetailViewForm`を開く。ただし元の対象者／収入限定等の条件を落とす経路あり | 新版の内訳API／UIなし。元のフィルタを引き継ぐか明示決定が必要 | **旧版不具合候補・後続** |
| A53 内訳粒度・例外表示 | 旧内訳はグループ／明細表示切替、Int32範囲超過時に「集計不可／範囲超過」を表示 | 新版なし。参考額の計算境界と、表示条件を元表と合わせる | **後続・要判断** |
| A54 旧マスタメンテ画面 | 旧Formは空の雛形、起動メニューの入口も非表示 | 新人物・支払方法・科目の管理は独立画面として完成を進めている | **旧機能の未完成を新版欠落扱いしない** |
| A55 共通キーボードとヘルプ | 旧独自Textbox/ComboBox/Radio等はEnter次フォーカス、各画面の「？」にキー説明。メインAlt←/→・Ctrl+N、レシートCtrl+T/G等あり | 新版はIME優先・Enter/Shift+Enter・明細Alt+Enter・全体Ctrl+Enterを定義。旧補助キーとツールチップは採否要判断 | **一部確定変更・補助キーは保留** |
| A56 エラー／同時更新 | 旧の親更新版検査はレシート・振替・残高に存在、支払メモは版なし。計算範囲超過表示や入力巻き戻しを別処理 | 新版のレシートAPIはCAS+冪等操作、マスタの権限と期間照合。未実装の他業務の版・結果不明はこれから設計 | **レシート強化／他業務後続** |
| A57 通信中・認証失効 | 旧DB同期呼出しでHTTP結果不明・認証失効はない | 新版は接続先設定、パスキー・トークン更新、要求中断・古い応答破棄・認証復帰・書込結果不明を区別 | **新規・基盤実装** |
| A58 Web／スマートフォン | 旧版はWindowsアプリ単独 | 新版のREST APIはWindowsから利用中。Web入力画面は別工程 | **新規・後続** |

## B. 実装コードから確定できた優先的な検証候補

| 優先 | 旧版との関係・新版のコード経路 | 今後の判定方法 |
| --- | --- | --- |
| **P0・要再現** | `ReceiptSearchState.ApplyText()`は`AllPeriods`でも`MonthText`を先に検証する。`MainWindow.Receipts.ApplyReceiptTextAsync()`は失敗すると`ReceiptMonth.Focus`するが、全期間中はその欄が無効 | **年月へ`bad` → 全期間ON → 店名入力 → 検索**。全期間検索なら年月下書きは検索を妨げないことをClient xUnit＋WinUI Smokeで固定。現コードは拒否する経路を持つ。実機未確認 |
| **P1・要再現** | マスタ更新通知`ReceiptMasterChanged`はレシートの支払方法・追加科目候補を再構成するが、登録済み科目行の`RenderReceiptGroups`・人物選択欄の`RenderReceiptDetailPeople`を呼ばない | レシート編集を開いた状態で**人物名／科目名を変更**して、フォーム上の名称・選択集合の見た目が最新になるか確認。単一クライアントの更新通知と他クライアント変更は別経路 |
| **P1・要再現** | マスタ名の再描画で、レシート一覧の**既に選択していた人物／支払方法がマスタ一覧から除外**されると、セレクタの項目がnullになっても`ReceiptSearchState`のIDは残り得る | 使用履歴なしの人物／口座を選んだ状態で権限内削除→マスタ更新→検索を確認。表示中の選択肢と適用中API条件が矛盾しないことを検証。**その削除がAPIの制約上不可なら再現不能と判定** |
| **P1・仕様判断** | 旧レシート一覧の**対象人物0人行着色・保存済み合計と再計算額の差異・再計算範囲超過**は、新API親7項目だけからは再現できない | **表示を残すか／捨てるか**を決めてから必要なデータ取得設計・コストを評価。別GET/集計や既存一覧APIの無承認拡張をしない |
| **P1・仕様判断** | 旧グループの**科目変更→別グループへ統合**は新WinUIにない。旧版では統合元メモ・税率を失い得た | 旧操作を廃止するか、安全な明細移動機能として提供するか決める。新DBでは明細の実税率を維持できるので単純な旧統合は避ける |
| **P1・未接続** | 正式税率履歴GET／CLI、税率初期値／候補／会計額共通計算をWinUIへ接続できていない | D4で実装し、制度導入前NULLと3/5/8/10等の端点、非課税と課税0、表示差額・金額超過を新旧固定例と照合 |
| **P1・未接続** | 既存の新API/Clientの原子保存・同一`requestId`再送・`409 latest`をWinUIから行えない | D5で入力保持・結果不明・再認証・競合／削除・再取得を実API／WinUIでシナリオテスト |
| **P2・後続** | 振替・残高・支払メモは旧モデル・UI・SQLが存在するが新業務テーブルなし | 実装前に旧版の**日末残高、符号付き振替、対象者両側検索、評価額、メモ閉じる動作**を業務仕様として固定 |
| **P2・後続** | 旧集計／内訳で**親表と内訳のフィルタ不一致、人物按分、円合計とUSD丸めの差**がある | 集計APIを設計するとき元のフィルタを全部引き継ぐか、旧版欠陥として修正するか明示し、旧計算の固定例と新表示を比較 |

**ここは「実装してよい確定チケット」の一覧ではない。** P0/P1でも旧仕様・新版正本に変更を要する場合は承認を得る。問題候補は実行テスト・再現条件・期待結果を揃えてからバグ確定とする。

## C. 網羅性の照合記録と未実行の範囲

**旧版を機能の基準として調べた入口（主要ソースと付属品）**：

- メイン・一覧／全画面切替：[ViewForm](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm.vb)、[レシート・振替一覧](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_List.vb)、[残高一覧](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_Balances.vb)
- レシート／振替／残高／支払メモ：[RegisterForm](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterForm.vb)、[RegisterTransferForm](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterTransferForm.vb)、[RegisterBalanceForm](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterBalanceForm.vb)、[RegisterPaymentMemoForm](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/RegisterPaymentMemoForm.vb)
- 3年次集計＋共通例外・内訳：[分類](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_ListStatistics.vb)、[収支](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_Expenses.vb)、[残高・USD](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_BalanceStatistics.vb)、[共通](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/ViewForm_Statistics.vb)、[内訳](../HouseholdAccountsBeta/HouseholdAccountsBeta/Control/DetailViewForm.vb)
- 算出／DB：[Receipt](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/Receipt.vb)、[ReceiptDetail](../HouseholdAccountsBeta/HouseholdAccountsBeta/Data/Data/ReceiptDetail.vb)、[Calculation](../HouseholdAccountsBeta/HouseholdAccountsBeta/Logic/Calculation.vb)、[DataAccess](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccess.vb)、[集計／為替SQL](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DataAccessList.vb)、[起動DB検証](../HouseholdAccountsBeta/HouseholdAccountsBeta/DataAccess/DatabaseStartup.vb)
- 共通入力・静的UI：旧`Control`配下7 Designer、[日付時刻](../HouseholdAccountsBeta/OriginalControls/OriginalDateTimeTextBox.vb)、[旧ComboBox](../HouseholdAccountsBeta/OriginalControls/OriginalComboBox.vb)、[旧Textbox](../HouseholdAccountsBeta/OriginalControls/OriginalTextBox.vb)、[旧ListBox](../HouseholdAccountsBeta/OriginalControls/OriginalListBox.vb)、[旧Radio](../HouseholdAccountsBeta/OriginalControls/OriginalRadioButton.vb)、[旧Check](../HouseholdAccountsBeta/OriginalControls/OriginalCheckBox.vb)。旧モデル（レシート・振替・残高・確認日・為替・マスタ・統計）と税マスタXMLも参照
- 新UI：[MainWindow.xaml](../src/Monetail.WinUI/MainWindow.xaml)、[レシート一覧](../src/Monetail.WinUI/MainWindow.Receipts.cs)、[編集](../src/Monetail.WinUI/MainWindow.ReceiptEditor.cs)、[明細](../src/Monetail.WinUI/MainWindow.ReceiptDetails.cs)、[人物](../src/Monetail.WinUI/MainWindow.Persons.cs)、[支払方法](../src/Monetail.WinUI/MainWindow.Payments.cs)、[科目](../src/Monetail.WinUI/MainWindow.Categories.cs)、[接続](../src/Monetail.WinUI/MainWindow.Connection.cs)
- 新API／Client：[レシート保存](../src/Monetail.Api/Features/Monetail/Ledgers/ReceiptManagement.cs)、[子保存](../src/Monetail.Api/Features/Monetail/Ledgers/ReceiptPersistence.cs)、[検証](../src/Monetail.Api/Features/Monetail/Receipts/ReceiptValidation.cs)、[マスタ照合](../src/Monetail.Api/Features/Monetail/Ledgers/ReceiptMasterValidation.cs)、[Client書込](../src/Monetail.Windows.Client/Ledgers/ReceiptWriteRequest.cs)、[Client通信](../src/Monetail.Windows.Client/Transport/ReceiptWriteTransport.cs)、[一覧検索](../src/Monetail.Windows.Client/Receipts/ReceiptSearchState.cs)
- 証拠レベル：[計算24件の直接比較](legacy-new-data-calculation-audit.md)、[API・Client・Smokeの自動テスト範囲](../tests/README.md)、[旧版マニュアル](01-features.md)、[新版の実装入口](ledger-receipt-implementation.md)

**静的照合での補足**：旧日付コントロール`StringToDateValue`には`ElseIf isMonthComplement AndAlso parsedDate > DateTime.Today`が**2回連続して現れる**ため、後の`AddYears(-100)`分岐は到達不能。新版の2桁年の世紀補完・過去日判定は承認済みの新ルールとし、旧版の実装上の取りこぼしまで再現しない。

**実際には実施していないこと**：旧Windows実機での連続キー・IME・高DPI比較／新Windowsと実APIでのレシート保存成功〜復帰のE2E／新版未実装業務の金額実測／実運用の旧SQLiteファイルの分析・変換・リハーサル。これらを「全機能を比較した」と誤認させない。旧データ移行は後回しの決定を保持し、ここから自動で進めない。

## D. この比較をどう使うか

1. **次の新実装（D4/D5）**にはA17〜A36の「未接続」を受入チェックとして添付し、P0/P1の再現テストと画面接続を先に行う。
2. **振替／残高／メモ／集計へ進むとき**にはA37〜A53をその機能の設計入口にし、採否が必要な項目だけ利用者に確認。元ソースの丸め・符号・日付・表示条件をCodexへ渡せる状態にする。
3. 仕様・実装の変更が確定したら**正本へ記載し、この台帳の判定列だけ更新**。この比較台帳で新API外形や税率・移行の既存決定を上書きしない。

**静的な旧新機能の棚卸しと比較台帳の作成はここで一区切り。** 未実装機能の実行一致／人の操作／運用DBの一致については、実装と検証が済むまで「未検証」を明示する。
