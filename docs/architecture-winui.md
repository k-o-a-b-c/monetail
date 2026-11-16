# 新Windows版 WinUI 3・Clientの構成

[← 04 全体構成の入口](04-architecture.md)

## 新版Windows版の構成

新版は**Windows 11のみ・.NET 10 LTS・C#・WinUI 3（Windows App SDK）**のアプリとする。プロジェクトは`src/Monetail.WinUI/Monetail.WinUI.csproj`。ターゲットは`net10.0-windows10.0.26100.0`、最小対応バージョンはWindows 11初版の`10.0.22000.0`。Windows App SDKは安定版1.8系列を使用する。初期ビルドと起動の検証はx64で行い、ARM64は未検証とする。

起動時の接続設定・ブラウザーログイン・保存ログインの復元・家計簿選択／作成／切替・ログイン情報・ログアウトを既存Clientへ接続済み。ホームと、左メニューを残した右側の人物一覧を表示する。起動中の待機表示も同じWindow内で扱い、具体的な表示・遷移は[58](ledger-windows.md#windows版の起動表示接続失敗時の操作)に従う。通常起動にサンプルデータは表示しない。人物の追加・編集・保存、日付/Enter入力、競合表示と同一アカウントでの再ログイン後の編集復帰を実装済み。人物削除の確認とAPI接続も実装済み。共通順設定と認証失効後の未保存順の復元も接続済み。

開発・CIでは非パッケージの自己完結した配置を使い、Windows App SDKと.NETランタイムを出力先に含めて起動を確認する。これは検証用の設定で、製品インストーラーの方式・更新方法・配布対象CPUを決定したものではない。WinUI 3のビルド・製品起動・同じXAMLによるスモークテストは専用の`.github/workflows/winui.yml`で行う。画像の取得は`preview/winui-*`ブランチでの単発実行に限定する。

API通信・認証・保存・日付と時刻の補完は`src/Monetail.Windows.Client/Monetail.Windows.Client.csproj`（`net10.0`）に分ける。画面はClientを参照し、入力欄・フォーカス・IMEは[59の入力仕様](59-input-controls-design.md)に従ってWinUI 3で実装する。API・認証契約・保存形式・通信単位を表示上の都合で変えない。旧VB.NET／.NET Framework 4.8は独立したアプリとして残し、新版から旧版の実行ファイル・プロジェクトを参照しない。レシート等の業務画面とインストーラーは後続とする。

画面、API通信、認証情報の保持を分ける。新版はAPI経由で読み書きし、画面へSQLやSQLite接続を持ち込まない。認証契約は[43](auth-windows-oauth.md#windows版のoauth認証dec-051)、起動時の遷移と段階移行は[58](ledger-windows.md#windows版のapi対応を先行する実装中)を正本とする。

通信回数をなるべく減らす。1つの操作で必要な処理と応答データをAPI側でまとめ、IDを取得してから同じ対象を再要求する往復を避ける。現在の認証・権限確認はAPI内部で維持し、振る舞いを変える統合は利用者の確認後に行う。

API接続先URLは、Windowsユーザーごとの`%LOCALAPPDATA%\Monetail\settings.json`に保存する。通常設定と認証情報は分け、JSONにトークンを含めない。認証情報は[43のDPAPI保存方針](auth-windows-oauth.md#windows版の認証情報保存)、初回入力と変更は[58の接続先設定](ledger-windows.md#windows版の接続先設定)に従う。

新版の変数・プロパティ・メソッド・クラス名は、意味と責務に合わせてC#の命名慣習で一から考える。既存フォーム・独自コントロールは操作・配置・計算条件を確認する資料として使い、必要な機能から実現する。機械的なVBからの変換を前提にせず、API対応と.NETの互換性を機能単位で確認する。業務モデルとDBの再設計方針は[58](ledger-foundation-core.md#新版の業務モデルdb設計方針未実装)を参照する。グラフ部品の選定とインストーラーの作成方式は実装前に具体化する。

## 正式レシート計算の利用基盤（工程6-D4-B実装済み）

Clientは`Monetail.Calculations`をProjectReferenceし、APIと同じ入力検証・Decimal計算・結果モデルを使用する。[参照関係とAPI側の責務](architecture-api.md#共通計算の参照関係工程6-d4-b実装済み)を参照。`ReceiptCalculations.FromSnapshot`/`FromEditor`で保存用スナップショット/編集状態を共通入力へ変換し、`Calculate`で通信なしに計算できる。ID・版・入力値・選択・下書き・未保存状態は変更しない。小数秒を丸めず共通検証へ渡し、未反映下書き/未確定の必須値は補正せず拒否する。支払方法等の保存専用条件を計算のために追加しない。

既存のClient書込要求・フォームの検証条件/メッセージは維持する。税率履歴の取得はD4-A、WinUIの参考額表示・再計算イベント・税率候補接続はD4-Cで接続済み。保存APIやマスタ不一致時の再取得接続は後続工程で、保存ボタンは無効。

## 支払方法のClient接続・画面

`PaymentMethodTransport`・`PaymentMethodsWorkspace`で一覧、作成、部分編集、削除、人物別並び替えを接続済み。確定契約は[業務モデル](ledger-core-schema.md#支払方法の作成編集削除apidec-135crud実装済み)と[並び順API](ledger-core-schema.md#支払方法の並び順採番保存api競合dec-132api実装済み)を参照する。

- `PaymentMethodsInfo`はAPI配列順・有効期間外の全行・0件人物を含む所有者別順序版を保持する。表示順の変更・本日有効への絞り込みは行わない。
- `PaymentMethodCreationDraft`は正規化した入力と所有者、requestIdを保持する。同じ作成の通信再試行には同じ下書きを渡す。自動的な業務再送や追加GETは行わない。
- `PaymentMethodChanges`は名前の省略と、`PaymentDateChange`による期間の省略/明示nullを区別する。更新版は行版、並び替えは対象人物の順序版を送る。全IDの一致・名前重複・使用済み判定はAPIが検証する。
- `CanManageOwner`は現在ロールと本人対応から操作可否を示す。Workspaceは人物管理と同じく権限をキャッシュせず、保存時の現在権限をAPIで検証する。403/404の最新権限と、操作別の409最新行/一覧/順序は型付きの業務例外で返し、勝手に表示状態へ適用しない。
- 認証更新・別アカウント検知・応答サイズ制限は既存Clientを共用する。WinUIの人物別一覧・追加/編集/削除ダイアログ・人物固定の並び順画面も[DEC-136](ledger-core-schema.md#windows版の支払方法管理画面dec-136winui実装済み)へ接続済み。認証復帰は元のアカウント・接続先・所有者・入力/旧版/requestIdを保持し、別アカウントへ引き継がない。並び順409だけは対象所有者の全行情報を応答へ追加し、追加GETなしで最新へ置換する。これ以外のAPI・DB/Migrationは変更していない。

## 科目のClient接続・画面

`CategoryTransport`・`CategoriesWorkspace`で一覧・作成・部分編集・削除・共通順の並び替えを接続済み。確定契約は[科目CRUD](ledger-core-schema.md#category-crud-api)・[並び替え](ledger-core-schema.md#category-order-api)を参照する。

- 型付きDTOの`CategoryInfo`は`IsEnabled`を持たず、`ValidFrom`・`ValidTo`をnullableのDateOnlyで保持する。`CategoriesInfo`は期間外を含む全行・API配列順・同順位/欠番・順序版を保持する。ID/更新版はGuid。未知の収支/税区分、不正な日付・型・ID・版・一覧は接続エラーにする。
- `CategoryInput`は名前・収支・初期税区分に任意の期間を加える。名前はTrim後1〜100 UTF-16単位。`CategoryCreationDraft`は入力とrequestIdを保持し、同じ作成の明示再試行に使う。
- `CategoryChanges`は名前・収支・初期税区分と、`CategoryDateChange`による期間の部分更新。文字列のnullは省略、期間はdefaultが省略・new(null)が境界解除。片方の期間だけでも現在行と組み合わせて前後関係を検証する。表示順は送らない。
- 行版と順序版を使い分け、並び替え配列は認証更新前にコピーする。`ClientSession`の認証更新・再ログイン・外部認証変更検知を共用し、成功一覧を使って追加GETしない。`CanManage`は現在の家計簿ロールによる表示用判定で、権限・重複・全IDの整合性はAPIが最終検証する。
- `CategoryRequestException`はコード・field・403/404の権限と、409の形に応じた`LatestCategory`/`LatestCategories`を保持する。最新行の対象IDも照合し、更新された収支・期間も受信する。自動取得・適用・保存はしない。401/403/404の非公開latestは読まない。429/503も型付き。既存Client操作の503処理は変更しない。

`MainWindow.Categories`・`CategoryOrder`・`CategoryRecovery`と`CategoryRow`で管理画面を接続済み。画面の表示・保存・競合・認証復帰は[科目管理画面](ledger-core-schema.md#科目管理画面)、人物の期間フィルターは[人物仕様](ledger-persons.md)を正本とする。レシートの親情報・科目入力は工程6-D2、明細編集は工程6-D3。税率履歴のClient取得/キャッシュは工程6-D4-Aで実装済み。計算表示と税率候補は工程6-D4-Cで接続済み。保存の画面接続は後続。

## 振替のClient読取接続

`TransferTransport`・`TransferJson`・`TransfersWorkspace`で一覧/詳細/振替名候補/支払方法自動補完の4種類のGETを接続済み。HTTPの正本は[振替API](ledger-transfer-api.md)、画面側の将来の適用規則は[振替画面](ledger-transfer-ui.md)を参照する。書込Client・編集モデルも下記の範囲で実装済み。WinUI一覧は下記の範囲で実装済み。入力・保存は下記の第3工程の範囲で接続済み。

- `TransferListQuery`・`TransferSuggestionQuery`・`TransferAutofillQuery`は省略/空文字と不透明カーソルを保持し、Trim・正規化・日付変換をしない。`focusTransferId`とcursorは空cursorも含め併用不可。APIの順序を保持し、自動再検索・ページ追加・キャッシュ更新・入力への補完適用は行わない。
- 一覧7項目/詳細8項目、候補の元文字列/`HasMore`、補完のnullable Guidの組を型付きで返す。UUID D形式、ローカル秒精度の`DateTimeKind.Unspecified`、正整数Int32、必須NULL/型/重複ID/対象IDを検証する。空文字メモだけをnullとして扱う。指定元があっても補完不能の両nullをそのまま返す。
- `ClientSession.ExecuteAuthenticatedAsync`と共通送信境界の認証更新・10秒タイムアウト・キャンセル・別アカウント検知を共用する。`TransferRequestException`はStatusCode/Code/Fieldと403/404の`PeoplePermissions`を保持し、DEC-280の403/404/200両nullを区別する。読取エラーのサーバーmessage/latestは公開しない。401・通信/不正応答・利用者キャンセルは既存Clientの例外で区別する。
- 一覧/詳細の成功本文だけをストリームで読み、構築時に一覧100件を検証する。候補/補完/エラーは既存64KiB上限を維持する。レシートClientや既存APIの読取契約は変更しない。

## 記録ホームの共通フィルター・振替一覧

`RecordSearchState`が既存`ReceiptSearchState`を保持し、年月・全期間・人物・支払方法を両一覧へ共通適用する。店名・振替名の下書きと確定値は独立させ、共通年月だけの確定では名前下書きを適用しない。表示・操作の正本は[ホーム3一覧](ledger-transfer-ui.md#3つの一覧と共通フィルター確定範囲)と[共通UI](../UI_DESIGN.md)。

`MainWindow.Transfers.cs`が`TransfersWorkspace.ListAsync`へ接続し、UI非依存の`TransferListState`で取得世代・ページング・中断／再開を管理する。`TransferRow`はAPI順と保存金額を維持し、支払方法名を既存マスタキャッシュから表示する。レシートと振替はそれぞれ読込・失敗・再試行を管理し、共通条件変更時は両一覧の旧世代を破棄する。マスタ取得はホーム文脈のCancellationTokenで共有し、片方の検索中断だけでは他方のマスタ待機を失敗させない。

共通フィルターは横1行の標準`ScrollViewer`、一覧は各`ListView`内のスクロールを使用する。初期の上下60:40・下段左右2:1を実装済み。振替入力・保存後focusは下記の第3工程で接続。残高データ・スプリッター・比率保存は後続。

## 振替のClient書込接続

`TransferWriteRequest.Create/Update/Delete`と`TransferWriteTransport`を`TransfersWorkspace.CreateAsync/UpdateAsync/DeleteAsync`へ接続済み。POSTは新規/流用登録で共用する。HTTP・入力値・再送の正本は[振替API](ledger-transfer-api.md)、画面の操作は[振替画面](ledger-transfer-ui.md)を参照する。編集モデルは下記の範囲で実装済み。WinUIでの書込接続は下記の第3工程まで実装済み。

- `TransferSnapshot`は業務6項目の型付き入力。要求は一操作の家計簿/対象ID/revision/requestId/JSONを生成時に固定し、毎回新しいHttpContentを作る。確定エラーを修正した保存は新しい要求、結果不明の再試行は同じ要求を使う。家計簿/操作の異なる窓口への流用を拒否する。既存`ClientSession`による認証更新・アカウント変更検知を共用し、更新後の再実行でも同じ本文を送る。
- 秒未満・UTF-16長/Unicode空白だけの名前・メモ改行・正整数Int32・空/同一支払方法ID・UUID v4のrevisionを入力時に検証する。日時はカレンダー値を時差変換せず送り、Trim・丸め・切り詰めはしない。空文字メモだけをnullにする。所有権/期間/権限はAPIが最終検証し、Client独自の制限は追加しない。
- POSTは201/空本文とLocationを検証し、`TransferCreatedInfo.TransferId`だけを返す。Locationは受信した生のヘッダーから同一家計簿・D形式の非空ID・振替詳細パスを検証し、同一オリジンの絶対URLも受理する。URLへ自動アクセスしない。共通送信境界に任意の成功ヘッダー検証フックだけを追加し、既存レシートの振る舞いは維持する。PUT/DELETEは204/空本文で完了し、IDや更新版を生成・推定しない。
- 業務エラーのStatusCode/Code/Field、403/404のPermissionsは既存`TransferRequestException`を共用する。PUT/DELETEの409 `LatestTransfer`だけを詳細GETと同じ8項目の検証で保持し、対象IDも照合する。両経路の受信revisionはUUID v4とRFC variantを検証する（振替/支払方法IDのUUID版は制限しない）。`request_conflict`は明示nullのlatestを検証する。共通エラーのmessage/409のpermissionsは読み捨て、入力や更新版へ自動適用しない。振替409を含むエラー本文は従来の64KiB上限を維持する。
- `TransferWriteUncertainException.Kind`のConnection/Timeout/InvalidResponseは保存成否を断定しない。利用者キャンセルは既存のOperationCanceledExceptionとして返し、サーバー側の保存取消を保証しない。認証更新中の通信/認証エラー・別アカウント検知は既存例外を維持する。自動業務再送・保存後GET・一覧/残高キャッシュ変更は行わない。

## 振替の専用入力・保存（第3工程）

`MainWindow.TransferEditor.cs`で`TransferEditorState`／`TransferDraft`を表示し、共通の日付・時刻と`FormKeyboard`を利用する。`MainWindow.TransferSuggestions.cs`は標準`AutoSuggestBox`の入力・プレビュー・確定を分け、画面内候補キャッシュ、デバウンス、IME保護、旧応答破棄、モデルの入力由来に従った補完を行う。既存内部TextBoxにも共通IME監視を登録する。

`MainWindow.TransferSave.cs`は`TransferWriteRequest`を一操作で一度だけ作成し、結果不明時は同じオブジェクトを保持する。保存後の共通条件調整は`RecordSearchState.ApplySavedTransfer`、最大300件の探索とフォーカス開始／404の1回復旧は`TransferListState`で管理する。API保存と一覧再取得は区別し、保存後の一覧取得失敗で再度保存しない。新規と既存に応じた確認・保存不可時の離脱保護を共通ナビゲーションに接続する。

第4-Aでは`MainWindow.TransferSave.cs`が409 `revision_conflict`の2択を表示し、破棄確認後の`TransferEditorState.AdoptLatest(true)`で通常編集へ戻す。`RenderTransferEditor`は競合中のコピー可能な読み取り専用表示と［最新を読み込む］を維持する。応答latestを使い、採用時の詳細GET・自動再保存は行わない。`request_conflict`やアクセス喪失・結果不明を採用対象にしない。

第4-Bの`MainWindow.TransferRecovery.cs`は入力値のスナップショットを作らず、元のWindow/コントロール/編集モデル/不変要求をそのまま保持する。通常の`ClearLedger`を迂回する再認証経路と、識別子・フォーカス/表示位置だけの復帰情報を使う。同じ本人/接続先で元家計簿へ復帰し、人物/支払方法を再取得する。マスタ失敗の一時読み取り専用はモデルの恒久的な権限拒否と分け、再試行後に保存済み両側のID（既存baselineの読み取り専用参照）も使って権限を再評価する。別本人/接続先・終了時は保持状態を破棄し、ディスク保存しない。

業務・復旧の正本と、残高連動で未完成の範囲は[第3工程の実装境界](ledger-transfer-ui.md#第3工程の実装境界)。API／DB契約、既存レシートの保存機能は変更していない。

## 振替の編集状態モデル

`Transfers/TransferEditorState`・`TransferDraft`はWinUIに依存しない編集状態。操作仕様は[振替画面](ledger-transfer-ui.md)を正本とする。`Create(DateOnly)`は呼出側の日付と00:00:00・未選択/金額未入力で開始し、`Edit(TransferInfo)`は保存済み8項目を保持する。時計・HTTP・requestId発行・保存・マスタ取得は持たない。

- `Draft.SetDate/SetTime/SetAmount`はnullableな値と未確定文字列を別に保持する。解析/入力補完/確定は後続画面が共通入力契約で行い、モデルは文字列を勝手に解析・補正しない。未確定文字列が残る保存準備では、以前の解析値を使わず拒否する。`HasUnsavedChanges`/`NeedsLeaveConfirmation`は実データと未確定入力で判定し、元の値へ戻れば解消する。メモnull/空文字は同値、空白は別値。選択由来だけの変更は除外する。
- `SelectFrom/SelectTo`と`TransferSelectionSource`で未選択/手動/履歴自動/保存済みを管理する。新規の`BeginAutofill`は元表記・取引日・手動指定元の文脈を返し、`TryApplyAutofill`はその文脈の応答だけを適用する。空白のみの名前は既存検証と同じ`string.IsNullOrWhiteSpace`で開始を拒否し、Trimや入力値の変更はしない。条件を変更して戻した場合・新しい文脈を開始した場合・別の編集状態・読み取り専用への移行では旧応答を採用しない。手動/保存済みの保護、両null時の自動値だけの解除、金額/メモを変更しない規則を守る。通信・デバウンス・候補表示は実行しない。
- `PrepareSave(Register/Update/ReuseRegistration)`は要求オブジェクトを生成せず、`TransferWriteRequest`の入力検証を共用して独立した6項目Snapshotを返す。更新だけ元ID/版を付け、流用は現在値を保持してID/版を除く。`PrepareDelete`は保存済みID/版だけを返す。失敗しても入力・基準・版は変更しない。`IsMidnight`/`HasSavedDateChanged`は警告条件を返し、日時未確定なら判定不能をnullで区別する。
- `RegisterConflict`は未保存入力/旧版を維持して最新8項目を別に保持する。`AdoptLatest(true)`による明示採用時だけ基準・入力・版を切り替える。競合からの復帰と403/404は`TransferReadOnlyReason`で表現し、入力/保存準備を拒否しても値の読取/コピーは妨げない。アクセス喪失を古い競合の採用で解除しない。実権限取得・認証復帰・確認/画面遷移・保存結果不明の要求保持は後続WinUIの責務。

## レシートのClient読取接続（工程6-A）

`ReceiptTransport`・`ReceiptsWorkspace`で一覧/詳細/履歴候補3種/親情報自動補完のGETを接続済み。正本は[レシートAPI](ledger-receipt-api.md)・[候補/補完API](ledger-receipt-suggestions.md)。WinUI一覧・画面用マスタキャッシュ基盤は工程6-Bで接続済み。書込Clientは工程6-Cで接続済み。編集状態モデルは工程6-D1で実装済み。親情報・科目・遷移の基本画面は工程6-D2で実装済み。明細フォーム/一覧は工程6-D3で接続済み。履歴候補UIは未実装。

- `ReceiptListQuery`・`ReceiptSuggestionQuery`・`ReceiptAutofillQuery`は入力時の条件を保持する。UUIDをD形式、取引日をInvariantのYYYY-MM-DDで送り、qの省略/空文字や不透明なcursorをTrim・正規化・破棄しない。ページの自動取得、業務エラー後の再試行、並び替えや候補/マスタキャッシュ更新は行わない。
- 一覧は要約7項目と`NextCursor`、詳細は親/グループ/明細/人物ID配列、候補は元文字列と`HasMore`、補完はNULL可の3項目を型付きで返す。日時は`DateTimeKind.Unspecified`のローカル秒精度、率はJSON文字列を正確な`decimal`/`decimal?`へ解析し、非課税nullと課税0%を分ける。JSON整数と数値文字列を混同せず、必須NULL項目の省略・不正コード/型・ID重複等を拒否する。APIの配列順を維持し、合計は再計算しない。
- Workspaceは既存`ClientSession.ExecuteAuthenticatedAsync`の更新/失効/アカウント変更検知を共用する。認証更新に伴う既存の再実行でも同じ検索条件を使う。業務エラーは`ReceiptRequestException`のStatusCode/Code/Field、403/404の権限情報を返す。401は既存の`AuthenticationRejectedException`、通信/不正応答は`ApiConnectionException`、利用者のキャンセルはキャンセル例外として区別する。読取エラーの任意のmessage/latestを採用せず、トークンを画面へ渡さない。
- 100件の有効な一覧は日本語の店名・所在地・メモで64KiBを超えるため、**一覧と詳細の成功本文だけ**をストリームから型付きDTOへ読み込む。本文全体の固定バイト上限・独自のメモ/子件数上限は追加しない。JSONの構造/必須項目、構築時の一覧100件、既存の10秒通信タイムアウトとキャンセルで保護する。DTOを保持するメモリは必要。候補（100文字×100件）・補完・読取エラー本文と既存APIは従来の64KiB上限を維持し、認証/方向転換/例外処理も共通の送信境界で扱う。

### レシート一覧の表示制御（工程6-B）

`MainWindow.Receipts.cs`が`ReceiptsWorkspace`へ接続し、UI非依存の`ReceiptSearchState`（確定条件/下書き）と`ReceiptListState`（取得世代/ページング/読込状態）を利用する。表示契約は[レシート画面](ledger-receipt-ui.md)を正本とする。画面遷移で進行中の一覧取得をキャンセルし、旧応答を適用しない。ClientSessionの認証を共用するが、一覧読込でWindow全体をbusyにしない。

`ClientConnection.Masters`は`LedgerMasterCache`を保持し、人物・支払方法・科目の初回取得を共有する。管理画面の既存Display処理で応答を反映し、遅延した古いマスタ取得で更新を戻さない。破棄条件はDEC-255に従う。`ReceiptRow`は保存済み合計額とマスタ由来の表示名だけを整形し、会計額を計算しない。

## レシートのClient書込接続（工程6-C）

`ReceiptWriteRequest.Create/Update/Delete`で操作ごとの新しい要求を作り、`ReceiptsWorkspace.CreateAsync/UpdateAsync/DeleteAsync`へ渡す。HTTP/再送の正本は[レシートAPI](ledger-receipt-api.md#保存リクエストと無変更putdec-258)、画面の保持/離脱/再認証は[レシート画面](ledger-receipt-ui.md#保存結果を確認できない場合dec-261)。

- `ReceiptSnapshot`・`ReceiptGroupSnapshot`・`ReceiptDetailSnapshot`は書込専用項目を型付けする。新規子のIDはnullとしてJSONから省略し、他のNULLは明示。マスタ所属/期間/実税率/費用区分と収支の整合性・計算はAPIが最終検証する。入力だけの型/長さ/列挙値/周期/重複等は要求作成時にも検証し、Trimや自動補正をしない。
- 要求は家計簿・操作・対象・revision・requestIdと送信JSONを固定し、元の入力配列を保持しない。通信再試行は**同じ要求オブジェクト**を渡す。入力配列を後で変更しても本文を変えない。確定エラーを修正した新しい保存には新しい要求を作る。別の家計簿/操作のWorkspace呼出しには流用できない。別アカウントへの下書き非引継ぎは後続WinUIの責務で、同一Sessionの認証変更検知は既存処理を共用する。
- `ReceiptJson`でDecimal文字列/整数/NULLを分け、DateTimeの年月日時分秒をそのまま送る。Kindによるタイムゾーン変換はせず、秒未満の入力は拒否する。成功は本文なしの201/204を検証してTask完了だけを返し、存在しないID/版を生成・取得しない。自動GET、キャッシュ反映、無変更PUTの省略は行わない。
- 確定業務エラーは`ReceiptRequestException`のStatusCode/Code/Field/Reason、403/404のPermissionsで返す。409は共通のmessage/field/permissions（nullを含む）も受理する。fieldは型付き例外へ保持し、messageと409のpermissionsは読み捨てる。書込409の`LatestReceipt`だけは詳細GETと同じ検証（対象ID・全階層・型・NULL・精度・重複等）を共用。**書込409だけ**をストリーム読取にし、大きなlatestを扱う。他API/他ステータスの64KiB、10秒タイムアウト、方向転換/認証の境界を維持する。latestの採用・マスタ再取得・保存禁止への移行は呼出し側の明示操作であり、Clientは入力を変更しない。
- `ReceiptWriteUncertainException.Kind`はConnection/Timeout/InvalidResponseを区別し、保存成否を断定しない。利用者キャンセルは既存のOperationCanceledExceptionのまま返し、送信後のキャンセルで保存取消を保証しない。認証更新中の通信失敗は既存ApiConnectionException、認証失効/別アカウント変更も既存例外を保つ。認証更新に必要な既存の再実行でも同じ本文を送り、通信失敗による自動業務再送はしない。

入力画面・結果不明/競合/認証復帰の表示・候補UIは後続。API・DB・Migrationは変更しない。

## レシート編集状態モデル（工程6-D1）

`Receipts/ReceiptEditorState`はWinUIに依存せず、`Create`／`Edit`で親・科目グループ・明細・対象人物を保持する。操作仕様の正本は[レシート画面](ledger-receipt-ui.md#レシートの編集開始更新流用登録dec-276)。

- 可変な`Header`・`Draft`と、反映済みの`Groups`を分離。`HasUnappliedChanges`と`HasUnsavedChanges`を別々に比較し、両者を`NeedsLeaveConfirmation`へ集約する。明細値・人物配列・ベースライン・保存用DTOは複製して外部変更を分離し、対象人物は集合として比較する。
- 科目／明細のローカル`Key`は保存IDとは別。新規保存IDはnull。既存IDは更新時に維持し、削除後に同じ既存科目を保存前に再追加した場合も元GroupIdを使用する（既存APIの子ID契約）。内容は自動復元しない。明細を動かして元の順序へ戻せば元の疎なdisplay_orderも維持できる。
- 選択切替は`PendingTransition`を保留し、`ResolveTransition`で適用／破棄／中止する。検証・次の初期値取得を先に済ませ、失敗時に元の選択や入力を更新しない。ダイアログ・フォーカス・エラー表示は呼出側の責務。
- 初期値は`ReceiptCategoryDefaults`のコールバックへ科目IDと現在の取引日時を渡して解決する。`ReceiptTaxSelection`で未選択／非課税／課税0%を分ける。`RefreshNewDraftDefaults`は未変更の新規明細フォームだけを明示更新し、保存済み値や手入力を上書きしない。モデルは時計・税率履歴取得・計算を持たない。
- `PrepareSave(Register/Update/ReuseRegistration)`は未反映入力を拒否し、独立した全階層`ReceiptSnapshot`と対象ID/版を返す。更新だけ既存ID/版を維持し、流用登録は現在値のまま保存済みID/版を除く。編集中の空グループは許可し、保存準備で拒否。要求生成を伴わない既存書込検証を共用し、明細／全体の追加検証フックを用意する。金額計算やマスタ参照の検証は呼出側・APIに委ね、別の計算方式を作らない。
- requestId生成・HTTP送信・競合採用・認証復帰は実行しない。後続画面は選んだ保存方式で工程6-Cの不変要求を一度作り、結果不明時にはそれを維持する。再試行時に編集モデルから要求を作り直さない。

## レシート基本入力画面（工程6-D2）

`MainWindow.ReceiptEditor.cs`と同じ製品XAMLを使用する。`HomePage`の共通ヘッダー下で`RecordContent`／`ReceiptEditorPage`を切り替え、一覧のモデル・選択・スクロールを保持する。親／科目操作は工程6-D1の`ReceiptEditorState`を使用し、DB・SQL・認証トークンを画面モデルに持たせない。詳細GETは既存`ReceiptsWorkspace.GetAsync`、名称・候補は`LedgerMasterCache`を使用する。

離脱時は入力イベントの通知に頼らず現在のコントロール値を読み取り、未確定の日付／時刻も保護する。空欄へ戻した任意項目は元のNULL／空文字を維持して未変更判定を復元する。既存ナビゲーションの確認と通常終了の取消を共通化し、未承認の画面切替でも編集中の状態を捨てない。`TimeInputBox`は`DateInputBox`と同じ確定境界で既存Client補完を使う。標準ListViewの`SingleSelectionFollowsFocus=false`で科目のフォーカスと確定選択を分ける。画面の動作・暫定の保存禁止・残る実装は[UI正本](ledger-receipt-ui.md#工程6-d2の実装境界)を参照。

税設定の由来はUI非依存の`ReceiptParentInputState`で項目別に保持する。`SelectTaxInclusion/SelectTaxRounding`は同値でも手動にし、`TryAutofill...`は手動/既存保存値を拒否する（HTTPは呼ばない）。状態をReceiptEditorStateの値比較から分け、由来だけの変更は未保存にしない。WinUIは標準RadioButtonのClickを明示操作、Checkedを値同期として扱い、`RenderReceiptTaxValues`は由来を変更せず描画する。`ReceiptRecurrenceInput.Parse`で間隔空欄と不正を区別し、不正時は入力文字列・最後の有効なモデル値を維持する。業務契約は[UI正本](ledger-receipt-ui.md#新規親情報の初期値dec-277)・[自動補完の正本](ledger-receipt-suggestions.md#radiobuttonの明示選択と補完保護dec-278)を参照。

## レシート明細フォーム・一覧（工程6-D3）

`MainWindow.ReceiptDetails.cs`は既存の`ReceiptEditorState`へ画面内操作を接続する。`ReceiptDetailInput`は未確定の数値文字列を可変下書きから分け、全数値の解析後だけ下書きを同期する。単価・単価値引の整数式は旧版と同じ+/-/*の優先順位・途中Int32範囲とし、解析失敗を0へ置き換えない。更新済み行と未反映フォームは別に保持する。

一覧表示は`ReceiptDetailRow`、人物選択は標準CheckBoxと単一のTab停止位置・上下フォーカス移動を使用する。選択変更を一度元へ戻し、標準ContentDialogで未反映下書きを解決した後だけ新しい対象へ移動する。共通`FormKeyboard`に明細のIME追跡を含め、明細領域のPreviewKeyDownでAlt+Enterを扱う。業務仕様と未接続部分は[UI正本](ledger-receipt-ui.md#工程6-d3の実装境界)を参照。

## 新版Windows版の配布・削除（未実装）

新版はインストーラー形式で配布する方針とする。アンインストール時に、そのWindowsユーザーの設定・認証情報も削除するか選べるようにする。対象はこのPCに保存した新版のデータで、サーバー上のアカウントや家計簿データは削除しない。アプリ内の専用データ削除操作は保留にし、初期実装の必須機能にはしない。

インストーラーの作成ツール、インストール範囲と更新方法、削除選択の初期値などは後続で設計する。インストーラーとアンインストール処理はまだ実装していない。

## 共通税率履歴のClient読取・キャッシュ（工程6-D4-A）

`TaxRateTransport`は全件GETの4項目を`TaxRateInfo`へ読み込み、率はDecimal文字列/NULL、日付はDateOnlyで保持する。成功応答は既存ReceiptJsonの逐次読取を共用して64KiBを超える履歴も扱い、他の応答・エラーの制限は変更しない。`TaxRatesInfo`はコピー済みの読取専用コレクションで全期間・配列順を保持し、`IsValidOn`だけで期間の両端を含めた照合ができる。候補化/初期選択はD4-Cの範囲であり、ここでは行わない。

`TaxRatesWorkspace.ListAsync`は既存ClientSessionの認証更新と前後のコンテキスト検証を通す。同Sessionが所有する`TaxRateHistoryCache`は家計簿に依存せず、キャッシュヒットや日付変更による追加GETはない。明示的な再ログイン/復元・ログアウト成功・認証失効/別ログイン検出で破棄する。通常のトークン更新と通信障害によるログアウト失敗では破棄しない。ClientConnectionの接続先変更/Disposeでは旧Sessionのキャッシュも破棄する。世代照合で旧応答の返却/保存を拒否し、取得失敗を記憶・自動再取得しない。ディスク保存・TTL・定期更新は設けない。API保存時の最新履歴検証は既存どおり維持する。契約の正本は[共通税履歴](ledger-core-schema.md#共通税率履歴の全件getメモリキャッシュ)。

## 税率・正式計算の画面接続（工程6-D4-C実装済み）

`MainWindow.ReceiptCalculations`が既存税率キャッシュと共通計算をレシート画面へ接続する。取得は編集開始前、候補/適合判定はClientの`ReceiptTaxRules`、正式金額は`ReceiptCalculationDisplay`→`ReceiptCalculations.CalculateApplied`を使用する。従来`FromEditor`の未反映拒否は維持し、表示専用経路は反映済みの値だけをコピーする。計算式をWinUIに複製しない。

行は`INotifyPropertyChanged`の表示プロパティだけを更新し、入力欄・選択・フォーカス・下書きを再生成しない。候補の再構成中はSelectionChangedから入力へ書き戻さず、現在の期限外税率も保持する。取得失敗の再試行、日付/率不一致、計算不能/部分額を誤認させない表示の正本は[レシートUI](ledger-receipt-ui.md#税率候補正式計算の画面接続工程6-d4-c実装済み)を参照。API保存・税率の自動投入は接続しない。
