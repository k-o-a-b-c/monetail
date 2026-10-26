# 44 認証DBの土台

更新: 2026-10-24。**認証テーブルの追加移行まで実装。Web管理画面・業務APIは未実装で、本番DBへの適用は行っていない。** [43 認証設計](43-passkey-authentication-design.md)の保存基盤に当たる。

導入設計は[56](56-new-user-onboarding-policy.md)の新規利用を基本とする方針へ進める。本書は現在の既存DBへの認証テーブル追加を記録したもので、空DBを作成する新規導入機能ではない。新規利用では明示的な初期設定コマンドでDB準備と最初の管理者用コード発行をまとめる方針が確定した（DEC-039）。一度きりのデータ移行と、今後も必要なDBスキーマ更新は区別する。新規初期化は未実装で、現時点の空DB拒否・明示的移行の動作は変更していない。

## 保存構成

`src/Monetail.Api/Authentication/`で、ASP.NET Core IdentityとEF Core SQLiteを共通のDI登録にまとめる。実行時・移行生成時・テストで`IdentitySchemaVersions.Version3`を使い、標準のパスキーストアも含める。接続は既存ファイル限定の`ReadWrite`とし、認証用接続で外部キーを有効にする。

| 対象 | 実装した内容 |
| --- | --- |
| Identity標準テーブル | `AspNetUsers`、`AspNetRoles`、各Claim・Login・Role・Tokenと`AspNetUserPasskeys`を追加する |
| アカウントの利用可否 | `AspNetUsers.IsEnabled`の初期値はfalse。移行だけではログイン可能なアカウントを作らない |
| `auth_user_links` | 認証IDをNOT NULLの主キー、人物コードをNOT NULLかつUNIQUEにする。Identityアカウントと`m_user(code)`へのFKは双方RESTRICT |
| `auth_account_creation_requests` | [54](54-admin-account-creation.md)の追加移行で発行要求ID・実行者・作成アカウント・人物コード・日時を保存。再送でコードを再表示せず、既存の認証・業務行を保持する |
| 既存`m_user` | Codeだけを参照先としてモデルに置き、`ExcludeFromMigrations()`で移行対象から除外する。表示名等による自動紐付けは行わない |
| EFの移行管理 | `__EFMigrationsHistory`と`__EFMigrationsLock`が追加される。既存の`PRAGMA user_version`とは分ける |

生成した初回移行は認証テーブル・索引の追加だけを行う。既存業務テーブルを再作成・変更せず、人物・取引・対象者・過去の`update_user`・保存済み`receipt.total`を更新しない。`total`のNULLも再計算・補完しない。適用済み移行の再実行では既存の認証情報も維持する。

## 隔離したコピーでの適用

この段階では開発・検証用のコピーだけを対象にする。コピーはサービス停止・全接続終了後、またはSQLiteのBackup APIで取得し、稼働中のDB本体だけをコピーしない。認証テーブルを加えたDBを既存Windows版と本番で直接共有する切替は未実施。

接続文字列は`ConnectionStrings:Monetail`から取得する。環境変数なら`ConnectionStrings__Monetail`を使い、既に存在する検証用SQLiteファイルを指定する。実際のパスや接続文字列はリポジトリへ記録しない。

```sh
dotnet run --project src/Monetail.Api -- migrate-auth-db
```

コマンドは接続後に既存の`m_user`の必要列と単独主キー、`t_receipts`の`seq`・`total`と単独主キーを確認してから移行する。不在ファイルを新規作成せず、空DBや確認条件を満たさないDBには認証テーブルを追加しない。接続未設定・確認失敗・移行失敗では終了コード1を返す。コマンドは30秒で移行処理のキャンセルを要求する。

通常のAPI起動では接続も移行も行わない。接続未設定でも従来どおり`/health`を利用でき、接続設定があっても自動移行しない。`/health`はDBの正常性を確認するエンドポイントではない。

EFの移行ロックは異常終了後に残る場合がある。待機失敗時はサービスを停止し、バックアップを確保した上で適用状況を調べる。ロック行やテーブルを推測で削除して再実行しない。

## 検証と後続

実際のSQLiteと合成データで、既存行・型・旧テーブル定義・保存済みtotal・PRAGMAの保持、標準Identityストア、人物対応の制約、再実行と途中失敗を検証する。検証範囲・件数・最新結果は[tests/README.md](../tests/README.md)を参照する。実データや本番環境での適用確認を意味しない。

初期管理者と登録コードの発行・登録コード用の表は[45](45-initial-admin-enrollment.md)で追加した。パスキー登録は[46](46-passkey-enrollment.md)まで実装した。ログインとセッションの表は[47](47-passkey-login-sessions.md)まで実装した。人物追加、Windows版のAPI切替、永続フォルダの実配置は後続とする。このDB基盤だけではパスワード登録等のHTTP経路も公開しない。
