# 参加者API・JSON契約

[← 人物API・DBの入口](ledger-person-api.md) ／ [人物5操作のAPI正本](specs/api/features/persons.md)

人物のGET・POST・PATCH・DELETE・共通順PUTの外部契約は[人物API](specs/api/features/persons.md)を正本とする。本書は**参加者4操作**の契約を保持する。人物とアカウントの対応は参加者の更新操作で保存し、人物APIでは変更しない。

<a id="人物管理apidb実装済み"></a>

## 対象と権限（実装済み）

全パスは`/ledgers/{ledgerId}`からの相対パス。4操作はいずれも、その家計簿の**現在のAdminのみ**が使用できる。APIは有効な認証・参加・権限を要求ごとに確認し、書込の確定前にも再検証する。

| メソッド・パス | 内容 |
| --- | --- |
| `GET /members` | 参加者一覧。本人を先頭にし、残りは人物対応ありを共通人物順、未対応を参加日時・内部ID順で返す |
| `GET /members/{accountId}/edit` | 対象参加者の編集情報と、同一家計簿の人物候補を一度に取得する |
| `PATCH /members/{accountId}` | 役割と人物対応を1要求・同じトランザクションで更新する |
| `DELETE /members/{accountId}` | 対象参加者を除外する。参加者行・人物対応を削除し、人物・アカウント本体は削除しない。対象アカウントの家計簿一覧からの除外と順序版の更新を同一トランザクションで確定する（[家計簿API](specs/api/features/ledgers.md)） |

## リクエスト本文

- PATCH：`revision`必須。`role`（`Admin`・`Editor`・`Viewer`）と`personId`は省略可能。**省略した項目は変更しない**。`personId: null`は対応解除、UUIDは同一家計簿の人物への対応を表す。役割の`null`は認めない。実値が同じなら参加者の更新版は変えない。
- DELETE：対象参加者の`revision`をJSON本文で必ず送る。
- 管理者は**自分の役割を変更できず、自分を除外できない**。自分自身の人物対応だけを更新することは禁止しない。
- 1人物に複数アカウントを対応付けず、別の参加者に対応済みの人物を奪わない。対応なしの参加者は複数存在してよい。

## 成功応答

4操作の成功は`200 OK`。GET一覧・PATCH・DELETEの応答は同じ`members`配列・`currentAccountId`・`currentRole`の形式を使う。参加者1件の項目は`accountId`・`displayName`・`role`・`personId`・`personName`・`personDisplayOrder`・`joinedAtUtc`・`revision`。更新・除外の成功時は現在の一覧を同じ応答で返す。

GET編集情報は`member`（上記と同じ参加者項目）と`personCandidates`を返す。各候補は`personId`・`name`・`isSelectable`。別の参加者に対応済みの人物も候補に含め、`isSelectable: false`とする。

## 失敗・競合

- 入力不備は400 `invalid_input`、権限不足は403、対象家計簿の不在・未参加・参加者不在は404。認証失効・受付前の拒否は共通契約による。
- 自分の役割変更は403 `self_role_change`、自分の除外は403 `self_exclusion`。
- 他参加者への対応済み人物の指定は409 `person_already_assigned`（`field: "personId"`）。更新版不一致は409 `revision_conflict`とし、`latest`に最新の編集情報（`member`・`personCandidates`）を返す。
- 業務エラーは`code`・`message`を基本とし、必要に応じて`field`・`latest`・`permissions`を返す。403/404では現在の操作者の権限情報を`permissions`として返す。権限喪失時に閲覧できない情報を返さない。更新が失敗した場合、役割と人物対応の一部だけを保存しない。

画面動作は[参加者管理](ledger-participants.md)、更新版の保存単位・FKは[人物DB](ledger-person-db.md)、共通の認証・再送条件はそれぞれの正本を参照する。
