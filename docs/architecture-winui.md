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

`MainWindow.Categories`・`CategoryOrder`・`CategoryRecovery`と`CategoryRow`で管理画面を接続済み。画面の表示・保存・競合・認証復帰は[科目管理画面](ledger-core-schema.md#科目管理画面)、人物の期間フィルターは[人物仕様](ledger-persons.md)を正本とする。レシートの親情報・科目入力は工程6-D2、明細入力・税率管理は後続。

## レシートのClient読取接続（工程6-A）

`ReceiptTransport`・`ReceiptsWorkspace`で一覧/詳細/履歴候補3種/親情報自動補完のGETを接続済み。正本は[レシートAPI](ledger-receipt-api.md)・[候補/補完API](ledger-receipt-suggestions.md)。WinUI一覧・画面用マスタキャッシュ基盤は工程6-Bで接続済み。書込Clientは工程6-Cで接続済み。編集状態モデルは工程6-D1で実装済み。親情報・科目・遷移の基本画面は工程6-D2で実装済み。明細入力・候補UIは未実装。

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

## 新版Windows版の配布・削除（未実装）

新版はインストーラー形式で配布する方針とする。アンインストール時に、そのWindowsユーザーの設定・認証情報も削除するか選べるようにする。対象はこのPCに保存した新版のデータで、サーバー上のアカウントや家計簿データは削除しない。アプリ内の専用データ削除操作は保留にし、初期実装の必須機能にはしない。

インストーラーの作成ツール、インストール範囲と更新方法、削除選択の初期値などは後続で設計する。インストーラーとアンインストール処理はまだ実装していない。
