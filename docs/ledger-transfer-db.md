# 振替DB・業務値仕様（DB実装済み）

更新: 2026-11-15。振替の**業務値・DB物理制約・トランザクション・更新版**の正本。振替DBの確定範囲は実装済み、振替APIは一覧GET・詳細GETを実装済み。正式リリース前はMigrationを追加せず、`initialize-db`で新規DBの最新スキーマを作る。

参照：[入口・残課題](ledger-transfer.md)／[API契約](ledger-transfer-api.md)／[WinUI画面](ledger-transfer-ui.md)／[既存レシートDB](ledger-receipt-schema.md)／[家計簿DBモデル](ledger-core-schema.md)。

## DB実装の範囲

`Features/Monetail/Transfers/LedgerTransfer`・`LedgerTransferModel`を`ApiDbContext`へ登録し、既存のモデル連動`initialize-db`が空の新規DBに13列・複合PK・5本のRESTRICT FK・下記4件のCHECK・一覧索引`ix_ledger_transfers_transaction`を作成する。既存EF規約のFK用索引を維持し、追加の検索索引・独自トリガー・Migrationは設けない。

通常起動や再初期化で既存DBを変更しない。振替表・索引がない旧構造は既存の構造検証で拒否するため、既存ファイルを保持して別の新規DBを準備する。[初期化手順](42-api-foundation.md#db初期化登録復旧コマンド)に従い、手動削除・自動修復で検査を回避しない。

この工程はEFマッピング・物理制約・新規DB初期化まで。UUID/更新版の発行、監査値の設定、日時/文字列の厳密な入力検証、原子的な版照合、`operation_requests`との同時確定、書込HTTP応答は後続APIで実装する。一覧/詳細GETの読取/JSON整形は[API正本](ledger-transfer-api.md#実装範囲)を参照。DBに追加のUUID/日時CHECKは設けていない。SQLite標準CHECKの文字列検証の限界は下記の確定仕様を維持する。検証範囲と結果は[tests/README.md](../tests/README.md#振替db確定範囲の実装)を参照する。

## 業務と入力値（確定）

- 振替は**同一家計簿内の異なる支払方法間の資金移動**。振替元の残高から金額を減算し、振替先に加算する。収入・支出には計上しない。両側が異なる所有人物でもよい（権限は[振替の権限](ledger-transfer-api.md)）。
- **振替IDはサーバーが発行するUUID v7**とする（2026-11-15確定）。クライアントは振替IDを採番しない。**振替テーブルの主キーは`(ledger_id, transfer_id)`の複合主キー**とし、家計簿ごとに振替を識別する（2026-11-15確定）。**振替ID・更新版などのUUIDは、DBではハイフンなしの32文字N形式、APIではハイフンありの36文字D形式**に統一する（2026-11-15確定）。FK・索引の確定範囲は以下および[残課題](ledger-transfer.md)を参照する。
- **振替テーブル名は`ledger_transfers`とする**（2026-11-15確定）。レシートと同じ家計簿別の業務テーブルとして設計し、未決定の物理列・制約・索引は[入口の残課題](ledger-transfer.md)で管理する。
- **家計簿参照は`FK(ledger_id) → ledgers(id)`とし、`ON DELETE RESTRICT`を指定する**（2026-11-15確定）。振替が存在する家計簿を物理削除できず、振替を連鎖削除しない。振替の複合主キー`(ledger_id, transfer_id)`は維持する。
- **振替一覧のキーセットページング用複合索引**として、`ledger_transfers(ledger_id, transaction_at, transfer_id)`を追加する（2026-11-15確定）。同一家計簿の取引日時＋振替IDを使った安定した走査に用い、レシートと同様にSQLiteの昇順B-tree逆走査を活用可能とする。**一覧順序は`ORDER BY transaction_at DESC, transfer_id DESC`で確定**（2026-11-15）。同日時もID降順で安定化する。カーソルの表現と保護は[振替API契約](ledger-transfer-api.md)で確定済み。振替元・先などの検索用追加索引は**計画・実測で必要性が確認された場合のみ**検討する（[振替API契約](ledger-transfer-api.md)）。
- **振替1件ごとに`revision`（UUID v4）を保持**する（2026-11-15確定）。更新・削除時はクライアントが保持する版と現行版を照合し、不一致なら**HTTP 409**で保存・削除を拒否する楽観的同時実行制御とする。新規登録時の版はサーバーが発行する。409のエラーコードと基本応答本文は[振替API契約](ledger-transfer-api.md)で確定。競合後の画面操作は[振替API契約](ledger-transfer-api.md)の2択・読み取り専用契約に従う。
- **`ledger_id`・`transfer_id`・`revision`のDB型はすべてSQLite `TEXT NOT NULL`**とする（2026-11-15確定）。`ledger_id`と`transfer_id`は複合主キーを構成し、`transfer_id`はサーバー発行UUID v7、`revision`はUUID v4。UUIDのDB表記は32文字N形式、API表記は36文字D形式で統一する。
- **金額**は当面1〜`Int32.MaxValue`（2,147,483,647）の正整数を維持する（2026-11-15再確認）。0円、負数、小数、数式入力は受け付けない。入力中は通常の数値文字列、フォーカスを外すと3桁区切り。不正入力は勝手に0へ補正・切り捨てず、検証エラーとする。APIでも範囲を検証する。**Int64への統一は将来の検討事項であり、現段階で仕様・実装を変更しない**。
- 振替元と振替先は**両方必須、同一支払方法は不可**。同一の場合は選択を保持し、振替先の選択欄の直下に赤いエラー「振替元と振替先は異なる支払方法を選択してください。」を表示して保存不可。修正で解除する。APIでも拒否する。交換（入れ替え）専用ボタンは設けない。
- **振替元・振替先の物理列名はそれぞれ`from_payment_method_id`、`to_payment_method_id`**とし、**どちらもSQLite `TEXT NOT NULL`**とする（2026-11-15確定）。保存時は両方必須で、未選択（NULL）のまま保存できない。両列は、それぞれ`ledger_id`を含む複合FKで`ledger_payment_methods(ledger_id, payment_method_id)`へ接続する。別家計簿の支払方法は参照不可。どちらかの側からでも振替に参照されている支払方法の物理削除は**`ON DELETE RESTRICT`**で禁止し、振替を連鎖削除しない。人物IDは振替行へ重複保存せず、支払方法の所有人物を参照する。
- **振替元と振替先の同一IDをDBでも禁止**するため、`ledger_transfers`に`CHECK (from_payment_method_id <> to_payment_method_id)`を設ける（2026-11-15確定）。両列の`NOT NULL`と組み合わせ、NULLと同一IDのいずれも保存させない。API側の同一ID検証も維持する。
- **振替名**は必須、最大100 UTF-16コード単位（2026-11-15確定）。**空文字・空白文字だけの名前は保存不可**（API・画面で検証）。前後に空白があっても自動でTrim・空白削除・表記正規化せず、入力された元文字列をそのまま保存する。自由入力と履歴候補を使い、振替名マスタは設けない。**Unicode空白だけの判定はAPI側で厳密に行う**。DB側はSQLite標準関数だけの基本的なCHECKとし、具体的な式と限界は下記「SQLite標準CHECKによる文字列の基本検証」に定める。
- **メモ**は任意・1行・最大100 UTF-16コード単位（2026-11-15確定）。**改行文字を含む入力は保存不可**とし、改行を勝手に削除・置換しない。超過はエラーにして自動切り詰めしない。APIでも検証する。**空欄は`null`に統一する**。APIが`memo:""`を受け取った場合は`null`へ正規化し、保存・取得とも空のメモは`null`とする。空白のみ（例：`" "`）は空文字と異なり元の文字列を保持し、Trimしない。レシート親メモの100 UTF-16コード単位も確定済みであり、[レシートDB仕様](ledger-receipt-schema.md)に反映する。レシート側はPR #166〜168で反映済み。振替API側の検証は後続工程。
- **`ledger_transfers`の保存型**（2026-11-15確定）：`amount`はSQLite `INTEGER NOT NULL`、`name`は`TEXT NOT NULL`、`memo`は`TEXT NULL`。**金額はAPI・DBの両方で正整数Int32範囲を厳密に検証**する。振替名・メモについては**APIで厳密に**`name`の空白のみ禁止・100 UTF-16コード単位以内、`memo`の100 UTF-16コード単位以内・改行禁止・空文字からNULLへの正規化を行う。前後空白・元文字列は保持する。**DBはSQLite標準CHECKによる基本検証に限定し、Unicode全体にわたる厳密な文字数・空白・改行の一致は保証しない**（2026-11-15 B案採用）。独自SQLite関数・全接続への関数登録は導入しない。
- **SQLite標準CHECKによる文字列の基本検証**（2026-11-15確定）：`name`は`CHECK (length(name) BETWEEN 1 AND 100 AND length(trim(name)) > 0)`とし、`memo`は`CHECK (memo IS NULL OR (length(memo) BETWEEN 1 AND 100 AND instr(memo, char(10)) = 0 AND instr(memo, char(13)) = 0))`とする。`name TEXT NOT NULL`、`memo TEXT NULL`を維持。SQLiteの`length(TEXT)`はUnicodeコードポイント単位、`trim()`は既定でASCIIスペースのみ、改行CHECKはCR/LFのみを検査する。したがって絵文字などのUTF-16サロゲートペア、その他Unicode空白・改行はAPIのC#検証で拒否する。**APIを介さない直接SQL書込には文字列仕様に反する値が入り得る**ことを運用上の制約として明記する。文字列検証のための独自DB関数を設けず、DBの制約をAPIと同等と表現しない。
- **既存の空文字データ**：新規書込では`memo:""`を`null`へ統一し、一覧・詳細GETでも過去の空文字は`null`として扱う。ただし既存DB全体を読み取り時に自動更新しない。正式リリース前の既存DBは初期化・確認済みの明示的なデータ整備を別途実施し、保存済み振替ID・監査情報・更新版を黙って書き換えない。旧データが未整備でも読取と無変更比較では空文字を`null`相当として扱う。再送同一性の判定は正規化後の入力で行う。
- **振替金額のDB CHECK**（2026-11-15確定）：`CHECK (typeof(amount) = 'integer' AND amount BETWEEN 1 AND 2147483647)`を`ledger_transfers`に設ける。レシートのInt32型チェックと同じくSQLiteの`typeof()`と範囲判定を併用し、振替では正整数のみ許可する。`NOT NULL`も維持し、API側の同値域検証も併用する。Int64化は[未決定事項](ledger-transfer.md)の将来検討事項のまま。
- 取引日時は日付と時刻（秒まで）を入力し、**新規画面の初期値は当日00:00:00**。日付・時刻の省略入力・確定・補完はレシートの共通入力仕様を使う。編集・流用では保存済み日時／画面上の日時を自動で今日に変更しない。
- **取引日時`transaction_at`はSQLite `TEXT NOT NULL`、`yyyy-MM-ddTHH:mm:ss`（タイムゾーン・UTCオフセットなし、小数秒なし、秒精度）**とし、入力された日時を時差変換せず保存する（2026-11-15確定）。APIの取引日時表現も同じ固定書式とする。取引日を用いる月次集計・支払方法の有効期間などの判定は、この日時のカレンダー日付を基準とする。**作成・更新の監査日時は取引日時と分離し、UTCで記録**する。
- **監査情報はレシート親と同じ4列を`ledger_transfers`に設ける**（2026-11-15確定）。`created_by_auth_user_id`・`updated_by_auth_user_id`（いずれも`TEXT NOT NULL`、認証アカウントID）、`created_at_utc`・`updated_at_utc`（いずれも`TEXT NOT NULL`、UTC日時）をサーバーで記録する。新規登録時は作成者＝更新者、作成日時＝更新日時。通常の実変更時は作成側2列を保持し、更新者・更新日時を更新する。**両認証アカウントIDはそれぞれ`AspNetUsers(Id)`への`ON DELETE RESTRICT` FK**を設ける。無変更PUTではいずれの監査列も更新しない。
- **無変更PUT**（2026-11-15確定）：レシートと同様、認証・権限、現行`revision`の一致、全入力値の妥当性を検証してから、**業務入力6項目**（`transactionAt`・`name`・`fromPaymentMethodId`・`toPaymentMethodId`・`amount`・`memo`）を保存済みの値と比較する。日時・ID・金額は確定した保存型に変換した値で比較し、**`name`は元文字列の完全一致、`memo`は空文字→`null`を正規化したうえで完全一致で比較する**（Trim、NFKC、大小文字変換などは行わない）。このためため、`memo:null`と`memo:""`は同値とする。`requestId`・`revision`・監査列は業務値の同値判定から除く。古い版なら内容が同じでも409で拒否し、業務入力6項目が同一なら振替行を`UPDATE`せず**HTTP 204 No Content（本文なし）**を返す。`revision`・作成者／作成日時・最終更新者／最終更新日時はすべて保持する。通常の実変更PUTでは新たな`revision`と更新監査情報を発行する。再送記録は[振替API契約](ledger-transfer-api.md)の共通契約に従う。

## 振替DB保存の原子性・更新版・索引（2026-11-15確定）

- **原子性**：POST（流用登録を含む）・PUT・DELETEは、振替本体の業務変更・`revision`／監査列の更新・`operation_requests`への成功再送記録を**同じSQLiteトランザクション**で確定する。バリデーション・権限照合・版照合・再送記録の作成を含む処理の途中で失敗した場合は**全体をロールバック**し、振替だけ保存／削除されて再送記録が残らない状態、または再送記録だけ残る状態を作らない。更新・削除の`revision`照合は保存処理と原子的に行い、競合時は変更を確定しない。
- **更新版の発行**：POSTで新しい`revision`（UUID v4）をサーバー発行する。PUTでは[業務値・DB契約](ledger-transfer-db.md)の6業務項目の比較によって**実際に保存値が変わる場合だけ**新しい`revision`を発行し、`updated_by_auth_user_id`・`updated_at_utc`を更新する。無変更PUTは**204 No Content**で振替行へのUPDATE・`revision`再発行・監査列の更新を行わず、成功再送記録だけを同じトランザクション内で保存する。DELETEは既存`revision`一致を条件に対象行を削除し、成功再送記録を同時に確定する。
- **索引の最小構成**：確定済みの`(ledger_id, transaction_at, transfer_id)`一覧キーセット用複合索引を基本とし、主キー・FK・一意制約の実現に必要な構造を除いて**先回りした追加索引は採用しない**。人物・振替元／先の検索、振替名候補・補完等に追加索引が必要かどうかは、実装時の`EXPLAIN QUERY PLAN`と代表的データ量での性能測定を根拠に判断する。必要性が確認されたものだけ、別途設計・検証して追加する。
- 本項はDBとAPIの処理契約を確定するものであり、**後続APIの原子性・版照合・再送保証や、実運用DBでの検証が完了したことを意味しない**。
