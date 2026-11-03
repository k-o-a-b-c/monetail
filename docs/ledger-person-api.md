# 人物管理API・DBと共通再送

[← 58 家計簿設計・文書案内](58-ledger-foundation-design.md)

<a id="人物管理apidbの具体案一部確認済み未実装"></a>

この文書は人物管理API・DBの**分野別入口**です。APIパス・DTO・保存構成・再送・競合の正本を分離しました。Codexは担当機能に必要なファイルだけを読んでください。旧見出しリンクはこの場所から各正本へ辿れます。

## 分野別の正本

| 参照対象 | 正本 |
| --- | --- |
| 人物・参加者の9操作、DTO、成功／失敗応答 | [API・JSON契約](ledger-person-api-contract.md) |
| 人物・参加者のSQLiteテーブル、制約、更新版、UUID | [DB構成](ledger-person-db.md) |
| 共通の作成要求、6列の記録、再送と保持条件 | [共通再送管理](ledger-operation-retries.md) |
| 同時編集、権限喪失、未実装画面と残項目 | [競合・後続確認](ledger-person-conflicts.md) |

## 旧見出しの案内

[人物管理API・DB（実装済み）](ledger-person-api-contract.md#人物管理apidb実装済み)
