# レシートAPI・業務契約

**レシートの一覧・詳細取得・保存・削除APIの外部契約の正本。**

## 1. 対象・関連契約

- 対象：レシート全体の参照・作成・更新・削除。履歴候補・自動補完は[履歴API](receipt-history.md)。
- 関連契約：[家計簿](ledgers.md)、[人物](persons.md)、[科目](categories.md)、[支払方法](payment-methods.md)、[税率履歴](tax-rates.md)、[レシートDB](../../../ledger-receipt-schema.md)、[計算契約](../../../12-calculation-contract.md)、[レシート画面](../../../ledger-receipt-ui.md)。
- 共通契約：[セッション保護](../../../auth-session-security.md)、[Windows認証](../../../auth-windows-oauth.md)、[共通HTTP境界・回数制限](../../../ledger-core-api.md)、[共通再送](../../../ledger-operation-retries.md)。

## 2. エンドポイントと権限

| メソッド・パス | 動作 | 権限 | HTTP成功コード |
| --- | --- | --- | --- |
| `GET /ledgers/{ledgerId}/receipts` | 一覧取得 | Admin・Editor・Viewer | 200 |
| `GET /ledgers/{ledgerId}/receipts/{receiptId}` | 詳細取得 | Admin・Editor・Viewer | 200 |
| `POST /ledgers/{ledgerId}/receipts` | 全体作成 | Admin・Editor（下記） | 201 |
| `PUT /ledgers/{ledgerId}/receipts/{receiptId}` | 全体更新 | Admin・Editor（下記） | 204 |
| `DELETE /ledgers/{ledgerId}/receipts/{receiptId}` | 全体削除 | Admin・Editor（下記） | 204 |

現在の認証・対象家計簿への参加を要求ごとに検証する。Adminは家計簿内の全レシートを書き込める。Editorは本人対応人物が所有する支払方法のレシートに限り書込可。PUTは変更前と変更後の両方の支払方法について検証する。作成者で権限を判定しない。成功済み要求の再送は第7節に従う。

## 3. 値・業務制約

| 項目 | 契約 |
| --- | --- |
| ID・更新版 | レシート・グループ・明細IDはAPIがUUID v7を生成。更新版はUUID v4。削除済みIDは再利用しない |
| 取引日時 | 実在するローカル日時 `yyyy-MM-ddTHH:mm:ss`（オフセット・小数秒なし）。税率・マスタ期間照合は日付部分を使用 |
| 店名・所在地・品名 | 各100 UTF-16コード単位以内。店名は非NULL・空文字不可、所在地はNULL可、品名は非NULL（空文字可）。Trimしない |
| メモ | レシートは100 UTF-16コード単位以内。グループ・明細には独自上限を設けない。全階層でNULL可、空文字をNULLに正規化し、空白のみの文字列は保持 |
| 税設定 | `taxInclusionMode` は整数0（税込）・1（税抜）。`taxRoundingMode` は `floor`・`half_up` |
| 周期 | 間隔は1以上のInt32、単位は `Week`・`Month`・`Year`。両方設定または両方NULL。設定する場合は定期明細が1件以上必要 |
| 科目グループ | 1件以上、同一科目の重複不可。各グループに明細1件以上 |
| 明細順 | `displayOrder` は0以上のInt32。同順位を許容 |
| 単価・単位値引額 | 符号付きInt32 |
| 数量 | 1以上のInt32 |
| 率値引 | 正確にDecimalで表現できる0〜100の十進文字列 |
| 実税率 | 正確にDecimalで表現できる十進文字列または非課税のNULL。課税0%と非課税を区別する |
| 費用区分 | 支出科目は `Fixed`・`Variable`・`Excluded`、収入科目は `Excluded` のみ |
| 発生区分 | `Regular`・`Irregular`・`Temporary` |
| 対象人物 | 0人以上、人物IDの重複不可。集合として扱う |

