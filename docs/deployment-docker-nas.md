# Docker対応NASでAPIを運用する

Docker EngineとDocker Composeが使えるLinux系NAS向けの手順。メーカー・型番・管理画面には依存しない。API・認証の契約は変更せず、[42 起動・運用](42-api-foundation.md)の設定と管理コマンドをコンテナで実行する。旧Windows版のDBをそのまま使う手順ではない。

## 構成と前提

```text
Windows／将来のスマートフォン（Tailscale接続＋monetailのパスキー認証）
  → tailnet内のHTTPS :443
  → Tailscale Serve（HTTPS証明書を取得・更新）
  → http://127.0.0.1:8080 → API → SQLite
     └ TailscaleコンテナとAPIコンテナはネットワーク名前空間を共有
```

標準は[追加Compose](../deploy/nas/compose.nas.yaml)と既存の[compose.yaml](../compose.yaml)を重ねる方式。Tailscaleはuserspaceモードで動かし、TUNデバイス・privileged・NET_ADMINは不要。NASの8080番は既存設定どおり**127.0.0.1だけ**で待ち受ける。443番をNASのLANへpublishする必要はない。Funnel、ルーターのポート転送、インターネットへの直接公開は行わない。

必要なもの:

- Docker Compose **2.24.4以降**（検証スクリプトの`!override`にも対応）、Bash、OpenSSL、Git、curl、sudo等によるファイル所有権の変更手段。
- NASのCPUに対応する.NET 10 SDK／ASP.NETランタイムとTailscaleの公式イメージ。両方のイメージに実機のアーキテクチャ用manifestがあることを事前確認する。
- NASのローカル永続領域。SQLiteのロックが保証されないSMB/NFS共有をDB保存先にしない。
- Tailscaleのアカウントと接続済みクライアント。MagicDNS・HTTPSを有効にし、端末承認とtailnetポリシーで利用者からこのノードのTCP 443への接続を許可する。初期の全許可ポリシーをそのまま安全なアクセス制限とはみなさない。

Tailscaleの参加権限とmonetailのアカウント権限は別。tailnet内でもアプリの認証が必要。HTTPSの公開証明書にはノードのDNS名が含まれ、Certificate Transparencyログにも記録されるため、機密性のある名前は使わない。

