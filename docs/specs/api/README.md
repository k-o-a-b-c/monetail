# API・業務契約の機能別整理 — 移行台帳

**対象**：monetailのサーバーAPIが保証する業務条件・HTTP契約。Windows/WinUI側の画面仕様は、APIの契約を参照して独自の表示・入力・復元操作だけを定める。

**状態：棚卸しのみ。** 本書は移行先の一覧・作業範囲を示すための文書で、**API契約そのものの正本ではない**。下表の既存正本は引き続き有効。新規エンドポイントやエラーを決定したり、実装状態を変更したりしない。機能別の新しい契約本文は未作成である。

## 1. 正本の分離原則

- **API／業務契約**：サーバー側の制約・権限・値の意味・保存条件、HTTPメソッドとパス、要求・応答のJSON、エラー、更新版、原子性、再送、機能固有の検索・並び順・計算を扱う。利用者にとっての振る舞いの正本とする。
- **DB物理仕様**：テーブル・列・FK・インデックス・トリガー・初期化等は既存のDB正本へリンクする。APIから観測できる制約だけをAPI仕様側で説明し、物理実装を重複転記しない。
- **WinUI／クライアント仕様**：画面配置、ボタン、フォーカス、選択、入力補助、通信失敗時の画面表示、未保存値の保持・復帰などに限定。APIが保証する範囲・エラーコード・JSONの正本はリンクする。
- **共通と機能固有**：共通契約は複数機能に本当に同じ意味で適用される部分だけ抽出。認証境界・作成再送・版競合・カーソル・回数制限には機能・操作別の例外があるため、無条件の全API共通化をしない。
- **契約と実装記録**：現在の契約、確定済みだが未実装の契約、未決定事項を区別する。PR番号・実装工程・当時のテスト結果は機能契約の本文から外し、適切な構成資料／テスト資料を参照する。
- **変更の禁止**：文書移行を理由に既存の業務仕様、API/DB、認証・セキュリティ、401・403・404・409等の挙動、JSONの省略/nullや再送条件を変更しない。齟齬を発見しても推測で「統一」しない。

## 2. 新しい配置方針

```text
docs/specs/api/
  README.md                    # この台帳。移行が済むまで既存正本への入口
  common/                      # 複数機能で実質同じ契約だけを後で抽出
    http.md                    # 必要な場合のみ：HTTP・認証境界
    errors.md                  # 必要な場合のみ：共通エラー本文
    retries-and-revisions.md  # 必要な場合のみ：再送・更新版の共通原則
  features/
    persons.md                 # 最初の移行対象
    participants.md
    ...                        # 実際の移行時に必要なファイルだけ追加
```

**上記は予定構成であり、まだ存在しないファイルへのリンクではない。** 機能の契約が短ければ1ファイルで管理する。APIメソッド単位・DTO単位・実装クラス単位の細分化はしない。

## 3. 機能別API契約の棚卸し