Decimal文字列はInvariantCultureの通常の十進表記（先頭符号・小数点可）とし、指数表記・桁区切り・空白・丸めを伴う表現不能な入力を拒否する。数値として同値の表記は同一視する。JSON整数は文字列や小数で代用しない。

保存時、支払方法とその所有人物、科目、対象人物は同一家計簿に存在し、取引日が各有効期間内（両端含む、NULL端は制限なし）であることを検証する。全明細の税率は取引日に有効な全税区分の履歴から得た税率（同値重複除去）または非課税であることを検証する。保存済みの設定・税率が未変更でも例外は設けず、代替値への自動変更は行わない。費用区分も現在の科目で検証する。

合計は[計算契約](../../../12-calculation-contract.md)に従ってAPIが計算する。各最終額のInt32超過・Decimal演算のオーバーフローは保存拒否とし、途中額がInt32を超えることだけを理由には拒否しない。

## 4. リクエスト

パスの `ledgerId`・`receiptId` はUUID（N・D・B・P・X形式、URL上で適切にエンコードする）。UUID値として照合し、解釈できないパスはルーティングの404とする。全桁ゼロのパスUUIDも通常の認可・存在確認に従う。JSON本文と検索条件のUUIDは同形式を受理し、全桁ゼロは不正。UUIDのバージョンによる入力制限は設けない。

### 4.1 一覧GET

| クエリ | 必須 | 値・意味 |
| --- | --- | --- |
| `month` | 必須 | 実在する `YYYY-MM`（0001〜9999年）または `all` |
| `ownerPersonId` | 任意 | 支払方法の所有人物ID |
| `paymentMethodId` | 任意 | 支払方法ID |
| `q` | 任意 | 店名の部分一致検索語。省略・空文字は絞り込みなし |
| `cursor` | 任意 | APIが発行した不透明な継続位置 |
| `focusReceiptId` | 任意 | 指定レシートを先頭とする開始位置 |