Docker Engineはセキュリティ更新済みのものを使う。28.0.0未満には、localhostへpublishしたポートでも同じL2ネットワークから到達できる問題がある。該当するNASではEngineの更新、またはNAS側のファイアウォールによる8080番の遮断を行い、LANから到達しないことを確認してから運用する（[Docker公式の注意](https://docs.docker.com/engine/network/port-publishing/#publishing-to-the-outside-world)）。

## 1. 配置と設定

以下のパス・ホスト名・tailnet名は**全て置換用の例**。実値や秘密ファイルはGitチェックアウトの外に置く。コマンドはNAS上のBashで実行する。実際の`.env`を作成・編集せず、専用の`nas.env`を使う。

```bash
git clone https://github.com/k-o-a-b-c/monetail.git
cd monetail
# 運用するコミット／リリースを確認してから選ぶ。
NAS_DATA=/srv/monetail-data
NAS_ENV="$NAS_DATA/nas.env"
sudo install -d -m 711 "$NAS_DATA"
sudo chown "$(id -u):$(id -g)" "$NAS_DATA"
umask 077
mkdir -m 700 "$NAS_DATA/db" "$NAS_DATA/keys" "$NAS_DATA/tailscale"
mkdir -m 700 "$NAS_DATA/tailscale/state" "$NAS_DATA/tailscale/config"
cp deploy/nas/nas.env.example "$NAS_ENV"
cp deploy/nas/serve.json "$NAS_DATA/tailscale/config/serve.json"
chmod 600 "$NAS_ENV"
```

`nas.env`をエディターで変更する。ここに認証キー・パスワードは書かない。

| 項目 | ダミー例と置換内容 |
| --- | --- |
| `MONETAIL_DATA_DIR` | `/srv/monetail-data`。上の`NAS_DATA`と同じ絶対パス。空白やComposeの展開文字を含まないパスを使う |
| `TAILSCALE_HOSTNAME` | `monetail-api`。tailnet内で重複しないノード名 |
| `MONETAIL_ORIGIN` | `https://monetail-api.tail-example.ts.net`。実際に割り当てられたノードの完全なHTTPS Origin。パス・末尾の`/`・クエリーを付けない |
| `MONETAIL_RP_ID` | `monetail-api.tail-example.ts.net`。Originと同じホスト名のみ。スキーム・ポート・パスは付けない |
| `TAILSCALE_IMAGE` | 例は`tailscale/tailscale:v1.104.4`。公式の安定版を確認してバージョンを固定する。必要ならdigestも固定する |
| `TZ` | 例は`Etc/UTC`。日付を扱う運用地域に合わせて、例として`Asia/Tokyo`等のIANAタイムゾーンを指定 |
| `MONETAIL_TRUSTED_PROXY` | 標準構成は`127.0.0.1`のまま。後述のNAS本体でTailscaleを実行する場合だけ変更 |

`serve.json`中の`${TS_CERT_DOMAIN}`は**置換せず残す**。Composeの環境変数ではなく、Tailscaleコンテナがノードの証明書ドメインに置き換える。設定はファイル単体でなくディレクトリごと読取専用マウントする。

以降、リポジトリ直下で次の関数を使う。新しいシェルでは変数と関数を再定義する。

```bash
dc() {
  docker compose --project-name monetail --env-file "$NAS_ENV" \
    -f compose.yaml -f deploy/nas/compose.nas.yaml "$@"
}
dc config --quiet
dc build api
```

`config --quiet`は値を表示しない構文検証。実ファイルの権限や接続の成功まで保証しない。通常のDocker権限エラーはNAS側のDocker操作権限を整える。Dockerソケットを誰でも操作できる権限にはしない。

## 2. OAuth証明書と秘密ファイル

HTTPS用とは**別**の、署名用・暗号化用の自己署名RSA PFXを作る。スクリプトは異なる鍵、個別の乱数パスワード、読込用JSONを生成し、秘密値は表示しない。

```bash
# 初回だけ。第2引数は証明書の有効日数の例。期限を運用記録へ残す。
bash deploy/nas/create-oauth-certificates.sh "$NAS_DATA/oauth" 365
api_uid="$(docker run --rm --entrypoint id monetail-api:local -u)"
api_gid="$(docker run --rm --entrypoint id monetail-api:local -g)"
sudo chown -R "$api_uid:$api_gid" "$NAS_DATA/db" "$NAS_DATA/keys" "$NAS_DATA/oauth"
```

`oauth`が既にあれば、空ディレクトリやシンボリックリンクも含めて生成を拒否する。再実行で上書きしない。途中失敗でも既存の出力先を自動削除しない。**初回作業で未使用だと確認できた不完全なディレクトリ**だけを別名へ退避してから再試行する。更新時には実行しない。

生成物は`signing.pfx`・`encryption.pfx`・`appsettings.Production.json`。最後のJSONには両PFXのパスワードが入る。Compose secretsから`/app/appsettings.Production.json`へ読取専用で渡し、ASP.NET Coreの標準設定読込を使う。パスワードは環境変数・コマンド引数に渡さない。生成中の一時PEM鍵・パスワードファイルは終了時に削除する。

Composeのファイルベースsecretsはホスト上の暗号化保管機能ではない。bind mountであり、`uid/gid/mode`指定は適用されないため、上のように**実イメージの非root UID/GIDとホスト上の権限**を合わせる。NASのACL・rootless Docker・user namespace remapを使う環境では、そのマッピングに合わせて所有権を調整する。`chmod 777`で回避しない。

次に、永続ノード用の**非ephemeral・原則1回限り**のTailscale認証キーを用意する。必要な承認・タグ・アクセス制限を公式手順で設定し、以下で非表示入力する。`set -x`、端末録画、入力のログ収集は無効にする。

```bash
umask 077
read -r -s -p 'Tailscale auth key: ' nas_authkey
printf '\n'
printf '%s' "$nas_authkey" > "$NAS_DATA/tailscale/authkey"
unset nas_authkey
chmod 600 "$NAS_DATA/tailscale/authkey"
```

Tailscaleには`TS_AUTHKEY=file:/run/secrets/tailscale-authkey`とパスだけを渡す。既存の認証キーファイルをこの入力手順で上書きしない。再認証が必要な場合は停止して旧ファイルを退避し、新しいキーで入れ替える。

## 3. DB初期化・起動・管理者登録

APIを停止した状態で**初回のみ**実行する。DBの通常起動はスキーマを自動作成しない。

```bash
dc run --rm --no-deps api initialize-db
dc up -d api tailscale
dc exec tailscale tailscale status --peers=false
dc exec tailscale tailscale serve status
```

Tailscaleの端末承認が必要なら承認し、実際のHTTPS URLを確認する。同名ノードとの衝突等で想定と違えば`nas.env`のOrigin/RpIdを実際のURLに合わせ、**管理者登録より前に**次を行う。パスキー登録後のRpId変更では再登録が必要になる。

```bash
# APIの再作成でネットワーク名前空間が変わるため、常に両方を再作成する。
dc up -d --force-recreate api tailscale
```

Tailscale接続済みクライアントから`https://実際のノード名/health`を開き、`Healthy`を確認する。続いて次をNASの管理用端末で実行する。

```bash
dc run --rm --no-deps api initialize-admin
```

この管理コマンドは、初回に**30分有効な招待URLだけを標準出力へ表示**する。管理者ID等の診断は標準エラーへ出す。通常のサービスログではなく、管理者だけが見る一時端末で実行し、リダイレクト・`tee`・共有ログ・スクリーンショットへ保存しない。`--rm`で実行コンテナを残さない。コマンド出力を収集するNASの実行機能は使わない。

30分以内に出力された招待URLをTailscale接続済みブラウザーで開き、表示名を入力してパスキーを登録する。URLのフラグメントは即座に除去され、トークンはタブのメモリだけに保持する。再読み込みした場合は元のリンクから開き直す。`initialize-admin`は再表示しない。未登録の初期管理者の期限切れ・出力紛失時は、診断に出たIDを指定して次で再発行する。

```bash
# 診断に出た管理者IDへ置換する。登録済み停止者にはこの操作を使わない。
dc run --rm --no-deps api issue-enrollment-invitation --account-id REPLACE_ACCOUNT_ID
```

Windowsアプリには同じ`https://実際のノード名`を設定する。将来のスマートフォンもTailscale接続と同じOrigin/RpIdを使う前提だが、スマートフォンアプリの動作確認は別途必要。

参加完了後はTailscale管理側で未使用・不要な認証キーを失効させ、ローカルファイルも空にできる。**ファイル自体は残す**。`TS_AUTH_ONCE=true`と永続stateで通常の再起動を行う。state喪失・ノード失効時には新しい認証キーで再認証が必要。

```bash
: > "$NAS_DATA/tailscale/authkey"
```

## 4. 動作確認

```bash
dc ps
curl --fail --show-error --silent https://monetail-api.tail-example.ts.net/health
dc exec tailscale tailscale serve status
```

curlのURLは実際のものへ置換し、`-k`でTLS検証を無効にしない。次も実機で確認する。

- `/auth/enroll`がHTTPSで開けること。`/health`だけではDB・認証・転送ヘッダーの成功を確認できない。
- ブラウザーのパスキー登録・ログインとWindowsアプリのOAuthログイン、家計簿の作成／再表示。
- 許可した別回線の端末から接続でき、許可していないtailnet利用者・Tailscale未接続端末からは接続できないこと。
- ServeはHTTPS 443だけで、Funnelが無効であること。NASのLANアドレスの8080番へ接続できないこと。
- APIとNAS再起動後も同じDB・Data Protectionキー・PFX・Tailscaleノードが使われること。

## 5. 更新と証明書の期限

先に次節の停止バックアップを取得し、更新元コミット・APIイメージID・Tailscaleバージョンを記録する。APIソース更新、イメージ更新、DB変更を同一作業だと決めつけず、リリース内容を確認する。

```bash
git fetch origin
git checkout REPLACE_REVIEWED_COMMIT_OR_TAG
dc build api
# nas.envのTAILSCALE_IMAGEを承認した安定版へ変更した場合のみ取得する。
dc pull tailscale
dc stop tailscale api
# 必要なDB移行が明示されたリリースだけ、バックアップ後に指定の管理手順を実行する。
dc up -d --force-recreate api tailscale
```

更新では`initialize-admin`、証明書生成、DBファイルの削除、`down -v`を実行しない。DB移行が必要な場合も通常起動には任せず、[42](42-api-foundation.md#db移行登録復旧コマンド)とリリースの指示に従い停止中に行う。DB移行後に古いAPIイメージだけへ戻すのは安全とは限らない。

HTTPS証明書はTailscaleが管理する。OAuth PFXの期限は別管理で、次のようにパスワードをコマンド引数へ入れず確認できる。

```bash
# パスワード入力は非表示。JSONの秘密値をログへ出さず、管理用端末で参照する。
sudo openssl pkcs12 -in "$NAS_DATA/oauth/signing.pfx" -nokeys | openssl x509 -noout -dates
sudo openssl pkcs12 -in "$NAS_DATA/oauth/encryption.pfx" -nokeys | openssl x509 -noout -dates
```

期限前に保守作業を計画する。現行APIは各用途1本の証明書だけを読むため、鍵を替えると既存Windowsトークンの再取得が必要。停止・バックアップ後に**別の新規ディレクトリ**へ生成して所有権を調整し、旧`oauth`を退避して新しいものを配置する。更新のたびに再生成しない。

## 6. バックアップと復元

### 停止バックアップ

バックアップには`db/`（DBと残っていればWAL/SHM）、`keys/`、`oauth/`（PFXとパスワードJSON）、`tailscale/`（state・Serve設定・認証キーファイル）、`nas.env`を含める。Tailscale stateにも秘密鍵がある。NASとは別の保護された保存先へ複製し、媒体の暗号化・アクセス制限・保管世代は運用側で管理する。

```bash
sudo install -d -m 700 /srv/monetail-backups
BACKUP="/srv/monetail-backups/monetail-$(date -u +%Y%m%dT%H%M%SZ).tar"
dc stop tailscale api
# 他の管理コマンド・DB接続も全て終了してからコピーする。
sudo tar --create --file "$BACKUP" --numeric-owner \
  --directory "$(dirname "$NAS_DATA")" "$(basename "$NAS_DATA")"
sudo chmod 600 "$BACKUP"
dc start api tailscale
```

稼働中のDB本体だけをコピーしない。バックアップを取っただけでは認証リセットしない。圧縮や暗号化を加える場合も、秘密をコマンド引数やログへ出さない。復元テストも隔離環境で定期的に実施する。

### 過去のバックアップから復元

対応するAPIコミット／イメージを用意し、復元先のパスを確認する。元ノードを停止し、同じTailscale stateのコピーを二台同時に起動しない。

```bash
dc down
# 空の復元先を用意する。現在のデータは削除せず退避する。
sudo mv "$NAS_DATA" "${NAS_DATA}.before-restore-$(date -u +%Y%m%dT%H%M%SZ)"
# BACKUPは自分が取得した信頼できるアーカイブの絶対パスへ設定する。
sudo tar --extract --file "$BACKUP" --numeric-owner --same-owner \
  --directory "$(dirname "$NAS_DATA")"
# NAS_DATA/nas.envが復元される。配置・Origin・実行UID/GID・PFX期限を確認する。
sudo test -f "$NAS_DATA/db/monetail.db"
# 新しいAPIで古いバックアップを復元する場合だけ、既存DBへ既知の追加移行を適用する。
# dc run --rm --no-deps api initialize-db
dc run --rm --no-deps api reset-authentication --after-restore
dc run --rm --no-deps api issue-recovery-link --account-id REPLACE_ADMIN_ACCOUNT_ID
dc up -d --force-recreate api tailscale
```

過去へ戻る復元では、失効情報も巻き戻るため**停止中に全体の認証リセットを行う**。まず管理者、次に他利用者へ`issue-recovery-link`で10分間の復旧リンクを発行し、パスキーを再登録する。Windowsアプリも再ログインする。DBを再初期化したり、`initialize-admin`で管理者を作り直したりしない。管理者IDはバックアップ前に控え、既存のアカウントを使う。

同じDB・RP ID・鍵を保つ、ロールバックを伴わない通常移設は[42の移設方針](42-api-foundation.md#db移行登録復旧コマンド)に従う。新NASでは実行UID/GIDやDockerのuser namespace設定を再確認する。アクセスできなくてもDBや鍵を削除して直そうとしない。

## TailscaleをNAS本体で実行する場合

| 実行方式 | Serveの転送先 | APIが信頼する転送元 |
| --- | --- | --- |
| **標準: APIと名前空間を共有するTailscaleコンテナ** | `http://127.0.0.1:8080` | `127.0.0.1` |
| NAS本体のTailscale | NASの`http://127.0.0.1:8080`（既存Composeのpublish） | DockerのNAT／userland proxy経由でAPIが実際に受ける送信元IP。通常はbridge gateway等。環境ごとに確認 |
| 別bridgeのTailscaleコンテナ | そのコンテナのlocalhostはAPIを指さない。共通ネットワーク上の`api:8080`等が必要 | その構成で固定したプロキシIP。標準ファイルのままでは対応しない |

NAS本体で動かす場合も同じComposeを使い、**`dc up -d --no-deps api`でAPIだけ**を起動する。sidecarは起動しない。APIの初期化・OAuth秘密設定・永続化は同じ。コンテナ用のTailscale認証キー入力は省略し、Tailscale state・認証キーは本体側の保管・バックアップ手順に従う。

```bash
# NAS本体のTailscaleが接続・HTTPS設定済みであること。
sudo tailscale serve --bg http://127.0.0.1:8080
sudo tailscale serve status
```

この場合、Origin/RpIdは**NAS本体のノードのFQDN**になる。`dc ps -q api`から対象コンテナを特定し、`docker inspect --format '{{json .NetworkSettings.Networks}}' コンテナID`でネットワークとGateway候補を確認する。ただしinspectだけで実際の転送元は断定できない。必要ならNAS上でそのbridgeの8080番宛て**パケットの送信元IPだけ**をパケット解析ツールで調べる。ペイロードや認証ヘッダーを記録しない。

確認した単一の送信元IPを`nas.env`の`MONETAIL_TRUSTED_PROXY`へ設定し、APIだけを再作成する。ネットワーク再作成でGateway等が変わった場合も再確認する。`/auth/enroll`が正しいHTTPSホストで成功することを確かめる。動作しないからといって全プロキシ・全ネットワークを信頼しない。IP特定や固定化が難しい場合は、標準の名前空間共有方式を使う。

APIは信頼した直前のプロキシから**1段分だけ**`X-Forwarded-For/Host/Proto`を処理し、設定したOriginのホストだけを受け付ける。標準のServeはHTTPSを終端し、これらのヘッダーを付け直してループバックへ送る。この間に別のリバースプロキシを挟む構成は、この手順の対象外。

例の固定タグでのヘッダー付与は[Tailscale v1.104.4の公式実装](https://github.com/tailscale/tailscale/blob/v1.104.4/ipn/ipnlocal/serve.go)で確認している。Tailscale更新時も、後述の認証画面と実ログインで確認する。

## 障害対応

| 症状 | 確認すること |
| --- | --- |
| Composeの変数エラー／マウント失敗 | `--env-file`と両`-f`、絶対パス、ファイルの存在。存在しないbind sourceは自動作成しない構成 |
| APIが起動しない | `dc logs --tail 80 api`をローカルで確認。PFXの期限・別鍵・JSONの読込・非root UID/GIDとACL・DB接続文字列を調べる。秘密ファイルの中身は出力しない |
| DB未初期化／読み書き不可 | 初回`initialize-db`の終了コード、DB親の所有権、同時接続とロック、ディスク残量。旧DBの拒否を無理に解除しない |
| `/health`は成功、認証画面は400 | Origin/RpIdと実際のFQDN、Forwarded-Proto/Host、TrustedProxyの実送信元。標準なら名前空間共有と127.0.0.1を確認 |
| HTTPSで到達しない | クライアントのTailscale接続、MagicDNS、HTTPS有効化、端末承認・期限、ポリシーの443許可、`tailscale serve status`。証明書更新に必要な外向き通信も確認 |
| API更新後だけServeが502 | 共有先のAPIコンテナを再作成した可能性。両サービスを`--force-recreate`で再作成 |
| 再起動でログインが失われる | DB・DPキー・PFXが同じ永続領域か、Tailscale stateが保持されているか、ホスト時計が正しいかを確認 |
| 登録できない | 招待／復旧リンクの期限・一回限りの消費、クライアントのパスキー対応、固定したOrigin/RpId、HTTPS、NASと端末の時刻を確認 |

ログ・`docker inspect`全文・`compose config`全文・バックアップ・招待URLを公開IssueやPRへ貼らない。Tailscale再認証時のログにはログイン用URLが出る場合もある。必要なエラー部分だけを、実環境の識別情報と秘密を除いて共有する。

## 検証範囲と公式資料

自動検証はダミー設定と隔離DBを使う。Composeのマージ、PFX再生成拒否、非rootでの秘密読込、DB・管理者初期化、HTTPS転送とHostの境界、再起動時のPFX保全を確認する。実際のTailscale参加・ServeのHTTPS証明書取得更新、NASのACL／再起動、実ブラウザーのパスキー、Windowsアプリとの実接続、実バックアップの復元は**実機で確認が必要**。採用した検証結果は[tests/README](../tests/README.md)に集約する。

- [Docker Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/)／[servicesのsecretsとファイル権限の制約](https://docs.docker.com/reference/compose-file/services/#secrets)
- [Tailscale Docker設定](https://tailscale.com/docs/features/containers/docker/docker-params)／[userspace networking](https://tailscale.com/docs/concepts/userspace-networking)
- [Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve)／[Serve CLI](https://tailscale.com/docs/reference/tailscale-cli/serve)／[HTTPS証明書](https://tailscale.com/docs/how-to/set-up-https-certificates)
- [Tailscale認証キーの安全な取扱い](https://tailscale.com/docs/features/access-control/auth-keys/how-to/secure-auth-keys)／[アクセス制御](https://tailscale.com/docs/features/access-control)
- [ASP.NET Coreの転送ヘッダー](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer)／[Data Protectionの保存](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers)
- [OpenIddictの署名・暗号化証明書](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html#registering-a-certificate-recommended-for-production)／[SQLiteのバックアップ](https://www.sqlite.org/backup.html)
