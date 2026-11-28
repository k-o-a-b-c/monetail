# 振替DB・業務値仕様（DB実装済み）

更新: 2026-11-15。振替の**業務値・DB物理制約・トランザクション・更新版**の正本。振替DBの確定範囲は実装済み、振替APIは一覧GET・詳細GET・新規POST・更新PUT・削除DELETEを実装済み。正式リリース前はMigrationを追加せず、`initialize-db`で新規DBの最新スキーマを作る。

参照：[入口・残課題](ledger-transfer.md)／[API契約](ledger-transfer-api.md)／[WinUI画面](ledger-transfer-ui.md)／[既存レシートDB](ledger-receipt-schema.md)／[家計簿DBモデル](ledger-core-schema.md)。

## DB実装の範囲

`Features/Monetail/Transfers/LedgerTransfer`・`LedgerTransferModel`を`ApiDbContext`へ登録し、既存のモデル連動`initialize-db`が空の新規DBに13列・複合PK・5本のRESTRICT FK・下記4件のCHECK・一覧索引`ix_ledger_transfers_transaction`を作成する。既存EF規約のFK用索引を維持し、追加の検索索引・独自トリガー・Migrationは設けない。

通常起動や再初期化で既存DBを変更しない。振替表・索引がない旧構造は既存の構造検証で拒否するため、既存ファイルを保持して別の新規DBを準備する。[初期化手順](42-api-foundation.md#db初期化登録復旧コマンド)に従い、手動削除・自動修復で検査を回避しない。

DB工程はEFマッピング・物理制約・新規DB初期化まで。新規POSTのUUID/更新版発行・監査値設定・入力検証・成功記録との同時確定はAPI側で実装済み。PUTの原子的版照合・無変更判定・更新監査・成功記録の同時確定もAPI側で実装済み。DELETEの原子的版照合・成功記録の同時確定もAPI側で実装済み。DB制約は変更しない。POST/PUT/DELETEおよび一覧/詳細GETの実装範囲は[API正本](ledger-transfer-api.md#実装範囲)を参照。DBに追加のUUID/日時CHECKは設けていない。SQLite標準CHECKの文字列検証の限界は下記の確定仕様を維持する。検証範囲と結果は[tests/README.md](../tests/README.md#振替db確定範囲の実装)を参照する。

## 業務と入力値（確定）

- 振替は**同一家計簿内の異なる支払方法間の資金移動**。振替元の残高から金額を減算し、振替先に加算する。収入・支出には計上しない。両側が異なる所有人物でもよい（権限は[振替の権限](ledger-transfer-api.md)）。
- **振替IDはサーバーが発行するUUID v7**とする（2026-11-15確定）。クライアントは振替IDを採番しない。**振替テーブルの主キーは`(ledger_id, transfer_id)`の複合主キー**とし、家計簿ごとに振替を識別する（2026-11-15確定）。UUIDのDB表記はハイフンなしの32文字N形式とする。API表記は[振替API](specs/api/features/transfers.md)に従う。FK・索引の確定範囲は以下および[残課題](ledger-transfer.md)を参照する。
- **振替テーブル名は`ledger_transfers`とする**（2026-11-15確定）。レシートと同じ家計簿別の業務テーブルとして設計し、未決定の物理列・制約・索引は[入口の残課題](ledger-transfer.md)で管理する。
- **家計簿参照は`FK(ledger_id) → ledgers(id)`とし、`ON DELETE RESTRICT`を指定する**（2026-11-15確定）。振替が存在する家計簿を物理削除できず、振替を連鎖削除しない。振替の複合主キー`(ledger_id, transfer_id)`は維持する。
- **振替一覧のキーセットページング用複合索引**として、`ledger_transfers(ledger_id, transaction_at, transfer_id)`を追加する（2026-11-15確定）。同一家計簿の取引日時＋振替IDを使った安定した走査に用い、レシートと同様にSQLiteの昇順B-tree逆走査を活用可能とする。**一覧順序は`ORDER BY transaction_at DESC, transfer_id DESC`で確定**（2026-11-15）。同日時もID降順で安定化する。カーソルの表現と保護は[振替API契約](ledger-transfer-api.md)で確定済み。振替元・先などの検索用追加索引は**計画・実測で必要性が確認された場合のみ**検討する（[振替API契約](ledger-transfer-api.md)）。
- **振替1件ごとに`revision`（UUID v4）を保持**する（2026-11-15確定）。更新・削除時はクライアントが保持する版と現行版を照合し、不一致なら**HTTP 409**で保存・削除を拒否する楽観的同時実行制御とする。新規登録時の版はサーバーが発行する。409のエラーコードと基本応答本文は[振替API契約](ledger-transfer-api.md)で確定。競合後の画面操作は[振替WinUI画面](ledger-transfer-ui.md)の2択・読み取り専用契約に従う。
- **`ledger_id`・`transfer_id`・`revision`のDB型はすべてSQLite `TEXT NOT NULL`**とする（2026-11-15確定）。`ledger_id`と`transfer_id`は複合主キーを構成し、`transfer_id`はサーバー発行UUID v7、`revision`はUUID v4。UUIDのDB表記は32文字N形式とする。
- 金額のAPI値域は[振替APIの値・業務制約](specs/api/features/transfers.md#3-振替の値業務制約)に従う。DBの保存型・CHECKは下記に定める。Int64化は将来検討とし、現段階の仕様・実装を変更しない。
- 両支払方法の業務制約は[振替API](specs/api/features/transfers.md#3-振替の値業務制約)、エラー表示・操作は[画面仕様](ledger-transfer-ui.md)に従う。
- **振替元・振替先の物理列名はそれぞれ`from_payment_method_id`、`to_payment_method_id`**とし、**どちらもSQLite `TEXT NOT NULL`**とする（2026-11-15確定）。保存時は両方必須で、未選択（NULL）のまま保存できない。両列は、それぞれ`ledger_id`を含む複合FKで`ledger_payment_methods(ledger_id, payment_method_id)`へ接続する。別家計簿の支払方法は参照不可。どちらかの側からでも振替に参照されている支払方法の物理削除は**`ON DELETE RESTRICT`**で禁止し、振替を連鎖削除しない。人物IDは振替行へ重複保存せず、支払方法の所有人物を参照する。
- **振替元と振替先の同一IDをDBでも禁止**するため、`ledger_transfers`に`CHECK (from_payment_method_id <> to_payment_method_id)`を設ける（2026-11-15確定）。両列の`NOT NULL`と組み合わせ、NULLと同一IDのいずれも保存させない。API側の同一ID検証も維持する。
- 振替名のAPI入力制約は[振替API](specs/api/features/transfers.md#3-振替の値業務制約)に従う。振替名マスタは設けない。DB側はSQLite標準関数による下記の基本CHECKを使用する。
- メモのAPI入力制約・正規化は[振替API](specs/api/features/transfers.md#3-振替の値業務制約)、貼り付け・入力操作は[画面仕様](ledger-transfer-ui.md#入力画面操作確定)を参照する。
- **`ledger_transfers`の保存型**（2026-11-15確定）：`amount`はSQLite `INTEGER NOT NULL`、`name`は`TEXT NOT NULL`、`memo`は`TEXT NULL`。**金額はAPI・DBの両方で正整数Int32範囲を厳密に検証**する。振替名・メモについては**APIで厳密に**`name`の空白のみ禁止・100 UTF-16コード単位以内、`memo`の100 UTF-16コード単位以内・改行禁止・空文字からNULLへの正規化を行う。前後空白・元文字列は保持する。**DBはSQLite標準CHECKによる基本検証に限定し、Unicode全体にわたる厳密な文字数・空白・改行の一致は保証しない**（2026-11-15 B案採用）。独自SQLite関数・全接続への関数登録は導入しない。
- **SQLite標準CHECKによる文字列の基本検証**（2026-11-15確定）：`name`は`CHECK (length(name) BETWEEN 1 AND 100 AND length(trim(name)) > 0)`とし、`memo`は`CHECK (memo IS NULL OR (length(memo) BETWEEN 1 AND 100 AND instr(memo, char(10)) = 0 AND instr(memo, char(13)) = 0))`とする。`name TEXT NOT NULL`、`memo TEXT NULL`を維持。SQLiteの`length(TEXT)`はUnicodeコードポイント単位、`trim()`は既定でASCIIスペースのみ、改行CHECKはCR/LFのみを検査する。したがって絵文字などのUTF-16サロゲートペア、その他Unicode空白・改行はAPIのC#検証で拒否する。**APIを介さない直接SQL書込には文字列仕様に反する値が入り得る**ことを運用上の制約として明記する。文字列検証のための独自DB関数を設けず、DBの制約をAPIと同等と表現しない。
- **既存の空文字データ**：新規書込では`memo:""`を`null`へ統一し、一覧・詳細GETでも過去の空文字は`null`として扱う。ただし既存DB全体を読み取り時に自動更新しない。正式リリース前の既存DBは初期化・確認済みの明示的なデータ整備を別途実施し、保存済み振替ID・監査情報・更新版を黙って書き換えない。旧データが未整備でも読取と無変更比較では空文字を`null`相当として扱う。再送同一性の判定は正規化後の入力で行う。
- **振替金額のDB CHECK**（2026-11-15確定）：`CHECK (typeof(amount) = 'integer' AND amount BETWEEN 1 AND 2147483647)`を`ledger_transfers`に設ける。レシートのInt32型チェックと同じくSQLiteの`typeof()`と範囲判定を併用し、振替では正整数のみ許可する。`NOT NULL`も維持し、API側の同値域検証も併用する。Int64化は[未決定事項](ledger-transfer.md)の将来検討事項のまま。
- 取引日時の画面初期値と補完操作は[振替画面](ledger-transfer-ui.md)を参照する。
- **取引日時`transaction_at`はSQLite `TEXT NOT NULL`、`yyyy-MM-ddTHH:mm:ss`（タイムゾーン・UTCオフセットなし、小数秒なし、秒精度）**とし、入力された日時を時差変換せず保存する（2026-11-15確定）。APIの取引日時表現も同じ固定書式とする。取引日を用いる月次集計・支払方法の有効期間などの判定は、この日時のカレンダー日付を基準とする。**作成・更新の監査日時は取引日時と分離し、UTCで記録**する。
- **監査情報はレシート親と同じ4列を`ledger_transfers`に設ける**（2026-11-15確定）。`created_by_auth_user_id`・`updated_by_auth_user_id`（いずれも`TEXT NOT NULL`、認証アカウントID）、`created_at_utc`・`updated_at_utc`（いずれも`TEXT NOT NULL`、UTC日時）をサーバーで記録する。新規登録時は作成者＝更新者、作成日時＝更新日時。通常の実変更時は作成側2列を保持し、更新者・更新日時を更新する。**両認証アカウントIDはそれぞれ`AspNetUsers(Id)`への`ON DELETE RESTRICT` FK**を設ける。無変更PUTではいずれの監査列も更新しない。
- 無変更判定の外部契約は[振替API](specs/api/features/transfers.md#7-整合性再送)に従う。物理的な更新・成功記録の確定は下記の原子性に従う。

## 振替DB保存の原子性・更新版・索引（2026-11-15確定）

- **原子性**：POST（流用登録を含む）・PUT・DELETEは、振替本体の業務変更・`revision`／監査列の更新・`operation_requests`への成功再送記録を**同じSQLiteトランザクション**で確定する。バリデーション・権限照合・版照合・再送記録の作成を含む処理の途中で失敗した場合は**全体をロールバック**し、振替だけ保存／削除されて再送記録が残らない状態、または再送記録だけ残る状態を作らない。更新・削除の`revision`照合は保存処理と原子的に行い、競合時は変更を確定しない。
- **更新版の発行**：POSTで新しい`revision`（UUID v4）をサーバー発行する。PUTでは[業務値・DB契約](ledger-transfer-db.md)の6業務項目の比較によって**実際に保存値が変わる場合だけ**新しい`revision`を発行し、`updated_by_auth_user_id`・`updated_at_utc`を更新する。無変更PUTは**204 No Content**で振替行へのUPDATE・`revision`再発行・監査列の更新を行わず、成功再送記録だけを同じトランザクション内で保存する。DELETEは既存`revision`一致を条件に対象行を削除し、成功再送記録を同時に確定する。
- **索引の最小構成**：確定済みの`(ledger_id, transaction_at, transfer_id)`一覧キーセット用複合索引を基本とし、主キー・FK・一意制約の実現に必要な構造を除いて**先回りした追加索引は採用しない**。人物・振替元／先の検索、振替名候補・補完等に追加索引が必要かどうかは、実装時の`EXPLAIN QUERY PLAN`と代表的データ量での性能測定を根拠に判断する。必要性が確認されたものだけ、別途設計・検証して追加する。
- POST/PUT/DELETEの実装・検証範囲は[API正本](ledger-transfer-api.md#実装範囲)を参照。**実運用DBでの検証は未実施**。
