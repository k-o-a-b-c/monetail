# 42 APIの起動・設定・運用

起動・配備設定・管理コマンドの正本。認証仕様は[43](43-passkey-authentication-design.md)、未実装の導入・家計簿設計は[58](58-ledger-foundation-design.md)、検証結果は[tests/README.md](../tests/README.md)を参照する。

## 実装した範囲

同じリポジトリの`src/Monetail.Api/`に、C#／ASP.NET Core／.NET 10 LTSの独立したAPIを追加した。Minimal APIや今後の認証部品・資料を使いやすい構成とする。.NET 10は[公式サポート方針](https://dotnet.microsoft.com/en-us/platform/support/policy)のLTSを採用する。

Windows版は従来のVB／WinForms／.NET Framework 4.8と直接SQLite接続を維持する。APIは既存Windowsプロジェクトを参照せず、起動やテストに実データを必要としない。後続ので既存SQLiteへの接続・追加移行を実装したが、通常起動で接続や移行は行わない。リポジトリ直下に`global.json`を置かず、既存の.NET 8テスト用CIからAPI用の.NET 10 SDKを分けている。

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

.NET 10 SDKを用意し、リポジトリ直下で実行する。

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

DockerとComposeを用意し、リポジトリ直下で実行する。ビルドコンテキストはリポジトリ直下だが、`.dockerignore`でAPIソースを中心に送信対象を限定し、`.env`やSQLite等を除外する。DBファイルのマウントは行わない。

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

## 認証を有効にする設定

既定はDB不要の`/health`だけ。認証を公開する場合は`ConnectionStrings:Monetail`と次の`Passkeys`設定を外部設定で指定する。環境変数では区切りを`__`にする。実接続先・秘密・実データはコミットしない。

| 設定 | 条件 |
| --- | --- |
| `Passkeys:Origin` | 固定HTTPSオリジン。パス・クエリ・資格情報なし |
| `Passkeys:RpId` | Originと同じDNSホスト名 |
| `Passkeys:DataProtectionKeysPath` | 永続化するキー保存先の絶対パス |
| `Passkeys:TrustedProxies` | 信頼するプロキシIPの配列。未指定なら転送ヘッダーを信用しない |
| `ConnectionStrings:Monetail` | 現在は既存SQLiteファイル限定。認証接続はReadWrite・FK有効 |
| `TZ` | Dockerの既定はEtc/UTC。Composeで変更し、.NETが解決した地域を起動ログで確認 |

固定HTTPS・証明書・HTTPS終端とサーバー上の実配置は運用で用意する。既定のComposeはループバック公開の稼働確認用で、DBマウントやHTTPSを自動準備しない。DBの親フォルダとData Protectionキーを永続化する。

## DB移行・登録・復旧コマンド

現在のコマンドは既存業務DBの隔離コピー向け。空DBからの新規導入は未実装。通常起動・HTTP要求でDB作成や移行をしない。バックアップを確保し、`ConnectionStrings__Monetail`を指定して実行する。

```sh
dotnet run --project src/Monetail.Api -- migrate-auth-db
dotnet run --project src/Monetail.Api -- initialize-admin
dotnet run --project src/Monetail.Api -- issue-enrollment-code --account-id ACCOUNT_ID
dotnet run --project src/Monetail.Api -- revoke-account --account-id ACCOUNT_ID
# 最後の管理者も全失効させる必要がある紛失復旧時だけ明示する。
dotnet run --project src/Monetail.Api -- revoke-account --account-id ACCOUNT_ID --emergency-recovery
# バックアップ復元後、サービスを停止した状態で実行する。
dotnet run --project src/Monetail.Api -- reset-authentication --after-restore
```

- `migrate-auth-db`は既存m_user・t_receiptsの列と主キーを確認し、認証移行を明示適用する。不在・空・別用途DBは拒否。業務値・保存済みtotal・過去の更新者を補完・再計算しない。
- 初期管理者は人物指定なしで新規作成する。既に管理者がいれば変更せず、生コードも再表示しない。再発行は既存の無効・鍵なしアカウントへ行う。旧`--person-code`や操作オプションの混在・重複は拒否する。
- 発行成功時だけ、アカウントID・生コード・UTC期限を標準出力へ出す。コードは10分・1回限りで、通常ログへ残さない。全失効ではコードを自動発行しない。本人が登録画面でパスキー登録後、明示ログインする。
- 管理コマンドはWeb待受けを行わず、成功0・失敗1。30秒でキャンセルを要求する。移行ロックが異常終了後に残った場合は、サービス停止・バックアップ後に状態を調べ、推測でロックを削除しない。

バックアップは停止・全接続終了後のコピー、またはSQLiteのBackup APIで取得する。稼働中のDB本体だけをコピーしない。復元後の全体リセットは手動で行い、まず管理者、次に他利用者へ再登録コードを発行する。バックアップ取得だけではリセットしない。同じRP ID・DB・Data Protectionキーを保つ通常移設は再登録不要。RP ID変更時は再登録を計画する。DB自体の暗号化は今回対象外。
