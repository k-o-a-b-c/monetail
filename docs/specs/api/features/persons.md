# 人物API・業務契約

**人物5操作のAPI外部契約の正本。** 人物の画面動作は[人物管理](../../../ledger-persons.md)、参加者のAPIは[参加者API](../../../ledger-person-api-contract.md)、物理DBは[人物DB](../../../ledger-person-db.md)、共通再送は[operation_requests](../../../ledger-operation-retries.md)を参照する。

本書は人物リソースのAPI要求・応答と、認可・検証・整合性・競合・再送の保証を定める。

**応答形式の変更は未実装。** 以下は確定した変更後の契約であり、現行API・Windows Clientはまだ`myPersonId`・`currentRole`を人物応答に含めている。実装時に両方を除外し、[家計簿API](../../../ledger-core-api.md)を取得元に統一する。

## 1. 対象・関連契約

- 対象：家計簿に属する人物の一覧取得、作成、変更、削除、人物共通順の保存。全パスは `/ledgers/{ledgerId}` の下。
- 対象外：参加者の役割と人物IDの対応付け（[参加者APIの移行元](../../../ledger-person-api-contract.md)、[参加者仕様](../../../ledger-participants.md)）、認証アカウント自体、科目・支払方法・レシートの固有操作。
- 共有の認証・HTTP境界は[認証API](../../../auth-api-contract.md)、[セッション保護](../../../auth-session-security.md)、作成要求の記録・保持は[共通再送](../../../ledger-operation-retries.md)。物理DBは[人物DB](../../../ledger-person-db.md)を正本とする。

## 2. エンドポイントと権限（既存確定契約）

| メソッド・パス（家計簿相対） | 動作 | 権限 |
| --- | --- | --- |
| `GET /people` | 家計簿の全人物を共通順で取得する | 現在の家計簿参加者（Admin・Editor・Viewer） |
| `POST /people` | 人物1人を作成する | Admin |
| `PATCH /people/{personId}` | 人物名・有効期間を更新する | Adminは全項目。管理者以外は**本人に対応する人物の名前のみ** |
| `DELETE /people/{personId}` | 更新版を確認して削除する | Admin |
| `PUT /people/order` | 全人物の共通順を一括保存する | Admin |

- APIは各要求で認証・対象家計簿への**現在の参加**を検証する。書込時は権限・本人に対応する人物のIDを、更新・確定と同じトランザクション内でも検証する。家計簿に参加しない全体管理者にもアクセスを認めない。
- 本人判定はログインアカウントが**その家計簿で対応付けられた人物ID**で行う。人物名で同一人物と判定しない。対応なしでも人物閲覧は可能だが、管理者以外は本人に該当する名前変更の権限を得ない。
- 人物へのアカウント対応はこのAPIでは変更しない。参加者API/DBへ属する。

## 3. 人物の値・業務制約

| 項目 | 契約 |
| --- | --- |
| `personId` | APIが生成するUUID。名前・順序と独立し、名前変更でIDを変えない |
| `name` | 前後の空白を除いて**1～100 UTF-16コード単位（.NET `string.Length`）**。APIは上限超過を切り詰めない |
| 同名判定 | **同一家計簿内のTrim後の完全一致**を禁止。大文字/小文字、全角/半角、Unicode正規化の差は同一視しない |
| `validFrom` / `validTo` | `YYYY-MM-DD` 形式の日付または `null`。各 `null` はその側の制限なし。両端を含み、両方がある場合は開始日≦終了日 |
| 有効期間の意味 | 認証アカウントのログイン期限ではなく、**記録対象日**に人物が利用できる範囲。期間終了後でも対象日が範囲内なら利用可能 |
| 既存データとの整合 | 既存の人物参照を範囲外にする期間変更は拒否。新規の業務記録の保存側でも各業務の対象日・所有者を検証する |
| 削除 | 人物への参照が1件でも残れば拒否。参加者の人物対応、支払方法の所有、取引などを自動解除・連鎖削除しない |

同名禁止・有効期間の整合性・参照削除制限はAPIが保証する。各業務に関する参照・期間検証の**実装範囲**は[人物業務ルール](../../../ledger-persons.md)と各業務API正本で確認し、未実装の後続業務まで実装済みとみなさない。

## 4. リクエスト本文

| 操作 | JSON本文と意味 |
| --- | --- |
| 人物作成 POST | `requestId`（要求を一意に識別するUUID）・`name`は必須。`validFrom`・`validTo`は省略または`null`ならその側の制限なし |
| 人物更新 PATCH | `revision`必須。`name`・`validFrom`・`validTo`の変更項目を送る。**省略は変更なし**、日付の明示`null`は制限解除。名前に`null`不可 |
| 人物削除 DELETE | JSON本文に対象人物の`revision`必須 |
| 共通順 PUT | JSON本文に順序全体の`revision`と、現在の**全人物IDを各1回**含む`personIds`配列を必須とする |