許可したクエリのみを各1値で受け付ける。全検索条件をANDで適用する。店名の照合は[履歴APIの検索正規化](receipt-history.md#3-値業務制約)と同じ方式とし、検索語の独自文字数上限は設けない。

`focusReceiptId` と `cursor` は併用不可（空cursorを含む）。focusの対象は家計簿・全検索条件に合致する必要があり、不在・削除済み・他家計簿・条件外は同じ404とする。

### 4.2 POST・PUT・DELETE

JSONキーは大文字・小文字を区別し、許可項目のみを受理する。NULL可能な項目も必須キーとする。

- POST・PUTは第5節の詳細と同じ基本情報・グループ・明細・対象人物の全体スナップショット。トップレベルは `requestId`、`transactionAt`、`shopName`、`shopLocation`、`paymentMethodId`、`taxInclusionMode`、`taxRoundingMode`、`recurrenceInterval`、`recurrenceUnit`、`memo`、`groups`。PUTはさらに `revision` を必須とする。
- グループは `categoryId`、`memo`、`details`、明細は第5節の `detailId` 以外の全項目を必須とする。PUTでは既存グループ・明細の `groupId`・`detailId` を必須とし、新規要素ではIDキーを省略する。POSTの全要素は新規とする。
- DELETEの本文は `requestId`・`revision` の2項目。
- 送信された全体を正とする。既存のグループ・明細のIDを維持し、同一レシート内での明細のグループ移動もIDを維持する。同じ科目の既存グループを新IDで作り直さない。送信されなかった既存要素と不要な人物関連付けは削除し、新規要素だけ採番する。既存子IDの重複・対象レシート外からの流用は認めない。

## 5. 成功応答・識別と並び順

応答UUIDおよびLocationのUUIDは小文字N形式。以下のJSONオブジェクトは列挙した項目だけを持ち、NULL可のキーも必ず返す。

### 5.1 一覧GET

200応答のトップレベルは `receipts`（配列）・`nextCursor`（不透明な文字列またはNULL）。

| `receipts[]` のキー | JSON型 | NULL |
| --- | --- | --- |
| `receiptId` | UUID文字列 | 不可 |
| `transactionAt` | 日時文字列 | 不可 |
| `shopName` | 文字列 | 不可 |
| `shopLocation` | 文字列 | 可 |
| `paymentMethodId` | UUID文字列 | 不可 |
| `totalAmount` | 保存済み合計のInt32整数 | 不可 |
| `memo` | 文字列 | 可 |

全件に条件を適用してから取引日時降順・同日時はIDのBINARY降順で最大100件を返す。後続が存在する場合のみ `nextCursor` を発行する。0件は `{"receipts":[],"nextCursor":null}`。通常は直前ページの末尾より後方を取得し、focus指定時は対象自身を先頭に含める。

カーソルは家計簿・年月・所有人物ID・支払方法ID・送信された検索語（省略と空文字を区別）に結び付け、改ざんを検出する。継続取得は同じ条件でカーソルをそのまま送る。focusは条件の結び付けに含めず、続きの要求では省略する。固定スナップショットではなく、ページ間の取引日時変更等による重複・取りこぼしの完全防止は保証しない。

### 5.2 詳細GET

200応答はレシートオブジェクトを直接返す。

| 基本情報のキー | JSON型 | NULL |
| --- | --- | --- |
| `receiptId` | UUID文字列 | 不可 |
| `transactionAt` | 日時文字列 | 不可 |
| `shopName` | 文字列 | 不可 |
| `shopLocation` | 文字列 | 可 |
| `paymentMethodId` | UUID文字列 | 不可 |
| `taxInclusionMode` | 整数 | 不可 |
| `taxRoundingMode` | 文字列 | 不可 |
| `recurrenceInterval` | 整数 | 可 |
| `recurrenceUnit` | 文字列 | 可 |
| `memo` | 文字列 | 可 |
| `revision` | UUID文字列 | 不可 |
| `groups` | 配列 | 不可 |

| `groups[]` のキー | JSON型 | NULL |
| --- | --- | --- |
| `groupId` | UUID文字列 | 不可 |
| `categoryId` | UUID文字列 | 不可 |
| `memo` | 文字列 | 可 |
| `details` | 配列 | 不可 |

| `details[]` のキー | JSON型 | NULL |
| --- | --- | --- |
| `detailId` | UUID文字列 | 不可 |
| `displayOrder` | Int32整数 | 不可 |
| `name` | 文字列 | 不可 |
| `unitPrice` | Int32整数 | 不可 |
| `quantity` | Int32整数 | 不可 |
| `unitDiscountAmount` | Int32整数 | 不可 |
| `discountRate` | Decimal文字列 | 不可 |
| `taxRate` | Decimal文字列 | 可 |
| `expenseType` | 文字列 | 不可 |
| `occurrenceType` | 文字列 | 不可 |
| `memo` | 文字列 | 可 |
| `personIds` | UUID文字列の配列 | 不可 |

グループは科目マスタ順（同順位は科目IDのBINARY昇順）、明細は表示順昇順（同順位は明細IDのBINARY昇順）。対象人物は人物マスタ順（同順位は人物IDのBINARY昇順）で返すが、集合として比較する。空配列は `[]`。保存済みの空文字メモもNULLとして返す。

### 5.3 書込

POSTは201・本文なし、作成した詳細リソースのURLをLocationに返す。PUT・DELETEは204・本文なし。成功済み同一要求の再送でも同じ成功応答とする。

## 6. 競合・失敗応答

業務エラーは `code`（文字列）・`message`（文字列）・`field`（文字列またはNULL）・`latest`（NULLまたは詳細GETと同じレシートオブジェクト）の4項目のみ。保存先障害の503は `code`・`message` の2項目。受付前の認証・認証ストア障害・Origin／CSRF・本文上限・回数制限、ルーティング段階のエラーは共通HTTP境界に従う。GETは共有参照枠、書込は共有書込枠を使用する。

| HTTP | code | 条件 | field |
| --- | --- | --- | --- |
| 400 | `invalid_input` | Content-Type欠落・JSON非対応、JSON構造不備、未知・重複JSONキー | NULL |
| 400 | `invalid_input` | 個別項目の型・値・必須キー・入力制約の不備、同一家計簿に存在しない指定マスタID（他家計簿を含む） | 該当項目パス |
| 400 | `invalid_input` | 未知・重複クエリ、不正な検索条件、focusとcursorの併用 | 該当クエリ名（併用はfocusReceiptId） |
| 400 | `invalid_cursor` | カーソルが不正・改ざん・検証不能または家計簿・条件不一致 | cursor |
| 400 | `master_changed` | 下記のマスタ不整合 | 該当項目パス |
| 403 | `forbidden` | 書込権限不足 | NULL |
| 404 | `not_found` | 家計簿不存在・未参加、対象レシート・既存子IDの不在（他家計簿・別レシートを含む）、focus対象の条件外 | NULL |
| 409 | `revision_conflict` | 更新版不一致 | NULL |
| 409 | `request_conflict` | 要求IDの不一致再使用 | NULL |
| 503 | `store_unavailable` | 保存先障害等 | キーなし |

`latest` はrevision_conflictの場合のみ返す。返却前に現在の認証・参加・書込権限を再検証する。他の業務エラーではNULL。

`master_changed` はPUTで取引日の日付部分と該当する参照値・設定値が保存済みと同じで、現行マスタとの不整合を確認できる場合に限る。取引日や該当値を変更した場合、判断不能、参照先の不存在、POSTは通常のinvalid_input。マスタ版の送信は要求しない。

個別項目のfieldは0始まりの配列パス（例：`groups[0].details[2].personIds[0]`）。複数の入力エラーは親→グループ→明細、各配列の先頭から最初の1件を返す。構文・型検証と認可・更新版照合の境界は第7節に従う。画面での入力保持・権限再取得・競合処理は[画面仕様](../../../ledger-receipt-ui.md)に従う。

## 7. 整合性・再送

- 参照は家計簿で限定し、取得前後で現在の認証・参加を検証する。
- 書込は現在の認証・参加・書込ロール、本文の型・項目、成功再送の照合を行う。新規要求では対象の存在と支払方法所有者の権限、更新・削除の版、保存の全項目・マスタ・子IDを検証する。確定前にも認証・参加・権限および保存に必要なマスタを再検証する。
- 全階層・合計・監査情報・更新版・成功再送記録を単一トランザクションで原子的に確定する。更新・削除の版照合は書込と一体で行い、失敗時はすべて取り消す。
- 無変更PUTも通常の権限・版・入力検証を行う。実質同一なら204、更新版・監査日時は変更せず成功記録だけ残す。同値判定はUUID値、Decimal数値、メモのNULL正規化、子要素と人物集合を用い、明細のdisplayOrder・所属グループは比較する。要求ID・版・監査情報は同値判定の対象外。
- POST・PUT・DELETEは操作ごとに新しいrequestIdを使う。共通再送記録は無期限保持し、操作者・操作種別（receipt.create／receipt.update／receipt.delete）・家計簿・対象レシート・版・正規化した入力を照合する。JSONキー順、UUID表記差、同値Decimal表記、空文字とNULLのメモ、グループ・明細配列順（displayOrderは比較）、人物ID配列順の差は同じ内容として扱う。
- 成功済み同一要求は現在の認証・参加・AdminまたはEditorロールを確認して第5節の元の成功応答を返す。支払方法の所有人物、マスタ、対象行の現在の有無、版は再検証せず、業務処理も再実行しない。異なる操作者・操作・内容での同一ID再使用は409。現在の未参加は404、Viewerは403。
- 結果不明時は同一要求ID・同一内容を再送する。確定エラーを修正して再度保存する場合は新規要求IDを使用する。更新版は勝手に変更しない。
