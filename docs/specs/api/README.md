# API契約の機能別整理・実装台帳

**対象：サーバーAPI。** 機能別にAPI契約の参照先・文書移行状況・実装状況・確定仕様との未対応差分を管理する。

**本書はAPIの実装状況・文書移行の台帳であり、外部契約の正本ではない。** API仕様は第3節の参照先、WinUIの実装状況は[画面台帳](../../screen-inventory.md)、開発の優先順位は[ロードマップ](../../07-roadmap.md)、検証・CIは[テスト台帳](../../../tests/README.md)で管理する。

**新たに確定した応答責務の変更は未実装。** 操作者の`role`・`myPersonId`を[家計簿API](../../ledger-core-api.md)に集約し、人物・科目・支払方法の成功応答・版競合の`latest`から`currentRole`・`myPersonId`を除く。これらのマスタAPIは403/404業務エラーでも操作者の`permissions`を返さず、Clientは家計簿APIで状態を再取得する。HTTP成功コード、業務制約、サーバー側の認可、再送・更新版・保存の原子性は維持する。実装とAPIテスト・Windows Clientの切替が完了するまでは、新しいJSON契約と現行実装を混同しない。

## 1. API仕様書の記載範囲

- HTTPメソッド・パス・クエリ・ヘッダー、認証と認可、リクエスト・レスポンスのJSON、ステータスコード・エラーコード。
- 入力値の制約、業務ルール、検索・並び順・計算、保存の原子性、更新版照合、再送の保証など**APIが受け付け、返し、保証する内容**。
- **機能別API正本は確定した最終契約だけを記載する。** 現行コードが未対応でも、承認済みの契約を正本とし、現行との差分・実装状況・移行手順は本台帳で管理する。提案・未決定事項は正本と分離し、承認前に契約へ混入させない。
- 旧仕様・変更理由・判断経緯・実装履歴を本文に残さない。確定した要求・応答項目を漏れなく列挙し、廃止済み項目を「含めない」と列挙し直さない。ただし不正入力の拒否、禁止操作、権限制約など、最終契約として意味のある否定条件は維持する。
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
    categories.md             # 科目API（正本）
    participants.md            # 後続候補
    ...                        # 機能の移行時だけ追加
