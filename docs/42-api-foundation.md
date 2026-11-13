# 42 APIの起動・設定・運用

起動・配備設定・管理コマンドの正本。認証仕様は[43](43-passkey-authentication-design.md)、家計簿のAPIと後続設計は[58](58-ledger-foundation-design.md)、検証結果は[tests/README.md](../tests/README.md)を参照する。

## 実装した範囲

同じリポジトリの`src/Monetail.Api/`に、C#／ASP.NET Core／.NET 10 LTSの独立したAPIを追加した。Minimal APIや今後の認証部品・資料を使いやすい構成とする。.NET 10は[公式サポート方針](https://dotnet.microsoft.com/en-us/platform/support/policy)のLTSを採用する。

Windows版は従来のVB／WinForms／.NET Framework 4.8と直接SQLite接続を維持する。APIは既存Windowsプロジェクトを参照せず、起動やテストに実データを必要としない。新規API用SQLiteの初期設定を明示コマンドで行い、通常起動では接続や移行を行わない。リポジトリ直下に`global.json`を置かず、既存の.NET 8テスト用CIからAPI用の.NET 10 SDKを分けている。

| 対象 | 内容 |
| --- | --- |
| API | `src/Monetail.Api/Monetail.Api.csproj`、`Program.cs` |
| 稼働確認 | ASP.NET Core標準のHealth Checksを`/health`に公開 |
| 応答 | HTTP 200、`text/plain`、本文`Healthy`、キャッシュ抑止の`no-store` |
| API統合テスト | `tests/Monetail.Api.Tests/`。本番モードで応答を確認 |
| コンテナ | ルートの`Dockerfile`でRelease発行し、ASP.NETランタイム上の非rootユーザーで8080番を待受け |
| 起動設定 | ルートの`compose.yaml`から`127.0.0.1:8080`へ公開 |
| CI | 独立した`api-foundation`ジョブで統合テスト、Compose設定、Dockerビルド、HTTP応答・非root実行を確認 |

`/health`はプロセスがHTTPに応答できることを確認する。DBのヘルスチェックは登録していないため、DB接続や業務機能が正常であることを示すものではない。

## ローカル起動