| API・業務機能 | 現行の正本／主な移行元 | 移行時の切り分け | 新正本 |
| --- | --- | --- | --- |
| 認証・セッション・全体アカウント | [認証API](../../auth-api-contract.md)、[セッション保護](../../auth-session-security.md)、[認証・パスキー](../../auth-passkeys.md) | 公開HTTP契約とセキュリティ前提を参照関係で整理。Windows認証操作やWeb画面は対象外 | 未移行（既存正本を維持） |
| 家計簿の作成・選択・参照 | [家計簿API](../../ledger-core-api.md)、[共通前提](../../ledger-core-principles.md) | 家計簿の業務条件・要求応答を整理。選択UIは含めない | 未移行 |
| 人物 | [人物・参加者API](../../ledger-person-api-contract.md)、[人物管理](../../ledger-persons.md)、[競合と後続](../../ledger-person-conflicts.md) | 人物GET/POST/PATCH/DELETE・共通順PUT、名前・有効期間・参照制限・権限・409/再送を集約。画面動作と参加者APIを分離 | **[人物API草案あり・正本未切替](features/persons.md)** |
| 参加者・人物対応 | [人物・参加者API](../../ledger-person-api-contract.md)、[参加者管理](../../ledger-participants.md)、[競合と後続](../../ledger-person-conflicts.md) | 参加者GET/編集GET/PATCH/DELETE・役割と人物対応・権限境界。人物のCRUDとは分離 | 未移行 |
| 招待・参加受諾 | [招待](../../ledger-invitations.md)、[認証API](../../auth-api-contract.md) | 招待発行・確認・取消・受諾のHTTP/権限。招待Web/WinUIの結果不明時表示はUI側 | 未移行 |
| 科目 | [家計簿DB・モデル](../../ledger-core-schema.md)（科目マスタ、CRUD、並び順） | API契約・業務制約を抜き出し、DB列・WinUI画面・初期値を分離 | 未移行 |
| 支払方法 | [家計簿DB・モデル](../../ledger-core-schema.md)（所有者、CRUD、並び順、評価額属性） | CRUD・所有者・権限・有効期間・順序契約を整理。未実装属性の扱いを明示し、DB/WinUIと分離 | 未移行 |
| 共通税区分・税率履歴 | [家計簿DB・モデル](../../ledger-core-schema.md)（共通税率履歴GETと同期CLI） | GETのHTTP契約と履歴の業務意味を整理。CLIによるデータ運用とClientキャッシュ・画面初期選択は別責務 | 未移行 |
| レシート | [レシートAPI](../../ledger-receipt-api.md)、[履歴候補と補完](../../ledger-receipt-suggestions.md) | 一覧/詳細、全体書込、計算・検証、競合/再送、履歴候補APIを整理。工程記録とAutoSuggestBox等のUIは分離 | 未移行 |
| 振替 | [振替API](../../ledger-transfer-api.md)、[振替DB](../../ledger-transfer-db.md) | CRUD・カーソル・振替名候補・自動補完・権限・再送。WinUIの手動入力保護/表示は分離 | 未移行 |
| 残高・推算残高 | [残高API](../../ledger-balance-api.md)、[家計簿DB・モデル](../../ledger-core-schema.md)（評価額と残高連携） | 確定済みの一括推算GETと未決定/未実装の境界を保持。取得失敗セル・InfoBar・再試行UIはWindows仕様へ | 未移行 |
| 集計・分析 | [共通UI](../../../UI_DESIGN.md)、[画面台帳](../../screen-inventory.md) | 現在は画面設計中心。API契約未決定の範囲をAPI実装済みと誤認せず、契約確定時に機能正本を作成 | API詳細未確定・移行対象外 |

**DBの現行正本**：共通の[家計簿DB・モデル](../../ledger-core-schema.md)、[人物DB](../../ledger-person-db.md)、[レシートDB](../../ledger-receipt-schema.md)、[振替DB](../../ledger-transfer-db.md)。API整理後も物理DBの契約をそのままAPI文書に複製しない。

## 4. 共通契約の候補と現在の根拠

