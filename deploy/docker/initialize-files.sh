#!/usr/bin/env bash
set -euo pipefail
set +x
umask 077
# 初回専用。DB初期化・アカウント作成は行わず、既存データディレクトリを拒否する。
if [ "$#" -ne 2 ]; then
    printf 'Usage: bash %s /absolute/new/data-directory fixed-image\n' "$0" >&2; exit 1
fi
data_dir="$1"; image="$2"
[[ "$data_dir" =~ ^/[a-zA-Z0-9_./-]+$ ]] && [ "$data_dir" != / ] || exit 1
[[ "$image" =~ ^ghcr\.io/k-o-a-b-c/monetail-api(:v[0-9]+\.[0-9]+\.[0-9]+|@sha256:[0-9a-f]{64})$ ]] || exit 1
command -v openssl >/dev/null
# 呼び出し前にdocker pullで取得する。ここで別イメージを取得したりバージョンを変更しない。
api_ids="$(docker run --rm --pull never --entrypoint sh "$image" -c 'printf "%s:%s" "$(id -u)" "$(id -g)"')"
[[ "$api_ids" =~ ^[0-9]+:[0-9]+$ && "$api_ids" != 0:* ]] || exit 1
files="$(cd -- "$(dirname -- "$0")" && pwd)"
mkdir -m 711 -- "$data_dir" || { printf '既存または作成不可のデータ領域です。変更しません。\n' >&2; exit 1; }
mkdir -m 700 "$data_dir/db" "$data_dir/keys"
cp "$files/server.env.example" "$data_dir/server.env"
sed -i "s|^MONETAIL_DATA_DIR=.*|MONETAIL_DATA_DIR=$data_dir|; s|^MONETAIL_IMAGE=.*|MONETAIL_IMAGE=$image|" "$data_dir/server.env"
bash "$files/create-oauth-certificates.sh" "$data_dir/oauth"
# bind secretsのuid/gid/modeは使わず、実イメージの非rootユーザーへ所有権を合わせる。
docker run --rm --pull never --user 0 --entrypoint sh --mount "type=bind,src=$data_dir,dst=/data" \
    "$image" -c 'chown -R "$1" /data/db /data/keys /data/oauth' sh "$api_ids"
printf '初回用のファイルを作成しました。server.envを設定後、DB初期化を明示実行してください。\n'