```

これは予定構成。存在しないファイルへのリンクは作らない。機能の契約が短ければ1ファイルとし、メソッド単位・DTO単位・実装クラス単位で機械的に分割しない。

## 3. 機能別の実装状況・契約参照先

**判定基準（APIのみ）**：実装済み＝対象範囲の確定API契約に対応するコードがある。一部実装済み＝実装済みのAPIがあり、確定契約への未対応部分が残る。未実装＝対象APIがない。未決定の仕様を実装済みと数えず、文書移行・WinUI接続・運用環境での検証とは区別する。状態は現行コード・関連テストと確定契約を照合して判断する。

| API機能 | API実装状況 | 実装範囲・残る差分 | 契約の参照先 | 文書移行 |
| --- | --- | --- | --- | --- |
| 認証・セッション・パスキー | 実装済み | 登録・自己登録・ログイン・本人再確認・ログアウト・鍵管理・デスクトップOAuth。公開運用の準備や未確定の復旧・退会は別途 | [認証API](../../auth-api-contract.md)、[セッション](../../auth-session-security.md)、[パスキー](../../auth-passkeys.md) | 既存正本維持 |
| 全体アカウント管理 | 実装済み | 一覧・作成・登録招待の発行／取消・管理メモ。全失効・緊急復旧はCLI。管理者の全失効HTTP経路・権限変更は未実装で、追加契約は未確定 | [認証API](../../auth-api-contract.md) | 既存正本維持 |
| 家計簿 | 一部実装済み | 作成・一覧・個別参照・指定／最後の家計簿を開く。操作者情報の集約（上記）未対応。削除APIは後続設計 | [家計簿API](../../ledger-core-api.md)、[共通前提](../../ledger-core-principles.md) | 未移行 |
| 人物 | 一部実装済み | CRUD・共通順PUT・既存レシート／振替の期間検証。操作者情報の集約（上記）未対応。残高等の期間検証は各業務API実装時 | [人物API](features/persons.md)、[人物の画面仕様](../../ledger-persons.md)、[DB](../../ledger-person-db.md)、[共通再送](../../ledger-operation-retries.md) | 移行済み |
| 参加者・人物対応 | 実装済み | 一覧・編集情報GET・役割／人物対応の更新・除外 | [参加者API](../../ledger-person-api-contract.md)、[参加者の業務条件](../../ledger-participants.md) | 既存正本維持 |
| 招待・参加受諾 | 実装済み | 一覧・発行・取消・プレビュー・受諾 | [招待](../../ledger-invitations.md)、[認証API](../../auth-api-contract.md) | 未移行 |
| 科目 | 一部実装済み | CRUD・順序PUT。操作者情報の集約（上記）、POST成功応答の統一、revisionのみのPATCHの無変更成功、同順PUTの無変更成功、既存レシートを期間外にする科目期間変更の拒否、使用済み科目の収支変更拒否のHTTP 409（現行400）が未対応。詳細は下記 | [科目API](features/categories.md)、[DB・管理画面](../../ledger-core-schema.md#category-master) | 移行済み |
| 支払方法 | 一部実装済み | CRUD・所有者別順序PUT・既存レシート／振替の期間検証。操作者情報の集約・評価額属性が未対応。残高等の期間検証は各業務API実装時 | [家計簿DB・モデル](../../ledger-core-schema.md) | 未移行 |
| 共通税区分・税率履歴 | 実装済み | 全履歴GET。履歴管理は実装済みの同期CLIを使用し、HTTP書込APIは対象外 | [家計簿DB・モデル](../../ledger-core-schema.md) | 未移行 |
| レシート | 実装済み | 一覧／詳細GET・POST／PUT／DELETE・保存行フォーカス・履歴候補／自動補完GET・計算／検証。メモの空文字正規化も対応済み | [レシートAPI](../../ledger-receipt-api.md)、[履歴候補API](../../ledger-receipt-suggestions.md) | 未移行 |
| 振替 | 実装済み | 一覧／詳細GET・POST／PUT／DELETE・保存行フォーカス・名称候補／自動補完GET。残高連動は残高API実装時 | [振替API](../../ledger-transfer-api.md)、[振替業務条件](../../ledger-transfer-db.md) | 未移行 |
| 残高・推算残高 | 未実装 | 評価額対応を含む登録／更新／削除・履歴取得・推算一括GET、人物／支払方法の期間検証。推算GETは契約の確定範囲あり、保存／履歴のHTTP詳細等は後続設計 | [残高API](../../ledger-balance-api.md)、[家計簿DB・モデル](../../ledger-core-schema.md) | 未移行 |
| 支払メモ | 未実装 | 記録・取得・日付付き参照の期間検証。API詳細は後続設計 | [共通UI](../../../UI_DESIGN.md)、[家計簿DB・モデル](../../ledger-core-schema.md) | 契約未整理 |
| 集計・分析 | 未実装 | 科目別・収支・残高の集計と内訳。API詳細は後続設計 | [共通UI](../../../UI_DESIGN.md)、[新旧比較台帳](../../legacy-new-final-parity-matrix.md) | 契約未整理 |
| ポイント | 未実装 | ポイント設定・取引。採用範囲・API詳細は後続設計 | [家計簿DB・モデル](../../ledger-core-schema.md) | 契約未整理 |

**更新ルール**：API契約の確定・変更、またはAPI実装の追加・完了に伴って状態／未対応差分が変わるとき、同じPRで本表の該当行だけ更新する。実装変更がなく文書だけ移行した場合は文書移行列だけを更新する。実装済みの判定はコードと必要なテストを確認し、未確認事項を完了扱いしない。文書移行やPRの進捗、テスト件数、実行ログを実装状況として転記しない。

**科目の確定契約と現行との差分・確認事項**：旧科目CRUD契約では使用済み科目の収支区分変更は409で拒否すると明記されていたが、`CategoryManagement.cs`は400 `invalid_input`（`field=incomeExpense`）、`ReceiptApiSafetyTests.cs`も400を期待する。業務上の変更禁止はDEC-147で確定している。HTTP 409を正本に維持し、機能固有のエラーコード・fieldの最終値は未確定のため決定後にAPI・テスト・Clientを整合させる。`CategoryPeriodApiTests.cs`の参照プローブは実レシートグループではなく、実際の使用済み判定を覆す根拠ではない。操作者情報の集約に伴う成功応答・エラー本文の切替も未対応。POSTの初回成功・再送成功は他の4操作と同じ一覧応答に統一することを利用者確認済み（2026-11-24）。現行APIのトップレベル`categoryId`とClientの`CategoryCreationInfo`は切替対象。科目一覧内のIDと再送記録の`result_id`は維持し、API・Client・関連テストを整合させる。既存取引を期間外にする科目の期間変更を拒否することも利用者確認済み（2026-11-24）。現行`CategoryManagement.cs`は期間の前後関係のみを検証し、既存レシートの取引日との照合は未実装。エラー形式も利用者確認済み（2026-11-24）。確定契約は[科目API第6節](features/categories.md#6-競合失敗応答)を参照。実レシート参照、両端日、null、片側変更、同時保存、拒否時のデータ・版保持を実装時に検証する。共通順PUTは人物APIと同様に、ID順が変わらない場合は表示順値と順序版を維持することを利用者確認済み（2026-11-24）。現行`CategoryOrderManagement.cs`は同順・0件でも順序版更新と連番設定を行う。変更後は同順（欠番・同順位を含む）・0件・1件で版と値を維持し、無変更でも古い版・不正な全ID集合を拒否すること、実際の順変更では連番・新順序版となり行版を維持することを検証する。`revision`のみのPATCHも人物APIと同じ無変更成功に統一することを利用者確認済み（2026-11-24）。現行`CategoryManagement.cs`とClientの`CategoryChanges`は変更項目なしを拒否する。API・Client・関連テストを整合させ、正しい版で200・行版と順序版の維持、古い版で409、権限・存在・業務制約の検証維持を確認する。実装・テストの変更は文書移行の対象外。

物理DBの正本は、[共通DB](../../ledger-core-schema.md)、[人物DB](../../ledger-person-db.md)、[レシートDB](../../ledger-receipt-schema.md)、[振替DB](../../ledger-transfer-db.md)を参照する。

## 4. 共通契約の抽出候補

| 候補 | 比較する既存正本 | 抽出前に確認する差異 |
| --- | --- | --- |
| 認証・HTTP境界 | [セッション](../../auth-session-security.md)、[認証API](../../auth-api-contract.md)、[家計簿API](../../ledger-core-api.md) | CSRF・本文上限・認証方式・適用範囲 |
| エラー本文 | [人物API](features/persons.md)、[参加者API](../../ledger-person-api-contract.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | `code`/`message`/`field`/`latest`/`permissions`の出現条件、認証境界、機能固有コード |
| 更新版・競合 | [人物API](features/persons.md)、[参加者API](../../ledger-person-api-contract.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md)、[科目API](features/categories.md)、[支払方法](../../ledger-core-schema.md) | 対象行版と共通順の全体版、409時の`latest` |
| 要求ID・再送 | [共通再送](../../ledger-operation-retries.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | 対象操作・成功再現・削除後再送・権限再検証・レスポンスの差異 |
| カーソル | [レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | フィルター・並び順・不正カーソルの扱い |
| 回数制限 | [認証API](../../auth-api-contract.md)、[家計簿API](../../ledger-core-api.md) | 認証系と業務APIの異なる枠・対象操作 |

**共通化は未決定。** 各機能の保証が異なる場合は、共通仕様として無理に統一せず、機能別契約に残す。

## 5. 既存の重複と整理先

| 既存文書 | API契約として移行する項目 |
| --- | --- |
| [参加者API](../../ledger-person-api-contract.md) | 人物5操作は[正本](features/persons.md)へ移行済み。参加者4操作の契約は本書に維持 |
| [人物の画面仕様](../../ledger-persons.md) | APIの名前・期間・削除制限・権限は[正本](features/persons.md)を参照し、本書は画面・後続業務の判断を維持 |
| [人物の競合](../../ledger-person-conflicts.md) | 画面の競合・権限喪失・再送時の動作。API契約は[人物API](features/persons.md)と[家計簿API](../../ledger-core-api.md)を参照 |
| [家計簿DB・モデル](../../ledger-core-schema.md) | 科目APIは[正本](features/categories.md)へ移行済み。支払方法・税率履歴のAPI契約は今後移行。物理DB・管理画面は既存正本に残す |
| [レシートAPI](../../ledger-receipt-api.md) | HTTP契約・金額計算・検証・検索。工程記録と区別 |
| [履歴候補API](../../ledger-receipt-suggestions.md) | 検索・正規化・重複判定・取得権限・応答 |
| [振替API](../../ledger-transfer-api.md) | CRUD・候補・補完・カーソル・権限・再送 |
| [残高API](../../ledger-balance-api.md) | 確定済みの推算GETのHTTP・JSON・権限・読取一貫性 |

## 6. 機能単位の移行

1. 既存正本と現行API実装・APIテストを照合し、メソッド・パス・要求・応答・制約・権限・エラー・更新版・再送・原子性の欠落を探す。確定/提案/未決定、実装/未実装を分ける。
2. 機能別正本に確定した最終API契約だけを整理する。共通の外部契約はリンクし、推測で新しい値や規則を追加しない。現行との差分や実装状況は本台帳へ記載する。
3. 旧文書の節と新文書の節との対応を確認し、未移行の契約や食い違いを台帳に記録する。正本のレビューでは、確定事項の欠落・意味の変更・重複・旧仕様や実装状況の混入・参照切れがないことを確認する。
4. 承認後に**対象機能の正本だけ切り替える**。[文書INDEX](../../INDEX.md)の契約参照先を更新し、旧文書の当該API契約の重複を案内に置き換える。
5. 第3節の文書移行列を更新する。実装状況は実際のAPI実装と確定契約の差分が変わった場合だけ更新する。API/DBの実装変更を伴わない文書整理では、ビルドやAPIテストの未実施を未実施と報告する。

**人物API・科目API**（GET/POST/PATCH/DELETE・共通順PUT）は移行済み。次の機能も確定した契約を照合してから移行する。参加者API・共通の認証・再送契約・物理DBは各正本を維持する。
