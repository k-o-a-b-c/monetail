# Docker対応NASでAPIを運用する

Docker EngineとDocker Composeが使えるLinux系NAS向けの手順。メーカー・型番・管理画面には依存しない。API・認証の契約は変更せず、[42 起動・運用](https://github.com/k-o-a-b-c/monetail/blob/main/docs/42-api-foundation.md)の設定と管理コマンドをコンテナで実行する。旧Windows版のDBをそのまま使う手順ではない。

## 構成と前提

```text
Windows／将来のスマートフォン（Tailscale接続＋monetailのパスキー認証）
  → tailnet内のHTTPS :443
  → Tailscale Serve（HTTPS証明書を取得・更新）
  → http://127.0.0.1:8080 → API → SQLite
     └ TailscaleコンテナとAPIコンテナはネットワーク名前空間を共有
```

標準は**GitHub ReleasesのNAS配布アーカイブ**を展開し、GHCRのビルド済みAPIを取得する方式。アーカイブの`compose.yaml`（image専用）に`deploy/nas/compose.nas.yaml`を重ねる。NASにGit・.NET SDK・ソースコード・Docker buildは不要。リポジトリ直下の開発用`compose.yaml`は従来どおりローカルbuild用として維持する。Tailscaleはuserspaceモードで動かし、TUNデバイス・privileged・NET_ADMINは不要。NASの8080番は既存設定どおり**127.0.0.1だけ**で待ち受ける。443番をNASのLANへpublishする必要はない。Funnel、ルーターのポート転送、インターネットへの直接公開は行わない。

必要なもの:

- Docker Compose **2.24.4以降**（検証スクリプトの`!override`にも対応）、Bash、OpenSSL、curl、tar、sha256sum、sudo等によるファイル所有権の変更手段。
- 公開済みのmonetail APIとTailscaleのイメージ。配布ワークフローはlinux/amd64・linux/arm64をネイティブrunnerで検証して同じタグのmanifestにまとめる。他のCPUは対象外。実際のリリースに両architectureのmanifestがあることを事前確認する。
- NASのローカル永続領域。SQLiteのロックが保証されないSMB/NFS共有をDB保存先にしない。
- Tailscaleのアカウントと接続済みクライアント。MagicDNS・HTTPSを有効にし、端末承認とtailnetポリシーで利用者からこのノードのTCP 443への接続を許可する。初期の全許可ポリシーをそのまま安全なアクセス制限とはみなさない。

Tailscaleの参加権限とmonetailのアカウント権限は別。tailnet内でもアプリの認証が必要。HTTPSの公開証明書にはノードのDNS名が含まれ、Certificate Transparencyログにも記録されるため、機密性のある名前は使わない。

Docker Engineはセキュリティ更新済みのものを使う。28.0.0未満には、localhostへpublishしたポートでも同じL2ネットワークから到達できる問題がある。該当するNASではEngineの更新、またはNAS側のファイアウォールによる8080番の遮断を行い、LANから到達しないことを確認してから運用する（[Docker公式の注意](https://docs.docker.com/engine/network/port-publishing/#publishing-to-the-outside-world)）。

## 1. 配置と設定

以下のパス・ホスト名・tailnet名は**全て置換用の例**。実値・秘密情報はGitへ入れない。コマンドはNAS上のBashで実行する。実際の`.env`は使わず、専用の`nas.env`を使う。初回以外は「更新」または「復元」へ進む。

`api-v1.2.3`は説明用であり、公開済みの希望するバージョンへ置換する。`latest`や`releases/latest`には自動追従しない。GitHubでパッケージをPublicにする手順は末尾を参照する。

```bash
# 初回だけ。未使用の配置先を選ぶ。既存のデータ領域には実行しない。
NAS_ROOT=/srv/monetail
sudo mkdir -m 711 -- "$NAS_ROOT"
sudo chown "$(id -u):$(id -g)" "$NAS_ROOT"
NAS_PACKAGES="$NAS_ROOT/packages"
mkdir -m 755 "$NAS_PACKAGES"
umask 077
NAS_RELEASE=api-v1.2.3  # 実際の公開済みバージョンへ置換
mkdir "$NAS_PACKAGES/download-$NAS_RELEASE"
cd "$NAS_PACKAGES/download-$NAS_RELEASE"
curl --fail --location --proto '=https' --tlsv1.2 --output "monetail-nas-$NAS_RELEASE.tar.gz" \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$NAS_RELEASE/monetail-nas-$NAS_RELEASE.tar.gz"
curl --fail --location --proto '=https' --tlsv1.2 --output SHA256SUMS \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$NAS_RELEASE/SHA256SUMS"
sha256sum --check SHA256SUMS
test ! -e "$NAS_PACKAGES/monetail-nas-$NAS_RELEASE"
tar -xzf "monetail-nas-$NAS_RELEASE.tar.gz" -C "$NAS_PACKAGES"
NAS_PACKAGE="$NAS_PACKAGES/monetail-nas-$NAS_RELEASE"
# 公開したmulti-arch manifestの固定digest。秘密情報は入っていない。
. "$NAS_PACKAGE/images.env"
docker pull "$MONETAIL_IMAGE"
NAS_DATA="$NAS_ROOT/data"
NAS_ENV="$NAS_DATA/nas.env"
bash "$NAS_PACKAGE/deploy/nas/initialize-files.sh" "$NAS_DATA" "$MONETAIL_IMAGE"
```

初回スクリプトは新しいデータ領域だけを作り、PFX生成・設定例/Serve設定の配置と実イメージの非root UID/GIDへの所有権設定を行う。既存ディレクトリは拒否する。**DB・管理者は作成しない**。失敗した出力を自動消去せず、未使用だと確認した不完全な領域だけを退避して再試行する。更新・復元時には実行しない。

アーカイブは設定例、Tailscale Serve設定、初回スクリプト、手順、`VERSION`/`images.env`だけを含み、ソース・Git・秘密ファイルは含まない。checksum不一致なら展開・実行を止める。GitHub Releasesの添付アーカイブを使い、自動生成の「Source code」は使わない。

`nas.env`をエディターで変更する。ここに認証キー・パスワードは書かない。

| 項目 | ダミー例と置換内容 |
| --- | --- |
| `MONETAIL_IMAGE` | アーカイブ`images.env`の`ghcr.io/k-o-a-b-c/monetail-api@sha256:…`を初回スクリプトが記入する。`ghcr.io/k-o-a-b-c/monetail-api:v1.2.3`の固定バージョン指定も可能。`:latest`は使わない |
| `MONETAIL_DATA_DIR` | `/srv/monetail/data`。上の`NAS_DATA`と同じ絶対パス。空白やComposeの展開文字を含まないパスを使う |
| `TAILSCALE_HOSTNAME` | `monetail-api`。tailnet内で重複しないノード名 |
| `MONETAIL_ORIGIN` | `https://monetail-api.tail-example.ts.net`。実際に割り当てられたノードの完全なHTTPS Origin。パス・末尾の`/`・クエリーを付けない |
| `MONETAIL_RP_ID` | `monetail-api.tail-example.ts.net`。Originと同じホスト名のみ。スキーム・ポート・パスは付けない |
| `TAILSCALE_IMAGE` | 例は`tailscale/tailscale:v1.104.4`。公式の安定版を確認してバージョンを固定する。必要ならdigestも固定する |
| `TZ` | 例は`Etc/UTC`。日付を扱う運用地域に合わせて、例として`Asia/Tokyo`等のIANAタイムゾーンを指定 |
| `MONETAIL_TRUSTED_PROXY` | 標準構成は`127.0.0.1`のまま。後述のNAS本体でTailscaleを実行する場合だけ変更 |

`serve.json`中の`${TS_CERT_DOMAIN}`は**置換せず残す**。Composeの環境変数ではなく、Tailscaleコンテナがノードの証明書ドメインに置き換える。設定はファイル単体でなくディレクトリごと読取専用マウントする。

以降、次の関数を使う。新しいシェルでは`NAS_PACKAGE`・`NAS_DATA`・`NAS_ENV`と関数を同じ値で再定義する。設定ファイルの展開で秘密値をログに出さない。

```bash
dc() {
  docker compose --project-name monetail --env-file "$NAS_ENV" \
    -f "$NAS_PACKAGE/compose.yaml" -f "$NAS_PACKAGE/deploy/nas/compose.nas.yaml" "$@"
}
dc config --quiet
dc pull tailscale
```

`config --quiet`は値を表示しない構文検証。実ファイルの権限や接続の成功まで保証しない。通常のDocker権限エラーはNAS側のDocker操作権限を整える。Dockerソケットを誰でも操作できる権限にはしない。

## 2. OAuth証明書と秘密ファイル

HTTPS用とは**別**の、署名用・暗号化用の自己署名RSA PFXを作る。スクリプトは異なる鍵、個別の乱数パスワード、読込用JSONを生成し、秘密値は表示しない。

初回スクリプトが`create-oauth-certificates.sh`を呼び、365日有効のOAuth PFXを生成済み。期限を運用記録へ残す。再生成せず、そのまま使う。HTTPS証明書の取得・更新はTailscaleの担当。

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
dc up -d --no-build api tailscale
dc exec tailscale tailscale status --peers=false
dc exec tailscale tailscale serve status
```

Tailscaleの端末承認が必要なら承認し、実際のHTTPS URLを確認する。同名ノードとの衝突等で想定と違えば`nas.env`のOrigin/RpIdを実際のURLに合わせ、**管理者登録より前に**次を行う。パスキー登録後のRpId変更では再登録が必要になる。

```bash
# APIの再作成でネットワーク名前空間が変わるため、常に両方を再作成する。
dc up -d --no-build --force-recreate api tailscale
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

先に次節の停止バックアップを取得し、旧`VERSION`・`nas.env`のAPI digest・Tailscaleバージョンを記録する。既存のデータ領域と`NAS_ENV`をそのまま使い、初回スクリプト・証明書生成・管理者初期化は実行しない。

```bash
NAS_RELEASE=api-v1.2.4  # 実際の公開済み更新先へ置換
mkdir "$NAS_PACKAGES/download-$NAS_RELEASE"
cd "$NAS_PACKAGES/download-$NAS_RELEASE"
curl --fail --location --proto '=https' --tlsv1.2 --output "monetail-nas-$NAS_RELEASE.tar.gz" \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$NAS_RELEASE/monetail-nas-$NAS_RELEASE.tar.gz"
curl --fail --location --proto '=https' --tlsv1.2 --output SHA256SUMS \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$NAS_RELEASE/SHA256SUMS"
sha256sum --check SHA256SUMS
test ! -e "$NAS_PACKAGES/monetail-nas-$NAS_RELEASE"
tar -xzf "monetail-nas-$NAS_RELEASE.tar.gz" -C "$NAS_PACKAGES"
NAS_PACKAGE="$NAS_PACKAGES/monetail-nas-$NAS_RELEASE"
. "$NAS_PACKAGE/images.env"
cp "$NAS_ENV" "$NAS_ENV.before-update-$(date -u +%Y%m%dT%H%M%SZ)"
sed -i "s|^MONETAIL_IMAGE=.*|MONETAIL_IMAGE=$MONETAIL_IMAGE|" "$NAS_ENV"
dc config --quiet
dc pull api
# Tailscaleの固定バージョンを明示変更した場合のみ dc pull tailscale
# 新イメージのUID/GIDを確認。変更があればdb/keys/oauthの所有権・ACLを調整する。
docker run --rm --pull never --entrypoint id "$MONETAIL_IMAGE"
dc stop tailscale api
# リリースでDB移行が指定された場合だけ、停止バックアップ後に指定の手順を実行する。
dc up -d --no-build --force-recreate api tailscale
dc ps
```

`MONETAIL_IMAGE`をシェルでexportしていると`nas.env`より優先されるため、意図した値か`dc config --images`で確認する。digest指定では同じ内容を再取得し、バージョンを勝手に切り替えない。API再作成時は名前空間を共有するTailscaleも同時に再作成する。

以前のGit/build方式から移る場合も、既存`MONETAIL_DATA_DIR`のdb/keys/oauth/tailscaleと`nas.env`をそのまま保持する。旧構成で停止バックアップを取り、配布アーカイブを展開して`NAS_PACKAGE`を設定し、既存`nas.env`へ`MONETAIL_IMAGE`だけ追加する。同じCompose project-name `monetail`で上のpull/再作成を行う。初回スクリプトやDB/管理者初期化は実行しない。

更新では`initialize-admin`、証明書生成、DBファイルの削除、`down -v`を実行しない。DB移行が必要な場合も通常起動には任せず、[42](https://github.com/k-o-a-b-c/monetail/blob/main/docs/42-api-foundation.md#db移行登録復旧コマンド)とリリースの指示に従い停止中に行う。DB移行後に古いAPIイメージだけへ戻すのは安全とは限らない。

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

バックアップ時と対応するNAS配布アーカイブと固定APIイメージを用意し、`NAS_PACKAGE`を該当版へ設定する。復元先のパスを確認する。元ノードを停止し、同じTailscale stateのコピーを二台同時に起動しない。

```bash
dc down
# 空の復元先を用意する。現在のデータは削除せず退避する。
sudo mv "$NAS_DATA" "${NAS_DATA}.before-restore-$(date -u +%Y%m%dT%H%M%SZ)"
# BACKUPは自分が取得した信頼できるアーカイブの絶対パスへ設定する。
sudo tar --extract --file "$BACKUP" --numeric-owner --same-owner \
  --directory "$(dirname "$NAS_DATA")"
# NAS_DATA/nas.envが復元される。配置・Origin・実行UID/GID・PFX期限を確認する。
sudo test -f "$NAS_DATA/db/monetail.db"
dc pull api tailscale
# 新しいAPIで古いバックアップを復元する場合だけ、既存DBへ既知の追加移行を適用する。
# dc run --rm --no-deps api initialize-db
dc run --rm --no-deps api reset-authentication --after-restore
dc run --rm --no-deps api issue-recovery-link --account-id REPLACE_ADMIN_ACCOUNT_ID
dc up -d --no-build --force-recreate api tailscale
```

過去へ戻る復元では、失効情報も巻き戻るため**停止中に全体の認証リセットを行う**。まず管理者、次に他利用者へ`issue-recovery-link`で10分間の復旧リンクを発行し、パスキーを再登録する。Windowsアプリも再ログインする。DBを再初期化したり、`initialize-admin`で管理者を作り直したりしない。管理者IDはバックアップ前に控え、既存のアカウントを使う。

同じDB・RP ID・鍵を保つ、ロールバックを伴わない通常移設は[42の移設方針](https://github.com/k-o-a-b-c/monetail/blob/main/docs/42-api-foundation.md#db移行登録復旧コマンド)に従う。新NASでは実行UID/GIDやDockerのuser namespace設定を再確認する。アクセスできなくてもDBや鍵を削除して直そうとしない。

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
| GHCRの401/403 | パッケージのPublic設定、指定バージョン/digestの公開、既存packageへのActions権限を確認。Public配布はNASでログイン不要 |
| no matching manifest | CPUがamd64/arm64か、リリースに両manifestがあるか確認 |
| Composeの変数エラー／マウント失敗 | `--env-file`と両`-f`、絶対パス、ファイルの存在。存在しないbind sourceは自動作成しない構成 |
| APIが起動しない | `dc logs --tail 80 api`をローカルで確認。PFXの期限・別鍵・JSONの読込・非root UID/GIDとACL・DB接続文字列を調べる。秘密ファイルの中身は出力しない |
| DB未初期化／読み書き不可 | 初回`initialize-db`の終了コード、DB親の所有権、同時接続とロック、ディスク残量。旧DBの拒否を無理に解除しない |
| `/health`は成功、認証画面は400 | Origin/RpIdと実際のFQDN、Forwarded-Proto/Host、TrustedProxyの実送信元。標準なら名前空間共有と127.0.0.1を確認 |
| HTTPSで到達しない | クライアントのTailscale接続、MagicDNS、HTTPS有効化、端末承認・期限、ポリシーの443許可、`tailscale serve status`。証明書更新に必要な外向き通信も確認 |
| API更新後だけServeが502 | 共有先のAPIコンテナを再作成した可能性。両サービスを`--force-recreate`で再作成 |
| 再起動でログインが失われる | DB・DPキー・PFXが同じ永続領域か、Tailscale stateが保持されているか、ホスト時計が正しいかを確認 |
| 登録できない | 招待／復旧リンクの期限・一回限りの消費、クライアントのパスキー対応、固定したOrigin/RpId、HTTPS、NASと端末の時刻を確認 |

ログ・`docker inspect`全文・`compose config`全文・バックアップ・招待URLを公開IssueやPRへ貼らない。Tailscale再認証時のログにはログイン用URLが出る場合もある。必要なエラー部分だけを、実環境の識別情報と秘密を除いて共有する。

## GitHubでの配布（リポジトリ管理者向け・NASでは実行しない）

`.github/workflows/release-api.yml`は`api-vX.Y.Z`タグpushだけで起動する。対象コミットがmainの履歴上にあり、**同じSHAに対するTestsのmain push実行が成功済み**の場合だけ先へ進む。PR/forkの成功や別SHAの結果は採用しない。mainのCI完了前にタグを付けた場合は、成功後に失敗ワークフローを再実行する。

```bash
# 開発用PCで実行。CIが成功したmainのコミットを明示して選ぶ。
git fetch origin main
git tag api-v1.2.3 REPLACE_VERIFIED_MAIN_COMMIT
git push origin api-v1.2.3
```

amd64/arm64の各ネイティブrunnerでAPI/ブラウザースクリプトのテスト、Dockerビルドと通常/NAS/配布アーカイブの起動・保全を検証する。各イメージをpush後、両方成功時だけ`ghcr.io/k-o-a-b-c/monetail-api:v1.2.3`のmulti-arch manifestとReleasesのアーカイブ・SHA256SUMSを公開する。`latest`は作らない。作業用`build-…`タグはNASの運用指定に使わない。

認証は`GITHUB_TOKEN`だけを使う。検証ジョブはcontents/actionsのread、イメージjobはcontents read/packages write、最終公開jobはcontents/packages writeで、checkoutへ認証を残さない。PRのCIでは公開しない。既存のバージョンタグは上書きしない。公開途中に失敗してvX.Y.Zのmanifestだけ存在する場合も同じバージョンへ再公開せず、原因を確認して新しいバージョンタグを作る。

初回のGHCRパッケージは、公開リポジトリに紐づけても自動でPublicになるとは限らない。GitHubの所有者のPackagesから`monetail-api`のPackage settingsを開き、リポジトリを確認して可視性を**Public**へ設定する。既存パッケージを使う場合はManage Actions accessで`k-o-a-b-c/monetail`へwrite権限を付与する。設定反映後、未ログイン環境で公開版をpullできることを確認する。実際の公開設定変更は管理者が行う。

Privateを選ぶ場合はNASでもGHCR認証が必要。専用アカウントのread:packages権限のPAT classic等を公式手順に従い非表示入力から`docker login ghcr.io --username REPLACE_USER --password-stdin`へ渡す。トークンをCompose・Git・コマンド引数・ログへ記載しない。標準手順はPublicイメージの匿名pullを前提とする。

## 検証範囲と公式資料

自動検証はダミー設定と隔離DBを使う。既存の開発用構成に加え、ソースなしのアーカイブ内からimage専用Composeを起動し、初回再実行拒否、DB・DPキー・PFX・Tailscale state用ファイルの再作成前後の一致、停止バックアップと隔離復元を検証する。実際のGHCR公開・匿名pull・GitHubの可視性設定は、マージ後の初回リリースで確認する。実ARM64検証はタグ配布ワークフローで行い、未実行なら検証済みとしない。実際のTailscale参加・ServeのHTTPS証明書取得更新、NASのACL／再起動、実ブラウザーのパスキー、Windowsアプリとの実接続、実バックアップの復元は**実機で確認が必要**。採用した検証結果は[tests/README](https://github.com/k-o-a-b-c/monetail/blob/main/tests/README.md)に集約する。

- [Docker Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/)／[servicesのsecretsとファイル権限の制約](https://docs.docker.com/reference/compose-file/services/#secrets)
- [Tailscale Docker設定](https://tailscale.com/docs/features/containers/docker/docker-params)／[userspace networking](https://tailscale.com/docs/concepts/userspace-networking)
- [Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve)／[Serve CLI](https://tailscale.com/docs/reference/tailscale-cli/serve)／[HTTPS証明書](https://tailscale.com/docs/how-to/set-up-https-certificates)
- [Tailscale認証キーの安全な取扱い](https://tailscale.com/docs/features/access-control/auth-keys/how-to/secure-auth-keys)／[アクセス制御](https://tailscale.com/docs/features/access-control)
- [ASP.NET Coreの転送ヘッダー](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer)／[Data Protectionの保存](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers)
- [OpenIddictの署名・暗号化証明書](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html#registering-a-certificate-recommended-for-production)／[SQLiteのバックアップ](https://www.sqlite.org/backup.html)

- [GitHub ActionsでDockerイメージを公開](https://docs.github.com/actions/use-cases-and-examples/publishing-packages/publishing-docker-images)／[GHCR認証・匿名pull](https://docs.github.com/packages/working-with-a-github-packages-registry/working-with-the-container-registry)／[パッケージ可視性・Actionsアクセス](https://docs.github.com/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility)
- [GitHubのARM runner](https://docs.github.com/actions/reference/runners/github-hosted-runners)／[Docker multi-platform](https://docs.docker.com/build/building/multi-platform/)
