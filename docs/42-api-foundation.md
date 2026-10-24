# 42 ASP.NET Core APIの土台

更新: 2026-10-24。

## 実装した範囲

同じリポジトリの`src/Monetail.Api/`に、C#／ASP.NET Core／.NET 10 LTSの独立したAPIを追加した。Minimal APIや今後の認証部品・資料を使いやすい構成とする。.NET 10は[公式サポート方針](https://dotnet.microsoft.com/en-us/platform/support/policy)のLTSを採用する。

Windows版は従来のVB／WinForms／.NET Framework 4.8と直接SQLite接続を維持する。APIは既存Windowsプロジェクト・DBを参照せず、起動やテストに実データを必要としない。リポジトリ直下に`global.json`を置かず、既存の.NET 8テスト用CIからAPI用の.NET 10 SDKを分けている。

| 対象 | 内容 |
| --- | --- |
| API | `src/Monetail.Api/Monetail.Api.csproj`、`Program.cs` |
| 稼働確認 | ASP.NET Core標準のHealth Checksを`/health`に公開 |
| 応答 | HTTP 200、`text/plain`、本文`Healthy`、キャッシュ抑止の`no-store` |
| API統合テスト | `tests/Monetail.Api.Tests/`。本番モードで応答を確認 |
| コンテナ | ルートの`Dockerfile`でRelease発行し、ASP.NETランタイム上の非rootユーザーで8080番を待受け |
| 起動設定 | ルートの`compose.yaml`から`127.0.0.1:8080`へ公開 |
| CI | 独立した`api-foundation`ジョブで統合テスト、Compose設定、Dockerビルド、HTTP応答・非root実行を確認 |

`/health`はプロセスがHTTPに応答できることを確認する。DB等の依存先は登録していないため、DB接続や業務機能が正常であることを示すものではない。

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

既定の公開先は同じ端末からの確認または将来のリバースプロキシ向けであり、この設定では他端末からLAN経由で接続できない。LAN公開・HTTPS終端・ログインは後続で設計する。

CIと同じコンテナ起動確認は次のとおり。スクリプトはループバックの空きポートを使い、終了時に確認用コンテナを削除する。

```sh
docker build --tag monetail-api:ci .
bash tests/Monetail.Api.Tests/docker-smoke.sh monetail-api:ci
```

## 次に設計するもの

パスキー中心のログイン、管理コマンドによる初回登録・紛失復旧、既存`m_user`との対応は[43 認証設計](43-passkey-authentication-design.md)にまとめる。認証は未実装で、HTTPSの具体的な配置、SQLiteの接続・追加移行とバックアップも後続で実装・検証する。XMLの利用者コード設定は採用しない。業務APIはレシート入力、次に振替入力を優先し、保存済み`total`等の既存契約を保つ。ログイン・DB接続・業務API・実環境への配置は今回の土台の実装に含まない。

検証の実行結果・件数・CIは[tests/README.md](../tests/README.md)を参照する。APIの起動確認だけでWindows版全体や実データ・実運用環境を検証済みとは扱わない。