.NET 10 SDKを用意し、下記の[必須設定](#認証の必須設定)を外部設定で指定してから、リポジトリ直下で実行する。

```sh
dotnet run --project src/Monetail.Api --urls http://127.0.0.1:5080
```

別のターミナルで確認する。本文は`Healthy`になる。終了は起動したターミナルでCtrl+C。

```sh
curl http://127.0.0.1:5080/health
```

APIテストだけを実行する場合:

```sh
dotnet test tests/Monetail.Api.Tests --configuration Release
```

## Dockerで起動

配備環境に依存しない通信・認証・永続化・更新の条件は[共通配備要件](deployment.md)を参照する。具体的なGHCRイメージ配備は[Docker構成例](deployment-examples/docker-tailscale.md)に分離した。以下のルートComposeは開発用build構成。

DockerとComposeを用意する。既定の`compose.yaml`には認証設定や永続化領域を含めないため、そのままでは起動しない。環境ごとのCompose overrideで下記の[必須設定](#認証の必須設定)を環境変数へ渡し、DBの親フォルダ・Data Protectionキー・OAuth証明書をマウントしてから実行する。証明書は読取専用にする。ビルドコンテキストはリポジトリ直下だが、`.dockerignore`でAPIソースを中心に送信対象を限定し、`.env`やSQLite等を除外する。

```sh
docker compose up --build -d
curl http://127.0.0.1:8080/health
docker compose down
```

既定の公開先は同じ端末からの確認または将来のリバースプロキシ向けであり、この設定では他端末からLAN経由で接続できない。登録時の固定HTTPS・既知プロキシ設定は[認証契約](43-passkey-authentication-design.md)を参照する。ログインは[認証契約](43-passkey-authentication-design.md)まで実装し、実環境のLAN公開・HTTPS終端は未実施。

CIでは変更対象に応じてAPIテスト・Compose設定・Dockerビルドと起動を選択する。APIテストだけの変更ではDockerをビルドせず、Markdownだけの変更ではこれらを省略する。選択規則は[自動テスト](../tests/README.md)を参照する。

CIと同じコンテナ起動確認は次のとおり。スクリプトはループバックの空きポートを使い、終了時に確認用コンテナを削除する。

```sh
docker build --tag monetail-api:ci .
bash tests/Monetail.Api.Tests/docker-smoke.sh monetail-api:ci
```

## 認証の必須設定

通常のAPI起動ではCookie認証とWindows版向けOAuthを常に公開する。`ConnectionStrings:Monetail`・次の`Passkeys`設定・下記の`DesktopOAuth`証明書設定が必須で、不足・不正なら起動を拒否する。管理コマンドはWeb認証設定・OAuth証明書なしで実行できる。環境変数では区切りを`__`にする。実接続先・秘密・実データはコミットしない。

| 設定 | 条件 |
| --- | --- |
| `Passkeys:Origin` | 固定HTTPSオリジン。パス・クエリ・資格情報なし |
| `Passkeys:RpId` | Originと同じDNSホスト名 |
| `Passkeys:DataProtectionKeysPath` | 永続化するキー保存先の絶対パス |
| `Passkeys:TrustedProxies` | 信頼するプロキシIPの配列。未指定なら転送ヘッダーを信用しない |
| `Registration:Mode` | 任意。`InvitationOnly`（既定）または`Public`。空文字・未知の値は通常API起動を拒否。`Public`は匿名自己登録APIを有効にする |
| `ConnectionStrings:Monetail` | 永続SQLiteファイル。通常接続はReadWrite・FK有効、新規作成はinitialize-dbだけ |
| `TZ` | Dockerの既定はEtc/UTC。Composeで変更し、.NETが解決した地域を起動ログで確認 |

Docker Composeの`Registration__Mode: "InvitationOnly"`は維持する。`Public`へ変更してAPIコンテナを再作成すると、匿名自己登録APIが直ちに有効になる。停止時は`InvitationOnly`へ戻して再作成し、未完了チャレンジを破棄する。設定を変更できる権限は運営者に限定する。登録画面は実装済み。CAPTCHA・退会・全パスキー喪失時の自己復旧は未実装であり、不特定多数への本番一般公開を開始しない。設定と単一APIプロセスの運用条件は[認証・パスキー](auth-passkeys.md#登録モードの設定dec-111設定読込と検証は実装)を参照する。

固定HTTPS・証明書・HTTPS終端とサーバー上の実配置は運用で用意する。既定のComposeはループバック公開用で、認証設定・DBマウント・HTTPSを自動準備しない。DBの親フォルダとData Protectionキーを永続化する。

### Windows版向けOAuthの有効化

`Passkeys`の固定HTTPS設定に加え、通常起動では次のファイルを必ず準備する。OAuthを無効にする設定は設けない。

| 設定 | 条件 |
| --- | --- |
| `DesktopOAuth:SigningCertificatePath` | 秘密鍵付きRSA署名証明書のPFX。絶対パス |
| `DesktopOAuth:EncryptionCertificatePath` | 別の秘密鍵付きRSA暗号化証明書のPFX。絶対パス |
| `DesktopOAuth:SigningCertificatePassword` | 署名PFXをパスワード保護した場合の読込用秘密値 |
| `DesktopOAuth:EncryptionCertificatePassword` | 暗号化PFXをパスワード保護した場合の読込用秘密値 |

証明書はRSA 2048ビット以上、署名と暗号化で別々の鍵とする。HTTPS用証明書とは別に用意し、OpenIddict公式の[証明書生成例](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html#registering-a-certificate-recommended-for-production)を参考にできる。自己署名でもよく、認証サーバー用の鍵として外部の永続化領域へ保存する。非rootアプリだけが読めるファイル権限にし、秘密鍵・パスワードをGitや通常ログへ残さない。証明書が不備なら起動時に拒否する。

DB・Data Protectionキーと併せて、この2つのPFXもバックアップ・再起動後の復元対象とする。単一の署名／暗号化鍵を別の鍵へ置き換える場合、以前のWindows用トークンを検証できなくなるため再取得が必要。HTTPの期限・失効・コールバック等の契約は[43](auth-windows-oauth.md#windows版のoauth認証dec-051)を参照する。新版Windowsの接続・認証基盤は[04](architecture-winui.md#新版windows版の構成)の独立プロジェクトで扱い、既存Windows版のSQLite接続は維持する。

<a id="db移行登録復旧コマンド"></a>

## DB初期化・登録・復旧コマンド

**正式リリース前のAPI用DBは現在のEFモデルから新規作成する（DEC-141）。** 正式リリースまではEF Core Migration・Designer・Snapshot・適用履歴を使用せず、`initialize-db`で**空の新規DBにその時点の最新スキーマを一括生成**する。既存API用DBは通常起動や初期化コマンドで追加移行・自動変換しない。**正式リリース後はEF Core Migrationを導入する方針**とし、初回導入・バックアップ・アップグレード・ロールバック手順はリリース準備時に別途設計する。旧Windows版DBとその業務データの移行SQLは別工程。

新規導入は空または未存在のSQLiteファイルを指定する。親ディレクトリを事前に用意し、APIを停止して実行する。**既存形式・旧API用・別用途・不正構造のDBは拒否し、初期化・修復・削除しない。** 旧DBとバックアップを保持し、別の新規ファイル/保存先で準備する。履歴表や列を手で消して検査を回避しない。今回は旧API用DBのデータ移行を提供しない。旧Windows版の将来の引継ぎ方針は[58](ledger-core-transition.md#新規利用と既存dbの境界)を参照する。

```sh
dotnet run --project src/Monetail.Api -- initialize-db
dotnet run --project src/Monetail.Api -- initialize-admin
dotnet run --project src/Monetail.Api -- issue-enrollment-invitation --account-id ACCOUNT_ID
# 全失効・復元リセット後の対象に、緊急復旧リンクを明示発行する。
dotnet run --project src/Monetail.Api -- issue-recovery-link --account-id ACCOUNT_ID
dotnet run --project src/Monetail.Api -- revoke-account --account-id ACCOUNT_ID
# 最後の管理者も全失効させる必要がある紛失復旧時だけ明示する。
dotnet run --project src/Monetail.Api -- revoke-account --account-id ACCOUNT_ID --emergency-recovery
# バックアップ復元後、サービスを停止した状態で実行する。
dotnet run --project src/Monetail.Api -- reset-authentication --after-restore
# サービス停止・バックアップ後に、全履歴JSONとの差分を確認して明示適用する。
dotnet run --project src/Monetail.Api -- sync-tax-rates --file /path/to/tax-history.json --dry-run
dotnet run --project src/Monetail.Api -- sync-tax-rates --file /path/to/tax-history.json --apply
```

- `initialize-db`は空のDBにだけ、`ApiDbContext`の現行モデルから表・NOT NULL/CHECK/FK/UNIQUE・索引・モデルの初期値を生成する。モデルだけでは作られない支払方法所有者変更禁止・税率期間重複禁止の3トリガーは`DatabaseSchema`で明示的に作成する。全DDL・初期値・トリガーは1つのSQLiteトランザクションで確定する。
- 初期値は全家計簿共通の税区分4件とWindows OAuth公開クライアント。税率履歴・管理者・登録リンク・家計簿・人物・科目は自動生成しない。パスキーを含む実行時のIdentity設定を使ったEFモデルを構造照合にも使用する。
- 既存ファイルは書込前にReadOnlyで、現行モデル/追加トリガーのDDL・初期マスタ・FK整合性を照合する。正しければ保存値・更新版・認証情報を変更せず終了。不足/余分な表・列・索引・トリガー、旧履歴表や不整合は明確なエラーで拒否する。
- 初期マスタは既存モデルの`HasData`を期待値とし、税区分4件のコード/正式名称、Windows OAuth公開クライアントのClientId・種別・ConsentType・秘密/クライアント鍵/個別Settingsの不在・リダイレクト先・権限・PKCE要件を照合する。JSON配列は順序/重複を無視した集合の完全一致、nullの任意配列は空集合として扱う。ConcurrencyToken・表示名/翻訳・運用Propertiesや認証状態は固定比較しない。通常APIの準備検証でも同じ照合を行い、不一致は書込・正規化・修復せず拒否する。
- 同時実行は直列化し、SQLiteの書込トランザクション内でも空/正規構造を再確認する。作成途中の失敗は全体をロールバックし、空DBなら再実行できる。残った旧管理表・不明な構造を削除して再開する動作はない。実ファイルや親ディレクトリを自動削除・置換しない。
- 初期化はDB横の空の`.initialize.lock`ファイルを排他オープンして直列化し、取得後にDBを検査する。ファイルは残るがプロセス終了でOSが排他を解放する。実行中に削除・置換しない。DBと親ディレクトリへの書込権限が必要で、親ディレクトリや既存ファイルを自動削除・置換しない。
- `initialize-admin`は人物指定なしで初期管理者を作り、30分のリンクを発行する。管理者が既にいれば変更せず、標準エラーに既存IDと案内だけを出し、URLを再表示しない。
- `issue-enrollment-invitation --account-id ID`は無効・鍵なし・未登録の対象だけ。一般は7日、未登録の初期管理者は30分。登録済み停止者へのWeb/API再発行は拒否する。
- `issue-recovery-link --account-id ID`は、運営者が全失効または復元後リセット済みの登録済み・無効・鍵なしの対象に明示する緊急復旧。未登録者への発行は拒否し、`issue-enrollment-invitation`を使用する。**既存の10分間を暫定維持**し、認証ID・表示名・ロールを変えず、パスキー再登録と明示ログインを要求する。新アカウントを作成しない。本人向けの自己復旧・一般公開の本人確認条件は未実装。
- URL発行コマンドは`Passkeys:Origin`だけを必須のWeb設定とし、正しい固定HTTPS Originがなければ状態を変更せず失敗する。RpId・Data Protection・OAuth証明書等の通常API設定はCLI発行には要求しない。DB初期化・全失効・復元リセットにはOriginを要求しない。
- 発行成功時の標準出力は`https://固定ホスト/auth/enroll#token=...`の1行だけ。診断・監査は標準エラーへ出し、URL・生トークンをログへ含めない。URL出力は管理者だけの一時端末で扱い、共有ログ・teeへ流さない。旧`issue-enrollment-code`は受け付けない。
- `sync-tax-rates`（D4-CLI実装済み）は既存DBだけを対象とする。JSON配置・API/Docker起動・初期化で自動投入しない。削除を含む適用には`--apply --allow-delete`が必要。引数は`--file`と操作フラグだけを受け付け、接続先は既存の設定ファイル/環境変数から取得する。Origin/OAuth証明書等のWeb設定を要求しない。契約と安全な書込順序は[共通税率履歴](ledger-core-schema.md#税率履歴の全履歴同期clid4-cli実装済み)、運用は[配備例](deployment-examples/docker-tailscale.md#税率履歴の手動同期d4-cli実装済み)を参照。
- 管理コマンドはWeb待受けを行わず、成功0・失敗1。30秒でキャンセルを要求する。通常起動はDBを作成・変更せず、認証APIと管理操作では必要な現行構造・初期マスタを検証して未準備のDBを拒否する。更新・復元で`initialize-db`を追加移行として使わない。


バックアップは停止・全接続終了後のコピー、またはSQLiteのBackup APIで取得する。稼働中のDB本体だけをコピーしない。復元後の全体リセットは手動で行い、まず管理者、次に他利用者へ`issue-recovery-link`で復旧リンクを発行する。バックアップ取得だけではリセットしない。同じRP ID・DB・Data Protectionキーを保つ通常移設は再登録不要。RP ID変更時は再登録を計画する。DB自体の暗号化は今回対象外。