- Admin以外の本人が人物をPATCHするときは、**名前だけ**を変更対象として送信し、日付項目は省略する。
- 編集で名前と期間を保存する際は1要求・1トランザクションでまとめ、一部だけ成功させない。APIの更新版は後述の単位で照合する。`revision`だけを指定した無変更PATCHも、版が一致すれば成功する。
- 順序の配列は他家計簿の人物ID・重複・欠落を認めない。有効期間外を含む全人物が対象。人物0件の場合は空配列を受け付ける。本文は許可されたキーのみを認め、未知・重複キーや不正なJSON型は400 `invalid_input`。

## 5. 成功応答・識別と並び順

| 操作 | HTTP成功応答 |
| --- | --- |
| `GET /people` | `200 OK`・人物一覧JSON |
| `POST /people` | 初回作成は`201 Created`、同一の成功要求の再送は`200 OK`。どちらも人物一覧JSON |
| `PATCH /people/{personId}` | `200 OK`・人物一覧JSON |
| `DELETE /people/{personId}` | `200 OK`・人物一覧JSON |
| `PUT /people/order` | `200 OK`・人物一覧JSON |

人物の作成・更新・削除・共通順保存の**成功時は、最新の人物一覧を同じ応答に含める**。変更後一覧は変更と同じサーバートランザクション内の状態を返す。
- 人物一覧/変更後の共通JSON形式は、トップレベルに`people`配列と`orderRevision`のみを持つ。人物ID・更新版はUUIDの**N形式の文字列**で返す。
- `people[]`の人物1件は`personId`、`name`、`validFrom`、`validTo`、`displayOrder`、`revision`。日付は形式文字列または`null`。
- `GET /people`は、有効期間外の人物を含む全人物を、家計簿共通の`displayOrder`に基づく順序で返す。新規人物は共通順の末尾へ追加する。
- **共通順PUTが現在の人物ID順と同一の場合は無変更成功（200）**とし、人物0件の空配列を含めて`displayOrder`・`orderRevision`を変更しない。
- 人物ID順が実際に変わる場合だけ`orderRevision`を更新する。人物の追加・削除時も全体順序版を更新する。
- 共通順PUTでは、無変更時も要求の更新版・全人物ID集合を検証し、古い`revision`は409を返す。
- 人物単位の`revision`は名前・期間の実値変更時だけ更新する。**無変更PATCHでは行版を維持**し、共通順だけの変更でも人物の行版を変えない（[更新版の保存単位](../../../ledger-person-db.md)）。

## 6. 競合・失敗応答

- 人物APIの業務エラー本文は`code`・`message`を基本とし、該当する場合のみ`field`・`latest`を含める。受付前の認証・CSRF・回数制限等の拒否には同じ本文を一律要求しない。

| HTTP | `code` | 内容 |
| --- | --- | --- |
| 400 | `invalid_input` | 名前・日付・JSON本文・順序ID集合等の入力不備。開始日・終了日の前後関係不正も含む |
| 401 | — | 未認証・認証失効等。共通の認証契約に従う |
| 403 | `forbidden` | 権限不足 |
| 404 | `not_found` | 家計簿不在・未参加・対象人物不在等（業務処理内） |
| 409 | `duplicate_person_name` | 同名人物の登録・更新 |
| 409 | `person_referenced` | 参照が残る人物の削除 |
| 409 | `person_period_conflict` | 既存レシートが期間外になる人物期間変更。`field`は`validFrom`または`validTo`、`latest`は`null` |
| 409 | `revision_conflict` | 更新版の不一致 |
| 409 | `request_conflict` | 要求IDを異なる内容で再利用 |
| 409 | `created_result_deleted` | 作成済み人物の削除後に同じ要求を再送 |
| 429 | — | 既存の共有回数制限 |
| 503 | `store_unavailable` | 保存障害等 |


### 更新版競合

- PATCH・DELETEは**対象人物の`revision`**、PUT /people/orderは**共通順全体の`orderRevision`**を必須として原子的に照合する。
- 409 `revision_conflict`の`latest`には、PATCH/DELETEなら最新の人物1件（`people[]`と同じ6項目）、共通順PUTなら最新の人物一覧応答（`people`・`orderRevision`）を返す。いずれも現在の権限で閲覧可能な情報に限る（[競合・権限喪失](../../../ledger-person-conflicts.md)）。
- 409の`latest`に情報を返す前にも現在の権限を検証し、権限喪失時は403/404等を優先する。閲覧できない他参加者の情報・過去の閲覧権限による情報を返さない。

## 7. 作成の再送・整合性

- POST /peopleでは`requestId`で同一作成要求を識別する。人物IDはサーバーで採番し、要求IDは人物IDとは別。現在のログイン・参加・権限を確認した上で要求内容の同一性を照合する。
- [共通再送](../../../ledger-operation-retries.md)の`operation_requests`記録を利用し、人物作成は`person.create`として扱う。**同じ成功要求の再送で重複作成しない**。操作者・操作種類・家計簿・正規化入力等が異なる要求IDの再利用は拒否する。
- 作成記録は業務行と同じトランザクション内で成功時だけ確定し、自動削除せず保持する。元の作成人物が後で削除されていても同じ要求で再作成せず、`created_result_deleted`で削除済みを伝える（[共通再送](../../../ledger-operation-retries.md)、[競合と後続](../../../ledger-person-conflicts.md)）。
