# 配備例：Dockerサーバーと別管理のTailscale Serve

Docker対応Linuxサーバーと特定のネットワーク構成を想定した**配備例**。NASもDockerが動作するなら利用できる。Tailscaleはmonetailに必須ではない。共通の安全要件は[配備・運用](../deployment.md)を参照する。Docker EngineとDocker Composeが使えるLinuxサーバー向けで、メーカー・型番・管理画面には依存しない。API・認証の契約は変更せず、[42 起動・運用](https://github.com/k-o-a-b-c/monetail/blob/main/docs/42-api-foundation.md)の設定と管理コマンドをコンテナで実行する。旧Windows版のDBをそのまま使う手順ではない。

## 構成と前提

```text
Windows／将来のスマートフォン（Tailscale接続＋monetailのパスキー認証）
  → tailnet内のHTTPS :443
  → 既存のTailscale Serveコンテナ（hostネットワーク・別管理）
  → ホストの http://127.0.0.1:8080
  → monetail APIコンテナ（hostネットワーク・loopback待受）
  → SQLite（サーバーの永続データ領域）
```

この例では**GitHub ReleasesのDocker配布アーカイブ**を展開し、GHCRのビルド済みAPIを取得する方式。アーカイブの`compose.yaml`（image専用）に`deploy/docker/compose.server.yaml`を重ねる。サーバーにGit・.NET SDK・ソースコード・Docker buildは不要。リポジトリ直下の開発用`compose.yaml`は従来どおりローカルbuild用として維持する。**Tailscaleコンテナの作成・認証・Serve設定・更新はmonetailでは管理しない。** 別途稼働中のTailscaleコンテナが`network_mode: host`でHTTPSを終端し、サーバーの`127.0.0.1:8080`へ転送することを前提とする。APIもhostネットワークで**127.0.0.1だけ**を待ち受ける。別コンテナがbridgeネットワークの場合は、この構成をそのまま適用しない。443番のLAN公開、Funnel、ルーターのポート転送、インターネットへの直接公開は行わない。

必要なもの:

- Docker Compose **2.24.4以降**（検証スクリプトの`!override`にも対応）、Bash、OpenSSL、curl、tar、sha256sum、sudo等によるファイル所有権の変更手段。
- 公開済みのmonetail APIイメージ。既存のTailscaleコンテナは別途稼働済みであること。配布ワークフローはlinux/amd64・linux/arm64をネイティブrunnerで検証して同じタグのmanifestにまとめる。他のCPUは対象外。実際のリリースに両architectureのmanifestがあることを事前確認する。
- サーバーのローカル永続領域。SQLiteのロックが保証されないSMB/NFS共有をDB保存先にしない。
- Tailscaleのアカウントと接続済みクライアント。MagicDNS・HTTPSを有効にし、端末承認とtailnetポリシーで利用者からこのノードのTCP 443への接続を許可する。初期の全許可ポリシーをそのまま安全なアクセス制限とはみなさない。

Tailscaleの参加権限とmonetailのアカウント権限は別。tailnet内でもアプリの認証が必要。HTTPSの公開証明書にはノードのDNS名が含まれ、Certificate Transparencyログにも記録されるため、機密性のある名前は使わない。

Docker Engineはセキュリティ更新済みのものを使う。hostネットワークではComposeの`ports:`は使わず、APIの`ASPNETCORE_URLS=http://127.0.0.1:8080`で待受けアドレスを限定する。サーバー上で他のプロセスが8080番を占有していないこと、サーバーのLANアドレスの8080番では接続できないことを確認する。

## 1. 配置と設定

以下のパス・ホスト名・tailnet名は**全て置換用の例**。実値・秘密情報はGitへ入れない。コマンドはサーバー上のBashで実行する。実際の`.env`は使わず、専用の`server.env`を使う。初回以外は「更新」または「復元」へ進む。

`api-v1.2.3`は説明用であり、公開済みの希望するバージョンへ置換する。`latest`や`releases/latest`には自動追従しない。GitHubでパッケージをPublicにする手順は末尾を参照する。

```bash
# 初回だけ。未使用の配置先を選ぶ。既存のデータ領域には実行しない。
SERVER_ROOT=/srv/monetail
sudo mkdir -m 711 -- "$SERVER_ROOT"
sudo chown "$(id -u):$(id -g)" "$SERVER_ROOT"
SERVER_PACKAGES="$SERVER_ROOT/packages"
mkdir -m 755 "$SERVER_PACKAGES"
umask 077
SERVER_RELEASE=api-v1.2.3  # 実際の公開済みバージョンへ置換
mkdir "$SERVER_PACKAGES/download-$SERVER_RELEASE"
cd "$SERVER_PACKAGES/download-$SERVER_RELEASE"
curl --fail --location --proto '=https' --tlsv1.2 --output "monetail-docker-$SERVER_RELEASE.tar.gz" \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$SERVER_RELEASE/monetail-docker-$SERVER_RELEASE.tar.gz"
curl --fail --location --proto '=https' --tlsv1.2 --output SHA256SUMS \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$SERVER_RELEASE/SHA256SUMS"
sha256sum --check SHA256SUMS
test ! -e "$SERVER_PACKAGES/monetail-docker-$SERVER_RELEASE"
tar -xzf "monetail-docker-$SERVER_RELEASE.tar.gz" -C "$SERVER_PACKAGES"
SERVER_PACKAGE="$SERVER_PACKAGES/monetail-docker-$SERVER_RELEASE"
# 公開したmulti-arch manifestの固定digest。秘密情報は入っていない。
. "$SERVER_PACKAGE/images.env"
docker pull "$MONETAIL_IMAGE"
SERVER_DATA="$SERVER_ROOT/data"
SERVER_ENV="$SERVER_DATA/server.env"
bash "$SERVER_PACKAGE/deploy/docker/initialize-files.sh" "$SERVER_DATA" "$MONETAIL_IMAGE"
```

初回スクリプトは新しいデータ領域だけを作り、PFX生成・設定例の配置と実イメージの非root UID/GIDへの所有権設定を行う。既存ディレクトリは拒否する。**DB・管理者は作成しない**。失敗した出力を自動消去せず、未使用だと確認した不完全な領域だけを退避して再試行する。更新・復元時には実行しない。

アーカイブはAPI設定例、初回スクリプト、手順、`VERSION`/`images.env`だけを含み、ソース・Git・秘密ファイルは含まない。checksum不一致なら展開・実行を止める。GitHub Releasesの添付アーカイブを使い、自動生成の「Source code」は使わない。

`server.env`をエディターで変更する。ここにパスワードは書かない。

| 項目 | ダミー例と置換内容 |
| --- | --- |
| `MONETAIL_IMAGE` | アーカイブ`images.env`の`ghcr.io/k-o-a-b-c/monetail-api@sha256:…`を初回スクリプトが記入する。`ghcr.io/k-o-a-b-c/monetail-api:v1.2.3`の固定バージョン指定も可能。`:latest`は使わない |
| `MONETAIL_DATA_DIR` | `/srv/monetail/data`。上の`SERVER_DATA`と同じ絶対パス。空白やComposeの展開文字を含まないパスを使う |
| `MONETAIL_ORIGIN` | `https://monetail-api.tail-example.ts.net`。実際に割り当てられたノードの完全なHTTPS Origin。パス・末尾の`/`・クエリーを付けない |
| `MONETAIL_RP_ID` | `monetail-api.tail-example.ts.net`。Originと同じホスト名のみ。スキーム・ポート・パスは付けない |
| `TZ` | 例は`Etc/UTC`。日付を扱う運用地域に合わせて、例として`Asia/Tokyo`等のIANAタイムゾーンを指定 |
| `MONETAIL_TRUSTED_PROXY` | hostネットワークの既存Serveがlocalhostから接続するため`127.0.0.1`のまま |

HTTPSのServe設定は別管理のTailscaleコンテナに既に適用されている前提とする。転送先は`http://127.0.0.1:8080`、外部の接続先は`https://実際のノード名`であること。HTTPの443番ではWinUIから接続できない。

以降、次の関数を使う。新しいシェルでは`SERVER_PACKAGE`・`SERVER_DATA`・`SERVER_ENV`と関数を同じ値で再定義する。設定ファイルの展開で秘密値をログに出さない。

```bash
dc() {
  docker compose --project-name monetail --env-file "$SERVER_ENV" \
    -f "$SERVER_PACKAGE/compose.yaml" -f "$SERVER_PACKAGE/deploy/docker/compose.server.yaml" "$@"
}
dc config --quiet
dc pull api
```

`config --quiet`は値を表示しない構文検証。実ファイルの権限や接続の成功まで保証しない。通常のDocker権限エラーはサーバー側のDocker操作権限を整える。Dockerソケットを誰でも操作できる権限にはしない。

## 2. OAuth証明書と秘密ファイル

HTTPS用とは**別**の、署名用・暗号化用の自己署名RSA PFXを作る。スクリプトは異なる鍵、個別の乱数パスワード、読込用JSONを生成し、秘密値は表示しない。

初回スクリプトが`create-oauth-certificates.sh`を呼び、365日有効のOAuth PFXを生成済み。期限を運用記録へ残す。再生成せず、そのまま使う。HTTPS証明書の取得・更新はTailscaleの担当。

`oauth`が既にあれば、空ディレクトリやシンボリックリンクも含めて生成を拒否する。再実行で上書きしない。途中失敗でも既存の出力先を自動削除しない。**初回作業で未使用だと確認できた不完全なディレクトリ**だけを別名へ退避してから再試行する。更新時には実行しない。

生成物は`signing.pfx`・`encryption.pfx`・`appsettings.Production.json`。最後のJSONには両PFXのパスワードが入る。Compose secretsから`/app/appsettings.Production.json`へ読取専用で渡し、ASP.NET Coreの標準設定読込を使う。パスワードは環境変数・コマンド引数に渡さない。生成中の一時PEM鍵・パスワードファイルは終了時に削除する。

Composeのファイルベースsecretsはホスト上の暗号化保管機能ではない。bind mountであり、`uid/gid/mode`指定は適用されないため、上のように**実イメージの非root UID/GIDとホスト上の権限**を合わせる。サーバーのACL・rootless Docker・user namespace remapを使う環境では、そのマッピングに合わせて所有権を調整する。`chmod 777`で回避しない。

Tailscaleの認証キーやstateは既存コンテナ側で保管・バックアップする。monetailのデータ領域に追加しない。

## 3. DB初期化・起動・管理者登録

APIを停止した状態で**初回のみ**実行する。DBの通常起動はスキーマを自動作成しない。

```bash
dc run --rm --no-deps api initialize-db
dc up -d --no-build api
```

既存Tailscale ServeのHTTPS URLを確認する。実際のURLと`server.env`のOrigin/RpIdが違えば、**管理者登録より前に**設定を修正して次を行う。パスキー登録後のRpId変更では再登録が必要になる。

```bash
# APIだけを再作成する。既存Tailscaleコンテナは変更しない。
dc up -d --no-build --force-recreate api
```

Tailscale接続済みクライアントから`https://実際のノード名/health`を開き、`Healthy`を確認する。続いて次をサーバーの管理用端末で実行する。

```bash
dc run --rm --no-deps api initialize-admin
```

この管理コマンドは、初回に**30分有効な招待URLだけを標準出力へ表示**する。管理者ID等の診断は標準エラーへ出す。通常のサービスログではなく、管理者だけが見る一時端末で実行し、リダイレクト・`tee`・共有ログ・スクリーンショットへ保存しない。`--rm`で実行コンテナを残さない。コマンド出力を収集するサーバーの実行機能は使わない。

30分以内に出力された招待URLをTailscale接続済みブラウザーで開き、表示名を入力してパスキーを登録する。URLのフラグメントは即座に除去され、トークンはタブのメモリだけに保持する。再読み込みした場合は元のリンクから開き直す。`initialize-admin`は再表示しない。未登録の初期管理者の期限切れ・出力紛失時は、診断に出たIDを指定して次で再発行する。

```bash
# 診断に出た管理者IDへ置換する。登録済み停止者にはこの操作を使わない。
dc run --rm --no-deps api issue-enrollment-invitation --account-id REPLACE_ACCOUNT_ID
```

Windowsアプリには同じ`https://実際のノード名`を設定する。将来のスマートフォンもTailscale接続と同じOrigin/RpIdを使う前提だが、スマートフォンアプリの動作確認は別途必要。

### 税率履歴JSONの配置（管理CLI未実装）

初期税率データの正本はGitHubの[`config/tax-history.json`](https://github.com/k-o-a-b-c/monetail/blob/main/config/tax-history.json)。GitHubに保存するだけではDBに登録されない。サーバーにはリリース版ごとの`$SERVER_PACKAGE`の内部ではなく、**更新で残る`$SERVER_ROOT/config/tax-history.json`**（例：`/srv/monetail/config/tax-history.json`）として配置する。これは一般的なJSONで秘密情報ではない。

```bash
# GitHubでレビュー済みのconfig/tax-history.jsonを、管理用端末からサーバーへ転送済みとする。
install -d -m 755 "$SERVER_ROOT/config"
install -m 644 /path/to/tax-history.json "$SERVER_ROOT/config/tax-history.json"
```

**この段階では`sync-tax-rates`は未実装なので、次のコマンドはCLIを含むAPIイメージを配布した後にのみ使用する。** API停止・[DBの停止バックアップ](#停止バックアップ)が完了した状態で、上記の`dc`関数を使用する。通常APIコンテナにJSONのマウントを追加する必要はなく、**管理コマンド実行時だけ読取専用で渡す**。

```bash
# CLI実装後：まずDB無変更の差分確認を行う。
dc run --rm --no-deps \
  --volume "$SERVER_ROOT/config/tax-history.json:/tmp/tax-history.json:ro" \
  api sync-tax-rates --file /tmp/tax-history.json --dry-run

# 差分を確認・承認した場合に限ってDBへ反映する。
dc run --rm --no-deps \
  --volume "$SERVER_ROOT/config/tax-history.json:/tmp/tax-history.json:ro" \
  api sync-tax-rates --file /tmp/tax-history.json --apply
```

通常の`--apply`は履歴の削除予定があれば拒否する。削除が明示的に必要な場合だけ`--apply --allow-delete`を指定する。税率改定時もGitHubのPR確認→サーバーファイル更新→差分確認→明示反映を同じ手順で行う。既存DB・認証設定を作り直さず、反映後はAPIを起動して運用記録に使用したJSONのコミットを記録する。

## 4. 動作確認

```bash
dc ps
curl --fail --show-error --silent https://monetail-api.tail-example.ts.net/health
```

curlのURLは実際のものへ置換し、`-k`でTLS検証を無効にしない。次も実機で確認する。

- `/auth/enroll`がHTTPSで開けること。`/health`だけではDB・認証・転送ヘッダーの成功を確認できない。
- ブラウザーのパスキー登録・ログインとWindowsアプリのOAuthログイン、家計簿の作成／再表示。
- 許可した別回線の端末から接続でき、許可していないtailnet利用者・Tailscale未接続端末からは接続できないこと。
- ServeはHTTPS 443だけで、Funnelが無効であること。サーバーのLANアドレスの8080番へ接続できないこと。
- APIとサーバー再起動後も同じDB・Data Protectionキー・PFXが使われること。Tailscaleコンテナの再起動・ノード維持は別途確認する。

## 5. 更新と証明書の期限

先に次節の停止バックアップを取得し、旧`VERSION`・`server.env`のAPI digestを記録する。既存のデータ領域と`SERVER_ENV`をそのまま使い、初回スクリプト・証明書生成・管理者初期化は実行しない。

```bash
SERVER_RELEASE=api-v1.2.4  # 実際の公開済み更新先へ置換
mkdir "$SERVER_PACKAGES/download-$SERVER_RELEASE"
cd "$SERVER_PACKAGES/download-$SERVER_RELEASE"
curl --fail --location --proto '=https' --tlsv1.2 --output "monetail-docker-$SERVER_RELEASE.tar.gz" \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$SERVER_RELEASE/monetail-docker-$SERVER_RELEASE.tar.gz"
curl --fail --location --proto '=https' --tlsv1.2 --output SHA256SUMS \
  "https://github.com/k-o-a-b-c/monetail/releases/download/$SERVER_RELEASE/SHA256SUMS"
sha256sum --check SHA256SUMS
test ! -e "$SERVER_PACKAGES/monetail-docker-$SERVER_RELEASE"
tar -xzf "monetail-docker-$SERVER_RELEASE.tar.gz" -C "$SERVER_PACKAGES"
SERVER_PACKAGE="$SERVER_PACKAGES/monetail-docker-$SERVER_RELEASE"
. "$SERVER_PACKAGE/images.env"
cp "$SERVER_ENV" "$SERVER_ENV.before-update-$(date -u +%Y%m%dT%H%M%SZ)"
sed -i "s|^MONETAIL_IMAGE=.*|MONETAIL_IMAGE=$MONETAIL_IMAGE|" "$SERVER_ENV"
dc config --quiet
dc pull api
# 新イメージのUID/GIDを確認。変更があればdb/keys/oauthの所有権・ACLを調整する。
docker run --rm --pull never --entrypoint id "$MONETAIL_IMAGE"
dc stop api
# 更新先が既存DB形式に対応することを事前確認。initialize-dbによる旧DBの変換は行わない。
dc up -d --no-build --force-recreate api
dc ps
```

`MONETAIL_IMAGE`をシェルでexportしていると`server.env`より優先されるため、意図した値か`dc config --images`で確認する。digest指定では同じ内容を再取得し、バージョンを勝手に切り替えない。既存Tailscaleコンテナは更新対象に含めない。

DB形式に互換性があるリリースでGit/build方式から移る場合も、既存`MONETAIL_DATA_DIR`のdb/keys/oauthと`server.env`をそのまま保持する。旧構成で停止バックアップを取り、配布アーカイブを展開して`SERVER_PACKAGE`を設定し、既存`server.env`へ`MONETAIL_IMAGE`だけ追加する。同じCompose project-name `monetail`で上のpull/再作成を行う。初回スクリプトやDB/管理者初期化は実行しない。

更新では`initialize-admin`、証明書生成、DBファイルの削除、`down -v`を実行しない。`initialize-db`は既存DBへ追加移行しない。旧API形式との互換性がない場合、旧DB・鍵・対応イメージを保持し、別の新規保存先で初回手順を行う。旧DBのデータを新方式へ移す機能はない。[DB初期化の正本](https://github.com/k-o-a-b-c/monetail/blob/main/docs/42-api-foundation.md#db初期化登録復旧コマンド)とリリースの対応形式を確認する。

HTTPS証明書はTailscaleが管理する。OAuth PFXの期限は別管理で、次のようにパスワードをコマンド引数へ入れず確認できる。

```bash
# パスワード入力は非表示。JSONの秘密値をログへ出さず、管理用端末で参照する。
sudo openssl pkcs12 -in "$SERVER_DATA/oauth/signing.pfx" -nokeys | openssl x509 -noout -dates
sudo openssl pkcs12 -in "$SERVER_DATA/oauth/encryption.pfx" -nokeys | openssl x509 -noout -dates
```

期限前に保守作業を計画する。現行APIは各用途1本の証明書だけを読むため、鍵を替えると既存Windowsトークンの再取得が必要。停止・バックアップ後に**別の新規ディレクトリ**へ生成して所有権を調整し、旧`oauth`を退避して新しいものを配置する。更新のたびに再生成しない。

## 6. バックアップと復元

### 停止バックアップ

バックアップには`db/`（DBと残っていればWAL/SHM）、`keys/`、`oauth/`（PFXとパスワードJSON）、`server.env`を含める。Tailscaleのstate・設定は別コンテナの運用側で管理する。サーバーとは別の保護された保存先へ複製し、媒体の暗号化・アクセス制限・保管世代は運用側で管理する。

```bash
sudo install -d -m 700 /srv/monetail-backups
BACKUP="/srv/monetail-backups/monetail-$(date -u +%Y%m%dT%H%M%SZ).tar"
dc stop api
# 他の管理コマンド・DB接続も全て終了してからコピーする。
sudo tar --create --file "$BACKUP" --numeric-owner \
  --directory "$(dirname "$SERVER_DATA")" "$(basename "$SERVER_DATA")"
sudo chmod 600 "$BACKUP"
dc start api
```

稼働中のDB本体だけをコピーしない。バックアップを取っただけでは認証リセットしない。圧縮や暗号化を加える場合も、秘密をコマンド引数やログへ出さない。復元テストも隔離環境で定期的に実施する。

### 過去のバックアップから復元

バックアップ時と対応するDocker配布アーカイブと固定APIイメージを用意し、`SERVER_PACKAGE`を該当版へ設定する。復元先のパスを確認する。復元時も既存Tailscaleコンテナを操作しない。

```bash
dc down
# 空の復元先を用意する。現在のデータは削除せず退避する。
sudo mv "$SERVER_DATA" "${SERVER_DATA}.before-restore-$(date -u +%Y%m%dT%H%M%SZ)"
# BACKUPは自分が取得した信頼できるアーカイブの絶対パスへ設定する。
sudo tar --extract --file "$BACKUP" --numeric-owner --same-owner \
  --directory "$(dirname "$SERVER_DATA")"
# SERVER_DATA/server.envが復元される。配置・Origin・実行UID/GID・PFX期限を確認する。
sudo test -f "$SERVER_DATA/db/monetail.db"
dc pull api
# バックアップのDB形式に対応する固定イメージを使う。
# initialize-dbで旧DBを変換・再作成しない。
dc run --rm --no-deps api reset-authentication --after-restore
dc run --rm --no-deps api issue-recovery-link --account-id REPLACE_ADMIN_ACCOUNT_ID
dc up -d --no-build --force-recreate api
```

過去へ戻る復元では、失効情報も巻き戻るため**停止中に全体の認証リセットを行う**。まず管理者、次に他利用者へ`issue-recovery-link`で10分間の復旧リンクを発行し、パスキーを再登録する。Windowsアプリも再ログインする。DBを再初期化したり、`initialize-admin`で管理者を作り直したりしない。管理者IDはバックアップ前に控え、既存のアカウントを使う。

同じDB・RP ID・鍵を保つ、ロールバックを伴わない通常移設は[42の移設方針](https://github.com/k-o-a-b-c/monetail/blob/main/docs/42-api-foundation.md#db移行登録復旧コマンド)に従う。新サーバーでは実行UID/GIDやDockerのuser namespace設定を再確認する。アクセスできなくてもDBや鍵を削除して直そうとしない。

## 別管理のTailscaleとの接続条件

既存Tailscaleコンテナは`network_mode: host`、monetail APIも`network_mode: host`とし、APIは`127.0.0.1:8080`だけを待ち受ける。Tailscale ServeはHTTPSを終端し`http://127.0.0.1:8080`へ転送する。Tailscaleの起動・設定・更新はこのComposeや配布アーカイブでは一切行わない。Tailscaleコンテナが停止している場合も、monetailから自動起動しない。

APIは信頼した直前のプロキシ`127.0.0.1`から**1段分だけ**`X-Forwarded-For/Host/Proto`を処理し、指定したOriginのホストだけを受け付ける。HTTPSの`/health`だけでなく`/auth/enroll`とWinUIのOAuth接続を確認する。別ネットワークモードや間に追加プロキシを挟む構成は対象外。

## 障害対応

| 症状 | 確認すること |
| --- | --- |
| GHCRの401/403 | パッケージのPublic設定、指定バージョン/digestの公開、既存packageへのActions権限を確認。Public配布はサーバーでログイン不要 |
| no matching manifest | CPUがamd64/arm64か、リリースに両manifestがあるか確認 |
| Composeの変数エラー／マウント失敗 | `--env-file`と両`-f`、絶対パス、ファイルの存在。存在しないbind sourceは自動作成しない構成 |
| APIが起動しない | `dc logs --tail 80 api`をローカルで確認。PFXの期限・別鍵・JSONの読込・非root UID/GIDとACL・DB接続文字列を調べる。秘密ファイルの中身は出力しない |
| DB未初期化／読み書き不可 | 初回`initialize-db`の終了コード、DB親の所有権、同時接続とロック、ディスク残量。旧DBの拒否を無理に解除しない |
| `/health`は成功、認証画面は400 | Origin/RpIdと実際のFQDN、Forwarded-Proto/Host、TrustedProxyの実送信元。hostネットワークと127.0.0.1を確認 |
| HTTPSで到達しない | クライアントのTailscale接続、MagicDNS、HTTPS有効化、端末承認・期限、ポリシーの443許可、`tailscale serve status`。証明書更新に必要な外向き通信も確認 |
| API更新後だけServeが502 | APIのlocalhost待受設定とTailscale Serveの転送先、API起動状態を確認 |
| 再起動でログインが失われる | DB・DPキー・PFXが同じ永続領域か、ホスト時計が正しいかを確認 |
| 登録できない | 招待／復旧リンクの期限・一回限りの消費、クライアントのパスキー対応、固定したOrigin/RpId、HTTPS、サーバーと端末の時刻を確認 |

ログ・`docker inspect`全文・`compose config`全文・バックアップ・招待URLを公開IssueやPRへ貼らない。別管理のTailscaleログには認証URLなどが含まれる場合がある。必要なエラー部分だけを、実環境の識別情報と秘密を除いて共有する。

## GitHubでの配布（リポジトリ管理者向け・サーバーでは実行しない）

`.github/workflows/release-api.yml`は`api-vX.Y.Z`タグpushだけで起動する。対象コミットがmainの履歴上にあり、**同じSHAに対するTestsのmain push実行が成功済み**の場合だけ先へ進む。PR/forkの成功や別SHAの結果は採用しない。mainのCI完了前にタグを付けた場合は、成功後に失敗ワークフローを再実行する。

```bash
# 開発用PCで実行。CIが成功したmainのコミットを明示して選ぶ。
git fetch origin main
git tag api-v1.2.3 REPLACE_VERIFIED_MAIN_COMMIT
git push origin api-v1.2.3
```

amd64/arm64の各ネイティブrunnerでAPI/ブラウザースクリプトのテスト、Dockerビルドと通常/サーバー/配布アーカイブの起動・保全を検証する。各イメージをpush後、両方成功時だけ`ghcr.io/k-o-a-b-c/monetail-api:v1.2.3`のmulti-arch manifestとReleasesのアーカイブ・SHA256SUMSを公開する。`latest`は作らない。作業用`build-…`タグはサーバーの運用指定に使わない。

push後のdigestはローカルimage storeではなく、GHCRへ`docker buildx imagetools inspect`で照会する。単一manifestとprovenance付きOCI indexに対応し、固定digestの参照先がこのパッケージであること、各ジョブのlinux/amd64・linux/arm64、統合後の両platformを検証する。公開前の既存タグ確認はRegistry APIのHTTP 404かつ`MANIFEST_UNKNOWN`だけを未公開と扱い、既存タグ・認証・通信・その他の応答では停止する。

認証は`GITHUB_TOKEN`だけを使う。検証ジョブはcontents/actionsのread、イメージjobはcontents read/packages write、最終公開jobはcontents/packages writeで、checkoutへ認証を残さない。PRのCIでは公開しない。既存のバージョンタグは上書きしない。公開途中に失敗してvX.Y.Zのmanifestだけ存在する場合も同じバージョンへ再公開せず、原因を確認して新しいバージョンタグを作る。

初回のGHCRパッケージは、公開リポジトリに紐づけても自動でPublicになるとは限らない。GitHubの所有者のPackagesから`monetail-api`のPackage settingsを開き、リポジトリを確認して可視性を**Public**へ設定する。既存パッケージを使う場合はManage Actions accessで`k-o-a-b-c/monetail`へwrite権限を付与する。設定反映後、未ログイン環境で公開版をpullできることを確認する。実際の公開設定変更は管理者が行う。

Privateを選ぶ場合はサーバーでもGHCR認証が必要。専用アカウントのread:packages権限のPAT classic等を公式手順に従い非表示入力から`docker login ghcr.io --username REPLACE_USER --password-stdin`へ渡す。トークンをCompose・Git・コマンド引数・ログへ記載しない。この手順はPublicイメージの匿名pullを前提とする。

## 検証範囲と公式資料

自動検証はダミー設定と隔離DBを使う。既存の開発用構成に加え、ソースなしのアーカイブ内からimage専用Composeを起動し、初回再実行拒否、DB・DPキー・PFXの再作成前後の一致、停止バックアップと隔離復元を検証する。実際のGHCR公開・匿名pull・GitHubの可視性設定は、マージ後の初回リリースで確認する。実ARM64検証はタグ配布ワークフローで行い、未実行なら検証済みとしない。実際のTailscale参加・ServeのHTTPS証明書取得更新、サーバーのACL／再起動、実ブラウザーのパスキー、Windowsアプリとの実接続、実バックアップの復元は**実機で確認が必要**。採用した検証結果は[tests/README](https://github.com/k-o-a-b-c/monetail/blob/main/tests/README.md)に集約する。

- [Docker Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/)／[servicesのsecretsとファイル権限の制約](https://docs.docker.com/reference/compose-file/services/#secrets)
- [Tailscale Docker設定](https://tailscale.com/docs/features/containers/docker/docker-params)（別管理のhostネットワークコンテナ向け）
- [Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve)／[Serve CLI](https://tailscale.com/docs/reference/tailscale-cli/serve)／[HTTPS証明書](https://tailscale.com/docs/how-to/set-up-https-certificates)
- [Tailscaleアクセス制御](https://tailscale.com/docs/features/access-control)（別コンテナ側で管理）
- [ASP.NET Coreの転送ヘッダー](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer)／[Data Protectionの保存](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers)
- [OpenIddictの署名・暗号化証明書](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html#registering-a-certificate-recommended-for-production)／[SQLiteのバックアップ](https://www.sqlite.org/backup.html)

- [GitHub ActionsでDockerイメージを公開](https://docs.github.com/actions/use-cases-and-examples/publishing-packages/publishing-docker-images)／[GHCR認証・匿名pull](https://docs.github.com/packages/working-with-a-github-packages-registry/working-with-the-container-registry)／[パッケージ可視性・Actionsアクセス](https://docs.github.com/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility)
- [GitHubのARM runner](https://docs.github.com/actions/reference/runners/github-hosted-runners)／[Docker multi-platform](https://docs.docker.com/build/building/multi-platform/)