| 候補 | 既存の参照元 | 共通化前に確認すること |
| --- | --- | --- |
| 認証・CSRF・HTTP境界 | [セッション保護](../../auth-session-security.md)、[認証API](../../auth-api-contract.md)、[家計簿API](../../ledger-core-api.md) | CookieとOAuth、HTTP本文制限、エンドポイントごとの適用例外。現在の認証/セキュリティ正本の統合を先行しない |
| エラー本文とHTTPステータス | [人物・参加者API](../../ledger-person-api-contract.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | `code`/`message`/`field`/`latest`/`permissions`の出現条件・省略/null、認証境界の例外、機能固有のエラーコード |
| `revision`と競合 | [人物・参加者API](../../ledger-person-api-contract.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md)、[科目・支払方法契約](../../ledger-core-schema.md) | 行版・一覧の全体順序版、409時のlatest、失敗後の契約の差異。画面の「最新に切替」はWindows側 |
| `requestId`と結果不明時の再送 | [共通再送](../../ledger-operation-retries.md)、[人物・参加者API](../../ledger-person-api-contract.md)、[レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | メソッド別の必須条件・同一性・成功再現、削除後の再送、401/権限再確認、書込成功時の本文あり/なしの違い |
| 検索・カーソル | [レシートAPI](../../ledger-receipt-api.md)、[振替API](../../ledger-transfer-api.md) | クエリ名・整列順・カーソル検証・エラーの違いを維持。全機能共通のページング方式とは決めない |
| 回数制限 | [認証API](../../auth-api-contract.md)、[家計簿API](../../ledger-core-api.md)、[競合と後続](../../ledger-person-conflicts.md) | 認証系と家計簿の参照/書込の枠の違い。値を横断的に揃えることを目的にしない |

**ここでは共通契約の存在・統一仕様を確定していない。** 上記を照合した結果、機能ごとの固有契約として残す方が短く正確なら、新しい共通ファイルは作らない。

## 5. 確認した重複・責務混在と移行方針

| 現行箇所 | 混在している内容 | 移行時の扱い |
| --- | --- | --- |
| [人物管理](../../ledger-persons.md) | APIの人物名/期間/参照制約と人物一覧・ContentDialog・競合表示・401復帰 | API保証は人物API正本へ、画面の表示/操作は人物WinUI仕様へ。移行後は相互リンク |
| [人物・参加者API](../../ledger-person-api-contract.md) | 人物と参加者の異なる操作・権限 | 別の機能契約へ分け、共通となるHTTP原則だけリンク |
| [家計簿DB・モデル](../../ledger-core-schema.md) | 科目・支払方法のAPI、物理DB、WinUI、税率履歴、残高の未実装設計 | 機能API・DB物理仕様・WinUI仕様に責務を分離。税率同期CLIをHTTP書込APIと誤認しない |
| [レシートAPI](../../ledger-receipt-api.md) | HTTP契約、計算契約、実装工程・内部方式、保存後のWinUI動作 | 外部契約と計算の保証を残し、実装経緯・WinUI操作は別正本へ |
| [履歴候補と補完](../../ledger-receipt-suggestions.md) | 候補APIの検索・応答とAutoSuggestBoxの操作・フォーカス・失敗表示 | API側検索/取得とWindows側の候補UIに分ける |
| [振替API](../../ledger-transfer-api.md) | APIの候補・自動補完とUIの手動選択保護・競合復旧 | APIが返す候補/権限を正本にし、UI保持・自動上書き禁止の表示操作はWindows側に |
| [残高API](../../ledger-balance-api.md) | 推算GETとWinUIの取得失敗セル・反映無効化・InfoBar再試行 | GETのJSON/権限/読取一貫性だけAPI側。画面状態・失敗表示・明示再試行はWindows側 |

ここに列挙した差異は**文書の整理対象であって仕様変更ではない**。実際の移行では見出し・DEC・テスト・現行API実装を照合し、内容が失われないことを確認する。

## 6. 機能を1つずつ移行する手順

1. 対象APIの現行正本を一通り集め、HTTPパス・リクエスト・応答・権限・入力制約・エラー・revision/requestId・再送・原子性と、確定/提案/未決定、実装/未実装を照合する。矛盾は解消せず差分として明示。
2. 最小の機能別新仕様へ**既存の確定契約**を整理し、API以外の画面・物理DB・実装履歴は参照リンクとする。新しい挙動・値・エラーを創作しない。
3. 「旧節 → 新節／他正本」の対応表で欠落・意味の変更・参照切れをチェックし、レビュー用草案にする。
4. **ユーザー確認後に対象機能だけ正本切替**：必要な旧文書の重複部分を削除または案内へ変更し、[docs/INDEX.md](../../INDEX.md)とWindows側の参照先を更新する。新旧の2か所を正本として保守しない。
5. 下表の移行状態を更新する。文書整理によるAPI/Client/WinUI/DBコードの変更は行わず、コード検証が未実施なら未実施と記録する。

**最初は人物API**（人物GET/POST/PATCH/DELETE・共通順PUT）。参加者の役割・人物対応、認証・共通再送・DB物理は別正本を参照し、人物固有のHTTP/業務保証だけを集約する。

人物API移行後に、保留中の[人物WinUI草案](https://github.com/k-o-a-b-c/monetail/pull/242)をAPI正本への参照中心に見直す。PR #242は現時点で正本切替・mainマージの対象としない。
