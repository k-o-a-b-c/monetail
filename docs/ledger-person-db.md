# 人物・参加者のDB構成と更新版

[← 人物API・DBの入口](ledger-person-api.md)

## DBの構成（初回定義へ実装済み）

アカウントとの対応は参加者表に人物IDを持つ構成で利用者確認済み（DEC-059）。人物0人・人物未対応の参加者・アカウントに対応しない人物を認め、人物への対応から認証アカウントを自動作成しない。人物IDのUUID採用・日付の保存形式・更新版の保存単位も確認済み。以下は現在の実装内容。未実装の業務表や画面の設計とは区別する。

- **支払方法の並び順更新版（DEC-129・130・133）を`ledger_people.payment_order_revision`に追加した。** 名前・期間編集用の`revision`とは独立し、人物新規作成時にUUIDを初期化する。現行の新規DBでは人物ごとにUUIDを初期化し、既存DBの追加移行は行わない（[DB初期化](42-api-foundation.md#db初期化登録復旧コマンド)）。支払方法が0件の人物にも保持し、支払方法の並び順APIで照合する。
- `ledger_people` に人物名・有効開始日・有効終了日・共通順・人物の更新版を保存する。期間はSQLiteの `YYYY-MM-DD` 形式のTEXT（制限なしはNULL）、C#では `DateOnly?` とし、開始日・終了日の前後関係をDBのCHECKで保護する（DEC-086）。API形式・検証は[人物API](specs/api/features/persons.md)に従う。
- 同一家計簿の人物名にUNIQUE制約を設け、SQLiteのBINARY比較で同時登録の重複を防ぐ。共通順は各人物行に保存する（DEC-087）。表示順の型・採番・保持は[DBの表示順管理](ledger-core-schema.md#master-order-storage)に従う。非負CHECKによるDB保護は未実装であり、初期DB定義に追加する（Migrationは追加しない）。
- `ledgers.people_order_revision` に家計簿単位の人物順の更新版を保持する。人物0人でも保持する（DEC-087）。
- 人物・参加者の行版はそれぞれ独立した行に保持する（DEC-087）。
- 更新版はUUID文字列として保持する。版の照合・更新条件と原子性は[人物API](specs/api/features/persons.md)・[参加者API](ledger-person-api-contract.md)を正本とする。

## アカウント対応の保存構成（API／DB実装済み）

`ledger_members`にNULL許容の`person_id`を設け、対応なしをNULLで表現する。`ledger_people`はアカウントIDを持たず、人物の名前・有効期間・表示順等を管理する。人物の登録とアカウントの参加は独立させ、対応専用表`ledger_member_people`は作らない。

- 参加者の主キー`(ledger_id, auth_user_id)`と人物IDの単一列で、1アカウントが同一家計簿で対応できる人物を最大1人とする。`(ledger_id, person_id)`へUNIQUE制約を置き、1人物へ複数アカウントが対応することも防ぐ。SQLiteではNULLを複数行に持てるため、人物未対応の参加者を複数登録できる。
- `(ledger_id, person_id)`から`ledger_people`の同じ組へRESTRICTの複合FKを設け、他家計簿の人物へ対応できないようにする。本人の人物IDは現在の参加者行から求める。参加者編集の人物候補は同じ家計簿の人物から取得する。
- 名前・期間は人物行、人物対応は参加者行に保存する。
- 現在の参加者行・役割の確認と更新版照合は[人物API](specs/api/features/persons.md)・[参加者API](ledger-person-api-contract.md)に従う。
- 人物を参照する参加者・支払方法・明細対象者等は家計簿ID＋人物IDの参照制約・索引で保護する。人物への参照が残る場合は外部キー制約（RESTRICT）で削除を拒否し、同時操作による参照切れを防ぐ。一覧表示のために全履歴を走査せず、削除時にDB側で現在の参照を検証する（DEC-060・061）。

実装状況は[API実装台帳](specs/api/README.md)・[画面台帳](screen-inventory.md)を参照する。ブラウザーの人物・参加者管理画面は未実装。参加者の除外・再参加・本人の退会・降格の契約は[参加者API](ledger-person-api-contract.md)・[参加者管理](ledger-participants.md)に従う。コードの責務と依存は[APIアーキテクチャ](architecture-api.md#apiの責務と依存)を参照する。

## 人物IDの採番（API／DB実装済み）

人物IDの採番契約は[人物API](specs/api/features/persons.md)に従う（DEC-062）。

主キーは家計簿ID＋人物ID、人物IDはUUIDのN形式のTEXTとする。同じ家計簿の人物への参照には複合FKを使用する。再送記録の保存構成は[共通再送](ledger-operation-retries.md)に従う。

