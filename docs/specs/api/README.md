# API契約の機能別整理 — 移行台帳

**対象：サーバーAPIの外部契約。** 本台帳では、既存のAPI仕様を機能単位に整理するための参照元、重複、移行状態を管理する。

**状態：人物APIは正本化済み、他機能は移行準備。** 本書は移行台帳でありAPI外部契約の正本ではない。人物APIは[features/persons.md](features/persons.md)、他機能は下表の既存正本を参照する。

**新たに確定した応答責務の変更は未実装。** 操作者の`role`・`myPersonId`を[家計簿API](../../ledger-core-api.md)に集約し、人物・科目・支払方法の成功応答・版競合の`latest`から`currentRole`・`myPersonId`を除く。これらのマスタAPIは403/404業務エラーでも操作者の`permissions`を返さず、Clientは家計簿APIで状態を再取得する。HTTP成功コード、業務制約、サーバー側の認可、再送・更新版・保存の原子性は維持する。実装とAPIテスト・Windows Clientの切替が完了するまでは、新しいJSON契約と現行実装を混同しない。

## 1. API仕様書の記載範囲

- HTTPメソッド・パス・クエリ・ヘッダー、認証と認可、リクエスト・レスポンスのJSON、ステータスコード・エラーコード。
- 入力値の制約、業務ルール、検索・並び順・計算、保存の原子性、更新版照合、再送の保証など**APIが受け付け、返し、保証する内容**。
- 確定済み／提案／未決定、および実装済み／未実装を区別する。実装済みの判定には既存のAPI実装・テストを照合する。
- 物理DBの列・索引・トリガー・運用CLIの手順は関連する正本を参照し、API仕様へ複製しない。認証・セキュリティの共通契約も別の正本を参照する。
- 実装クラスや工程・PR履歴を契約と混同しない。APIの外部契約として観測できない内部処理手順は記載しない。
- 文書移行で業務制約、API/DB、認証・セキュリティ、データ整合性、JSONの省略/null、再送・版競合の確定済み契約を変えない。

## 2. 新しい配置方針

```text
docs/specs/api/
  README.md                    # この移行台帳
  common/                      # 共通契約の存在を確認できたときだけ追加
    http.md                    # 必要ならHTTP・認証境界
    errors.md                  # 必要なら共通エラー本文
    retries-and-revisions.md  # 必要なら共通の再送・版照合
  features/
    persons.md                 # 人物API（正本）
    participants.md            # 後続候補
    ...                        # 機能の移行時だけ追加
```

これは予定構成。存在しないファイルへのリンクは作らない。機能の契約が短ければ1ファイルとし、メソッド単位・DTO単位・実装クラス単位で機械的に分割しない。

## 3. 機能別契約の棚卸し

| API機能 | 既存の契約・業務条件の参照元 | 移行時の確認対象 | 状態 |
| --- | --- | --- | --- |
| 認証・セッション・全体アカウント | [認証API](../../auth-api-contract.md)、[セッション](../../auth-session-security.md)、[パスキー](../../auth-passkeys.md) | HTTP要求・認証・セッション・本人確認・アカウント管理 | 既存正本維持 |
| 家計簿 | [家計簿API](../../ledger-core-api.md)、[共通前提](../../ledger-core-principles.md) | 作成・取得・権限・要求ID・回数制限 | 未移行。`myPersonId`の応答追加は未実装 |
| 人物 | [人物の業務条件・画面](../../ledger-persons.md)、[DB](../../ledger-person-db.md)、[共通再送](../../ledger-operation-retries.md) | GET・POST・PATCH・DELETE・順序PUT、制約・権限・更新版・再送 | **[正本](features/persons.md)**。操作者情報の除去は未実装 |
| 参加者・人物対応 | [参加者API](../../ledger-person-api-contract.md)、[参加者の業務条件](../../ledger-participants.md)、[競合・画面](../../ledger-person-conflicts.md) | 参加者取得・更新・除外、役割・人物対応・権限 | **既存正本維持**（人物APIを分離） |
| 招待・参加受諾 | [招待](../../ledger-invitations.md)、[認証API](../../auth-api-contract.md) | 発行・取得・取消・受諾・認可・再送 | 未移行 |
| 科目 | [家計簿DB・モデル](../../ledger-core-schema.md) | マスタのCRUD・順序API、入力制約・競合・再送 | 未移行。`currentRole`の除去は未実装 |
| 支払方法 | [家計簿DB・モデル](../../ledger-core-schema.md) | 所有者・CRUD・順序・有効期間・評価額の確定/実装状態 | 未移行。`myPersonId`・`currentRole`の除去は未実装 |
| 共通税区分・税率履歴 | [家計簿DB・モデル](../../ledger-core-schema.md) | 税率履歴全件GETの要求/応答と税区分・履歴の業務条件 | 未移行 |
| レシート | [レシートAPI](../../ledger-receipt-api.md)、[履歴候補API](../../ledger-receipt-suggestions.md) | 一覧/詳細・書込・計算・検索候補・再送・エラー | 未移行 |
| 振替 | [振替API](../../ledger-transfer-api.md)、[振替業務条件](../../ledger-transfer-db.md) | CRUD・カーソル・候補・補完・権限・再送 | 未移行 |
| 残高・推算残高 | [残高API](../../ledger-balance-api.md)、[家計簿DB・モデル](../../ledger-core-schema.md) | 一括推算GETの要求/応答・権限・一貫性・未決定項目 | 未移行 |
| 集計・分析 | 具体的なAPI契約の確定状況は未棚卸し | 新規のAPI契約が確定した範囲のみ対象 | 対象範囲未確定 |

