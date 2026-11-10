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

`MainWindow.Categories`・`CategoryOrder`・`CategoryRecovery`と`CategoryRow`で管理画面を接続済み。画面の表示・保存・競合・認証復帰は[科目管理画面](ledger-core-schema.md#科目管理画面)、人物の期間フィルターは[人物仕様](ledger-persons.md)を正本とする。レシート・税率管理は未実装。

## レシートのClient読取接続（工程6-A）

`ReceiptTransport`・`ReceiptsWorkspace`で一覧/詳細/履歴候補3種/親情報自動補完のGETを接続済み。正本は[レシートAPI](ledger-receipt-api.md)・[候補/補完API](ledger-receipt-suggestions.md)。WinUI一覧・画面用マスタキャッシュ基盤は工程6-Bで接続済み。書込・入力画面・下書き・候補UIは未実装。

- `ReceiptListQuery`・`ReceiptSuggestionQuery`・`ReceiptAutofillQuery`は入力時の条件を保持する。UUIDをD形式、取引日をInvariantのYYYY-MM-DDで送り、qの省略/空文字や不透明なcursorをTrim・正規化・破棄しない。ページの自動取得、業務エラー後の再試行、並び替えや候補/マスタキャッシュ更新は行わない。
- 一覧は要約7項目と`NextCursor`、詳細は親/グループ/明細/人物ID配列、候補は元文字列と`HasMore`、補完はNULL可の3項目を型付きで返す。日時は`DateTimeKind.Unspecified`のローカル秒精度、率はJSON文字列を正確な`decimal`/`decimal?`へ解析し、非課税nullと課税0%を分ける。JSON整数と数値文字列を混同せず、必須NULL項目の省略・不正コード/型・ID重複等を拒否する。APIの配列順を維持し、合計は再計算しない。
- Workspaceは既存`ClientSession.ExecuteAuthenticatedAsync`の更新/失効/アカウント変更検知を共用する。認証更新に伴う既存の再実行でも同じ検索条件を使う。業務エラーは`ReceiptRequestException`のStatusCode/Code/Field、403/404の権限情報を返す。401は既存の`AuthenticationRejectedException`、通信/不正応答は`ApiConnectionException`、利用者のキャンセルはキャンセル例外として区別する。エラー本文のmessage/latestやトークンを画面へ渡さない。
- 100件の有効な一覧は日本語の店名・所在地・メモで64KiBを超えるため、**一覧と詳細の成功本文だけ**をストリームから型付きDTOへ読み込む。本文全体の固定バイト上限・独自のメモ/子件数上限は追加しない。JSONの構造/必須項目、構築時の一覧100件、既存の10秒通信タイムアウトとキャンセルで保護する。DTOを保持するメモリは必要。候補（100文字×100件）・補完・エラー本文と既存APIは従来の64KiB上限を維持し、認証/方向転換/例外処理も共通の送信境界で扱う。

## 新版Windows版の配布・削除（未実装）

新版はインストーラー形式で配布する方針とする。アンインストール時に、そのWindowsユーザーの設定・認証情報も削除するか選べるようにする。対象はこのPCに保存した新版のデータで、サーバー上のアカウントや家計簿データは削除しない。アプリ内の専用データ削除操作は保留にし、初期実装の必須機能にはしない。

インストーラーの作成ツール、インストール範囲と更新方法、削除選択の初期値などは後続で設計する。インストーラーとアンインストール処理はまだ実装していない。


### レシート一覧の表示制御（工程6-B）

`MainWindow.Receipts.cs`が`ReceiptsWorkspace`へ接続し、UI非依存の`ReceiptSearchState`（確定条件/下書き）と`ReceiptListState`（取得世代/ページング/読込状態）を利用する。表示契約は[レシート画面](ledger-receipt-ui.md)を正本とする。画面遷移で進行中の一覧取得をキャンセルし、旧応答を適用しない。ClientSessionの認証を共用するが、一覧読込でWindow全体をbusyにしない。

`ClientConnection.Masters`は`LedgerMasterCache`を保持し、人物・支払方法・科目の初回取得を共有する。管理画面の既存Display処理で応答を反映し、遅延した古いマスタ取得で更新を戻さない。破棄条件はDEC-255に従う。`ReceiptRow`は保存済み合計額とマスタ由来の表示名だけを整形し、会計額を計算しない。
