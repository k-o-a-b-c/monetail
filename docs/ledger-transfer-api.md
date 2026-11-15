# 振替API契約（基本CRUD・振替名候補GET実装済み）

更新: 2026-11-15。振替の**権限・HTTP・JSON・一覧／フォーカスページング・候補／補完・再送・競合**の正本。振替の一覧GET・詳細GET・新規POST・更新PUT・削除DELETE・振替名候補GETを実装済み。レシート既存APIおよび後続の自動補完の実装状態と区別する。

参照：[入口・残課題](ledger-transfer.md)／[DB・値域](ledger-transfer-db.md)／[WinUI画面](ledger-transfer-ui.md)／[レシートAPI](ledger-receipt-api.md)／[共通再送](ledger-operation-retries.md)。

## 実装範囲

一覧/詳細GETは既存レシートの認証/DB準備フィルター、`LedgerApiFilter`、UUID入力検証、共有参照レート制限を利用する。`LedgerManagementService.InLedgerAsync`で読取前後の認証・現在参加を検証し、指定家計簿の振替だけを参照する。下記の応答を整形するだけで保存値・監査列・更新版・再送記録は変更しない。実装/検証結果は[一覧GET](../tests/README.md#振替一覧get)・[詳細GET](../tests/README.md#振替詳細get)を参照。

一覧GETの`focusTransferId`も実装済み（DEC-279）。POST・PUT・DELETEは下記の範囲で実装済み。振替名候補GETも実装済み。自動補完は未実装であり、以下の確定契約の記述はその完了を意味しない。

### 新規POSTの実装

`TransferCreateInput`は共通`LedgerPayload`で未知/重複項目・型・必須項目を確認し、下記の業務値を検証する。UUID D形式とrequestIdのUUID v4、ローカル秒精度、UTF-16文字数/Unicode空白・メモ改行を検証し、名前/空白のみのメモは保持、空文字メモだけnullへ正規化する。

レシートの支払方法所有権照合・`InLedgerAsync`・`PreviousAsync`・`Record`・`OperationRequest.Hash`・期間判定を共用する。双方の支払方法/人物を同一家計簿で解決し、現行の権限・有効期間を検証。振替INSERTと`transfer.create`の成功記録INSERT後も、認証/参加/人物対応/所有権/マスタ期間を再取得してから同一SQLiteトランザクションを確定する。

成功済みの同一要求は現在の認証/参加/ロール/双方の所有権を確認し、処理を繰り返さず元の201・Locationを再現する。結果行が削除済みでも作り直さない。201の本文は空とし、ID/更新版/監査情報はサーバーで設定する。新しいDB表・列・索引や独自再送期限は追加しない。検証結果は[POSTのテスト](../tests/README.md#振替post新規登録)を参照。

### 更新PUTの実装

`TransferUpdateInput`はPOSTの入力検証を共用し、`revision`もUUID v4として検証する。現在の振替の元/先と要求の元/先を照合し、Editorは4か所とも本人所有の場合だけ変更できる。保存する両支払方法と所有人物の有効期間を取引日で検証し、書込後も所有権/期間と認証/参加/役割/人物対応を再確認する。

実変更時は家計簿・振替ID・旧`revision`一致を条件にUPDATEし、新しい版と更新監査2項目を設定する。作成監査2項目は保持する。無変更時は振替行をUPDATEせず、同じ版一致を条件とする`operation_requests`へのINSERTだけを実行する。いずれも`transfer.update`の成功記録と同じSQLiteトランザクションで確定し、途中失敗を全体ロールバックする。

再送の照合は共通処理を使い、家計簿/URLの振替ID/要求revision/正規化済み6業務値/操作者/操作を結び付ける。成功再送でも現在の必要な権限を確認し、変更を繰り返さず204/空本文を返す。古い版の新規要求は無変更でも409。`latest`は詳細GETと同じ整形処理を使い、現在のアクセス権を再確認してから返す。検証結果は[PUTのテスト](../tests/README.md#振替put更新)を参照。

### 削除DELETEの実装

`TransferDeleteInput`は共通の厳密JSON処理とUUID v4検証で`requestId`/`revision`だけを受け付ける。新規要求は同一家計簿の対象と両支払方法を読み、現在のAdminまたは両側を所有するEditorに限って削除する。家計簿/振替ID/旧版一致を条件にDELETEし、`transfer.delete`成功記録INSERT後も認証/参加/ロール/人物対応/両所有権を再確認して、共通SQLiteトランザクションを確定する。版競合はPUTの最新8項目の整形・公開前権限確認を共用する。

要求ハッシュは家計簿/振替ID/要求revisionを含む。成功済み同一要求は現在の認証/家計簿参加/AdminまたはEditorを検証し、削除済み対象や支払方法の所有権を再取得せず204/空本文を再現する。振替のDELETEや成功記録INSERTを繰り返さない。入力/権限/版/途中書込の失敗は全体をロールバックし、関連マスタや他の取引は変更しない。検証結果は[DELETEのテスト](../tests/README.md#振替delete削除)を参照。

### 振替名候補GETの実装

`TransferSuggestionsQuery`で任意の`q`1件だけを受け付け、100 UTF-16コード単位以内か確認する。`TransferSearchText.Normalize`を振替一覧と共用するSQLite関数で、保存名と検索文字列の英数字だけを揃えて部分一致を判定する。検索文字列をSQLへ埋め込まずパラメーターで渡し、元の保存名は変更しない。

家計簿内の全履歴に検索を適用し、SQLのBINARY集約・最新更新UTC/元文字列順の後に101候補を取得して、下記契約の100候補と`hasMore`を返す。既存`InLedgerAsync`の読取前後の認証/参加確認・共有参照枠・共通エラー処理を使用する。取得専用でDB/索引を変更しない。実SQLの実行計画と検証結果は[候補GETのテスト](../tests/README.md#振替名候補get)を参照。

## 権限（確定・API強制）

| 役割 | 閲覧 | 登録・流用 | 更新・削除 |
| --- | --- | --- | --- |
| Admin | 家計簿内の全振替 | 両支払方法とも家計簿内全員から選択可 | 全振替 |
| Editor | 家計簿内の全振替 | **振替元・先の両方**が本人の支払方法の場合のみ | **元・先の両方**が本人の支払方法の場合のみ |
| Viewer | 家計簿内の振替閲覧のみ | 不可 | 不可 |

- Editorは片側だけ本人でも書込不可。振替先を他人の口座にする権限もない。権限・人物対応・所有者・家計簿所属を**API側で再検証**する。更新では変更前と変更後の両側の支払方法を検証し、権限チェックの抜け道にしない。
- Editorが他人の支払方法を含む既存振替を開く場合、およびViewerが開く場合は**同じ画面を読み取り専用**にし、［更新］［流用登録］［削除］は表示しない。左上［←］で戻れる。
- 管理者は人物をまたぐ振替を作成できるが、編集者にその振替の変更権限を継承させない。

## 振替APIの基本契約（2026-11-15確定）

レシートAPIと原則統一しつつ、振替の独立した業務・権限・検索条件を維持する。以下のパス・HTTPステータス・JSONの基本構造は利用者確認済み。競合エラーの共通本文は本節で定義する。競合後の2択・保存不可の画面動作は[振替WinUI画面](ledger-transfer-ui.md)で確定、カーソル生成・検証は本書で確定する。UnicodeのDB制約実装と他の残項目は[未決定事項](ledger-transfer.md)で区別する。

### APIパス・成功ステータス

| 操作 | API | 成功時 |
| --- | --- | --- |
| 一覧 | `GET /ledgers/{ledgerId}/transfers` | 200 OK |
| 詳細 | `GET /ledgers/{ledgerId}/transfers/{transferId}` | 200 OK |
| 登録（流用登録を含む） | `POST /ledgers/{ledgerId}/transfers` | 201 Created・本文なし |
| 更新 | `PUT /ledgers/{ledgerId}/transfers/{transferId}` | 204 No Content・本文なし |
| 削除 | `DELETE /ledgers/{ledgerId}/transfers/{transferId}` | 204 No Content・本文なし |

- 読取は現行の家計簿参加・閲覧権限を確認する。書込は[振替の権限](ledger-transfer-api.md)の振替元・先双方の権限制御を強制する。**POST 201 Createdでは新規振替の詳細リソースを指す`Location: /ledgers/{ledgerId}/transfers/{transferId}`ヘッダーを必須とし、新IDはAPI UUID D形式で示す**。成功済みPOSTが同じ`requestId`・内容で再送された場合も、既存`operation_requests.result_id`を使って**同じ振替IDの`Location`**を再現する（新規発行・二重登録しない）。成功本文は従来どおり空とする。
- 一覧の振替1件は**7フィールドのみ**：`transferId`、`transactionAt`、`name`、`fromPaymentMethodId`、`toPaymentMethodId`、`amount`、`memo`。詳細GETはこれら7つに`revision`を加えた**8フィールド**とする。監査情報はどちらにも含めない。UUIDはAPIでD形式、日時は[業務値・DB契約](ledger-transfer-db.md)の固定書式、金額はInt32範囲のJSON整数。支払方法・所有人物の表示名は既存マスタを利用し、一覧応答に重複させない。
- 保存成功のレスポンス本文に保存済み振替や新しい版を付けない。更新版は次回の詳細GETで取得する。

### JSON応答と書込リクエスト（2026-11-15確定）

- **一覧GET**は200 OKでトップレベルに`transfers`（0〜100件の前述7項目の配列）と`nextCursor`（次ページがあるとき不透明文字列、なければ`null`）の**2項目のみ**を返す。0件の場合も`{"transfers":[],"nextCursor":null}`とし、キーを省略しない。例の`nextCursor`はページング情報であり、振替1件のフィールドではない。
- **詳細GET**は200 OKで振替の**8項目のJSONオブジェクトを直接**返す。`{"transfer":{...}}`のような外側ラッパーは付けない。一覧・詳細とも`memo`は文字列または`null`とし、キー自体は省略しない。旧保存値`""`は`null`として返す。
- **POST本文**は`requestId`（UUID v4）と、業務入力の**6項目** `transactionAt`、`name`、`fromPaymentMethodId`、`toPaymentMethodId`、`amount`、`memo`を必須とする。`memo`のみ値`null`を許す。新規`transferId`・`revision`・監査情報は送らず、サーバー側で採番・記録する。 `memo:""`も受け付けるが、API保存前に`null`へ正規化する。空白のみの文字列はそのまま保持する（[メモの値と無変更PUT](ledger-transfer-db.md)）。
- **PUT本文**はPOSTの`requestId`＋業務6項目に、更新対象の`revision`（UUID v4）を加えた**8項目**を必須とする。`transferId`はURLで指定し、本文に含めない。無変更PUTの判定と204応答は[業務値・DB契約](ledger-transfer-db.md)・本節の契約に従う。
- **DELETE本文**は`requestId`（UUID v4）と`revision`（UUID v4）の**2項目だけ**を必須とする。対象`transferId`はURLで指定する。POST/PUT/DELETEの成功本文は従来どおり空とする。
- API JSONのIDは小文字UUID D形式、取引日時は`yyyy-MM-ddTHH:mm:ss`、金額はInt32正整数のJSON数値とする。名前・メモの値域・検証は[業務値・DB契約](ledger-transfer-db.md)に従う。**POST・PUT・DELETEのJSON本文で未知のプロパティ・同名プロパティの重複・指定型と異なる値があればHTTP 400 `invalid_input`で拒否する**（2026-11-15確定）。必須項目欠落・不正UUID・値域違反も既存の同コードで拒否し、無断の型変換・上書き・黙殺をしない。`field`には判定可能な場合に問題の項目名を返す。一般のエラー本文は[振替API契約](ledger-transfer-api.md)の共通形式を使用する。


### 一覧検索とカーソル

- 振替一覧のクエリは、レシートと共通の5種類に**保存した振替への移動専用`focusTransferId`を追加した6種類**：**`month`（必須：`YYYY-MM`または`all`）**、`ownerPersonId`（任意）、`paymentMethodId`（任意）、`q`（任意）、`cursor`（任意）、**`focusTransferId`（任意、UUID D形式）**。`q`は**振替名`name`の部分一致検索**で、文字列の英数字の大文字・小文字と全角・半角を区別しない（[ホームと振替一覧](ledger-transfer-ui.md)）。レシートの店名検索ではない。**同一視は英数字の全角/半角・大文字/小文字だけ**とし、かな・記号・互換文字まで変換する正規化は使わない。
- `ownerPersonId`は振替元・先の**いずれかの支払方法の所有人物**、`paymentMethodId`は振替元・先の**いずれかの支払方法**に一致すれば該当。人物条件と支払方法条件は**それぞれ独立に判定しAND結合**する。他の検索条件もANDで適用する。検索・権限を適用後に[業務値・DB契約](ledger-transfer-db.md)・[ホームと振替一覧](ledger-transfer-ui.md)の**取引日時降順、同日時は振替ID降順**で100件ずつキーセット取得する。
- **`focusTransferId`の入力と404の境界（DEC-279・2026-11-15確定）**：任意のUUID D形式1件だけを受け付ける。不正値・前後空白・空UUID・重複指定・`cursor`との併用（空cursorを含む）は**400 `invalid_input`**。認証・家計簿参加の検証は既存の順序を維持する。現行家計簿と指定済みの月・人物・支払方法・振替名の**全フィルター**を適用して対象を検索し、不存在・削除済み・別家計簿・条件外は区別せず**404 `not_found`**を返す。対象の存在情報・検索条件外となった理由は返さない。
- **フォーカスの開始位置**：対象の取引日時・振替IDを起点とし、`(transaction_at, transfer_id COLLATE BINARY) <= (対象日時, 対象ID)`の範囲を取引日時降順・同日時ID降順で取得する。対象自身を先頭に含め、最大100件を返す。対象確認後に対象が先頭として取得できなくなった場合も404とする。追加の絞り込み条件ではなく**初回ページの開始位置**であり、既存の7項目・`transfers`/`nextCursor`の構造は変更しない。
- **後続取得**：既存の振替専用`nextCursor`を使い、`focusTransferId`なしで通常の古い方向へ継続する。条件ハッシュには家計簿と4つの検索条件だけを含め、`focusTransferId`を含めない。［先頭に戻る］はフォーカスもカーソルも送らず、現在の検索条件で先頭ページを取得する。フォーカス取得404時の将来WinUIの復帰は[保存後の一覧復帰](ledger-transfer-ui.md#保存後の一覧復帰対象振替への移動2026-11-15確定)に従う。
- 次ページはサーバー発行の不透明な`cursor`を使用。カーソルは家計簿・すべての検索条件に紐づけ、不正・改ざん・条件不一致は**HTTP 400、`invalid_cursor`**。条件変更時はカーソルを捨て、先頭から検索する。WinUIの初回100件・領域不足時は最大300件まで自動補充し、末尾20行以内のスクロール時は自動追加取得する。初回補充上限に達しても領域不足なら［続きを読み込む］を表示する（[ホームと振替一覧](ledger-transfer-ui.md)）。**カーソルの生成・検証方式はレシート一覧と同方式に統一する**（2026-11-15確定）。ASP.NET Core Data Protectionの既存の永続鍵を利用し、**振替専用purpose `Monetail.TransferList.Cursor.v1`**で保護する（レシート用カーソルは流用不可）。内部に家計簿ID、家計簿IDと検索条件（`month`・`ownerPersonId`・`paymentMethodId`・送信された`q`。省略と空文字は区別）のSHA-256、末尾の取引日時とN形式`transfer_id`を保持する。`(transaction_at, transfer_id COLLATE BINARY) < (末尾日時, 末尾ID)`のキーセット境界で逆順B-tree走査し、100件＋次ページ有無の確認を行う。OFFSET・COUNT・専用DBテーブル・独自有効期限は不要。再起動後も同じ保護鍵で検証し、保護解除不可・改ざん・検索条件不一致・レシート用トークンは400 `invalid_cursor`。各GETで現在の認証・家計簿参加を検証する。一覧の応答外枠は本節の`transfers`・`nextCursor`の2項目で確定。

### 振替名候補・自動補完API（2026-11-15確定）

- **振替名候補**：`GET /ledgers/{ledgerId}/transfer-suggestions?q={検索文字列}`。`q`は任意で、未指定・空文字なら同一家計簿で最近使われた振替名を取得する。部分一致検索は[候補・自動補完画面](ledger-transfer-ui.md)のとおり英数字の全角／半角・大文字／小文字を同一視するが、保存済みの元文字列は変更しない。元文字列のBINARY完全一致で重複排除し、該当履歴の`MAX(updated_at_utc) DESC`、同時刻なら**候補の元文字列BINARY昇順**で並べ、最大100件を返す。全履歴への検索と重複排除を先に行う。
- 候補検索成功は**200 OK**、応答は`{"suggestions":["銀行へ入金","銀行間振替"],"hasMore":false}`の**2項目のみ**。`suggestions`は元文字列の配列（0〜100件）、`hasMore`は重複排除後の該当候補が100件を超える場合だけ`true`。0件でも`{"suggestions":[],"hasMore":false}`を返す。カーソルや振替ID・金額・支払方法などは付けない。入力`q`は100 UTF-16コード単位以内とし、超過・不正形式は`400 invalid_input`。
- **自動補完**：`GET /ledgers/{ledgerId}/transfer-autofill?name={確定振替名}&transactionDate=YYYY-MM-DD`。`name`は元文字列を完全一致で照合する必須項目（1〜100 UTF-16コード単位）、`transactionDate`は取引日の日付部分のみを表す必須項目。URLパラメータは適切にエンコードし、時刻・UTCオフセットは送らない。元を手動選択した後は任意の`fromPaymentMethodId`（UUID D形式）も送る。この場合は**振替名＋指定された振替元**が一致する履歴だけから振替先を探す。
- 補完成功は**200 OK**、応答は`fromPaymentMethodId`と`toPaymentMethodId`の**2項目だけ**（それぞれUUID D形式または`null`）。履歴がない・利用可能な履歴がない場合は補完できない項目を`null`とする。指定`fromPaymentMethodId`がある場合は、採用する同名履歴の振替元に一致するものだけを選ぶ（見つかった場合の応答元IDは指定元IDと同一）。クライアントは**利用者の手動選択を必ず保持**し、`null`で手動選択を消さない。APIは読み取り専用であり、画面側の入力値・DBを直接変更しない。
- 補完元の優先順位と妥当性は[候補・自動補完画面](ledger-transfer-ui.md)に従う。`updated_at_utc DESC`、同時刻なら`transfer_id COLLATE BINARY DESC`で検索し、**取引日に有効かつ現在の権限で双方利用可能な同一履歴**を採用する。無効な履歴を飛ばし、複数履歴の振替元・先を継ぎ接ぎしない。元指定時は振替先の妥当性を確認し、指定元の所有者権限・有効期間も確認する。候補検索の表記ゆれ一致を補完の`name`完全一致には流用しない。
- 2つのGETはいずれも現在の認証・家計簿参加／閲覧権限を検証する。補完では支払方法の所有人物と書込権限まで確認し、**利用できない他人物の支払方法IDを返さない**。参照結果は当時点の候補であり、保存時には再検証する。

### 共通エラー・再送・レート制限（2026-11-15確定）

| HTTP | `code`など | 適用 |
| --- | --- | --- |
| 400 | `invalid_input` | 不正JSON・入力値・UUID、取引日の支払方法の有効期間外など（`field`で対象を指定） |
| 400 | `invalid_cursor` | 一覧カーソル不正・検索条件不一致 |
| 401 | 既存の認証エラー形式 | 認証なし・期限切れ |
| 403 | `forbidden` | 家計簿内の操作権限不足 |
| 404 | `not_found` | 対象振替不存在・家計簿非参加・フォーカス対象の条件外（存在情報を公開しない） |
| 409 | `revision_conflict` | 更新版の競合 |
| 409 | `request_conflict` | 同じ`requestId`を異なる操作者・操作・要求内容に再利用 |
| 429 | 既存の回数制限エラー | 参照・書込の共有枠超過 |

- エラー本文はレシートと同じ共通形式（`code`・`message`・`field`・`latest`・`permissions`）を使用し、`revision_conflict`の**更新・削除競合**では`latest`に詳細GETと同じ最新振替の8項目を格納する。再送ID競合では`latest:null`。401など認証境界の既存形式を独自の振替専用コードへ置き換えない。競合後のWinUI操作と401・403・404時の画面復旧は[振替WinUI画面](ledger-transfer-ui.md)に従う。
- **全書込で`requestId`（UUID v4）を必須とし、既存`operation_requests`に操作・操作者・家計簿・対象・版・入力の同一性を記録**。同じ操作の成功済み再送は現在の認証・家計簿参加・適切な権限を検証した上で、前回の成功結果（POST 201／PUT・DELETE 204、本文なし）を再現する。内容が異なる再利用は`409 request_conflict`。成功記録は保存対象と同じトランザクションで記録し、レシートと同様に無期限保持する。
- **通信失敗・結果不明時**は同じ`requestId`と完全に同じ送信内容で再送し、成功を推定しない。入力を修正して新たに保存する場合は新しい`requestId`を生成し、`revision`を勝手に更新しない。保存失敗時は画面の未保存入力を保持する。成功済みDELETEの同一内容再送は、既に対象がなくても現在の参加・AdminまたはEditorを検証して204を再現し、削除済みのため支払方法所有権の再検証は省略する。新規DELETEとPUTでは通常の厳密な権限検証を行う。
- **既存のアカウント単位共有レート制限**を利用する。参照GET（候補・補完を含む）は**120回/分**、POST/PUT/DELETEは**20回/分**。専用の制限枠・新しいDBテーブルは追加しない。候補GETの429・通信失敗時は小さな非モーダル通知と候補非表示で自由入力を維持し、連続自動再試行はしない。既存の候補キャッシュ・約250msデバウンスを使う。

### 保存・削除の再送と競合

- **POST・PUT・DELETEのすべてに操作ごとの`requestId`（UUID v4）を必須**とし、既存の`operation_requests`を共用する。PUT・DELETEは保存済み`revision`（UUID v4）も必須。再送による二重登録・二重変更を防止する。記録方式・衝突時応答の基本は本節の「共通エラー・再送・レート制限」に従い、実装上の細目はレシート契約と整合させる。
- 更新・削除の版不一致は**HTTP 409 `revision_conflict`**。更新・削除競合では最新の振替詳細データを共通エラー本文の`latest`に返す（本節の共通エラー契約）。対象削除済み・閲覧権限喪失などは共通の404・403等を使い、画面側は[振替WinUI画面](ledger-transfer-ui.md)の入力保持・読み取り専用の契約に従う。
- 無変更PUTは[業務値・DB契約](ledger-transfer-db.md)で確定したとおり、全検証の後に**204を返し、振替行・`revision`・監査列を変更しない**。通信失敗や結果不明の場合は成功と断定せず、再送契約に基づき扱う。