物理DBの正本は、[共通DB](../../ledger-core-schema.md)、[人物DB](../../ledger-person-db.md)、[レシートDB](../../ledger-receipt-schema.md)、[振替DB](../../ledger-transfer-db.md)を参照する。

## 4. 共通契約の抽出候補

| 候補 | 比較する既存正本 | 抽出前に確認する差異 |
| --- | --- | --- |
| 認証・HTTP境界 | [セッション](../../auth-session-security.md)、[認証API](../../auth-api-contract.md)、[家計簿API](../../ledger-core-api.md) | CSRF・本文上限・認証方式・適用範囲 |
| エラー本文 | [人物・参加者API](../../ledger-person-api-contract.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | `code`/`message`/`field`/`latest`/`permissions`の出現条件、認証境界、機能固有コード |
| 更新版・競合 | [人物・参加者API](../../ledger-person-api-contract.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md)、[科目・支払方法](../../ledger-core-schema.md) | 対象行版と共通順の全体版、409時の`latest` |
| 要求ID・再送 | [共通再送](../../ledger-operation-retries.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | 対象操作・成功再現・削除後再送・権限再検証・レスポンスの差異 |
| カーソル | [レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | フィルター・並び順・不正カーソルの扱い |
| 回数制限 | [認証API](../../auth-api-contract.md)、[家計簿API](../../ledger-core-api.md)、[人物の競合・制限](../../ledger-person-conflicts.md) | 認証系と業務APIの異なる枠・対象操作 |

**共通化は未決定。** 各機能の保証が異なる場合は、共通仕様として無理に統一せず、機能別契約に残す。

## 5. 既存の重複と整理先

| 既存文書 | API契約として移行する項目 |
| --- | --- |
| [参加者API](../../ledger-person-api-contract.md) | 人物5操作は[正本](features/persons.md)へ移行済み。参加者4操作の契約は本書に維持 |
| [人物の業務条件](../../ledger-persons.md) | APIの名前・期間・削除制限・権限は[正本](features/persons.md)を参照し、本書は画面・後続業務の判断を維持 |
| [人物の競合](../../ledger-person-conflicts.md) | `revision`、403/404からの家計簿コンテキスト再取得、409の`latest`、再送保証 |
| [家計簿DB・モデル](../../ledger-core-schema.md) | 科目・支払方法・税率履歴のAPI契約。物理DB契約はその正本に残す |
| [レシートAPI](../../ledger-receipt-api.md) | HTTP契約・金額計算・検証・検索。工程記録と区別 |
| [履歴候補API](../../ledger-receipt-suggestions.md) | 検索・正規化・重複判定・取得権限・応答 |
| [振替API](../../ledger-transfer-api.md) | CRUD・候補・補完・カーソル・権限・再送 |
| [残高API](../../ledger-balance-api.md) | 確定済みの推算GETのHTTP・JSON・権限・読取一貫性 |

## 6. 機能単位の移行

1. 既存正本と現行API実装・APIテストを照合し、メソッド・パス・要求・応答・制約・権限・エラー・更新版・再送・原子性の欠落を探す。確定/提案/未決定、実装/未実装を分ける。
2. 新規の機能別文書に既存の確定した**API契約だけ**を整理する。共通の外部契約はリンクし、推測で新しい値や規則を追加しない。
3. 旧文書の節と新文書の節との対応を確認し、未移行の契約や食い違いを明記する。
4. 承認後に**対象機能の正本だけ切り替える**。[文書INDEX](../../INDEX.md)の契約参照先を更新し、旧文書の当該API契約の重複を案内に置き換える。
5. 移行状態を更新する。API/DBの実装変更を伴わない文書整理では、ビルドやAPIテストの未実施を未実施と報告する。

**人物API**（GET/POST/PATCH/DELETE・共通順PUT）は移行済み。次の機能も確定した契約を照合してから移行する。参加者API・共通の認証・再送契約・物理DBは各正本を維持する。
