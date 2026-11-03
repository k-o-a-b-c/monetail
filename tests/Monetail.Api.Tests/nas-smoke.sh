#!/usr/bin/env bash
set -euo pipefail
set +x
umask 077
# 実tailnetへは接続せず、NAS設定・鍵の保全・非root起動と1段の転送を検証する。
image_name="${1:-monetail-api:ci}"
test_dir="$(mktemp -d)"
project="monetail-nas-check-$$"
compose=(docker compose --project-name "$project" --env-file "$test_dir/nas.env"
    -f compose.yaml -f deploy/nas/compose.nas.yaml -f "$test_dir/test.yaml")
cleanup() {
    result=$?
    "${compose[@]}" down --remove-orphans >/dev/null 2>&1 || true
    docker run --rm --user 0 --entrypoint sh --mount "type=bind,src=$test_dir,dst=/cleanup" \
        "$image_name" -c 'rm -rf /cleanup/*' >/dev/null 2>&1 || true
    rm -rf -- "$test_dir"
    exit "$result"
}
trap cleanup EXIT
mkdir -p "$test_dir/db" "$test_dir/keys" "$test_dir/tailscale/config" "$test_dir/tailscale/state"
printf 'synthetic-unused-authkey\n' > "$test_dir/tailscale/authkey"
cp deploy/nas/serve.json "$test_dir/tailscale/config/serve.json"
bash deploy/nas/create-oauth-certificates.sh "$test_dir/oauth" 1 >/dev/null
sha256sum "$test_dir/oauth/"* > "$test_dir/before.sha256"
if bash deploy/nas/create-oauth-certificates.sh "$test_dir/oauth" 1 >/dev/null 2>&1; then
    printf '既存証明書への再実行を拒否しませんでした。\n' >&2
    exit 1
fi
sha256sum --check --status "$test_dir/before.sha256"
cat > "$test_dir/nas.env" <<EOF
MONETAIL_DATA_DIR=$test_dir
MONETAIL_ORIGIN=https://monetail-api.tail-example.ts.net
MONETAIL_RP_ID=monetail-api.tail-example.ts.net
TAILSCALE_HOSTNAME=monetail-api
TAILSCALE_IMAGE=tailscale/tailscale:v1.104.4
EOF
cat > "$test_dir/test.yaml" <<EOF
services:
  api:
    image: $image_name
    ports: !override
      - "127.0.0.1::8080"
EOF
"${compose[@]}" config --format json > "$test_dir/compose.json"
python3 - "$test_dir/compose.json" <<'PY'
import json,sys
config=json.load(open(sys.argv[1]))
api=config['services']['api']; ts=config['services']['tailscale']
assert ts['network_mode']=='service:api'
assert api['environment']['Passkeys__TrustedProxies__0']=='127.0.0.1'
assert api['ports'][0]['host_ip']=='127.0.0.1'
assert ts['environment']['TS_AUTHKEY']=='file:/run/secrets/tailscale-authkey'
assert any(s['target']=='/app/appsettings.Production.json' for s in api['secrets'])
assert not any('Password' in name for name in api['environment'])
PY
# bind secretsのuid/gid/mode指定に頼らず、実イメージの実行ユーザーへ所有権を渡す。
api_ids="$(docker run --rm --entrypoint sh "$image_name" -c 'printf "%s:%s" "$(id -u)" "$(id -g)"')"
docker run --rm --user 0 --entrypoint sh --mount "type=bind,src=$test_dir,dst=/test" \
    "$image_name" -c 'chown -R "$1" /test/db /test/keys /test/oauth; chmod 711 /test' sh "$api_ids"
"${compose[@]}" run --rm --no-deps api initialize-db > "$test_dir/initialize.log" 2>&1
"${compose[@]}" run --rm --no-deps api initialize-admin > "$test_dir/admin-private.log" 2>&1
"${compose[@]}" up -d --no-deps --no-build api >/dev/null
container_id="$("${compose[@]}" ps -q api)"
port="$(docker inspect --format '{{(index (index .NetworkSettings.Ports "8080/tcp") 0).HostPort}}' "$container_id")"
healthy=false
for attempt in {1..30}; do
    if [ "$(curl --silent --max-time 2 "http://127.0.0.1:$port/health" || true)" = Healthy ]; then
        healthy=true
        break
    fi
    sleep 1
done
[ "$healthy" = true ] || { printf 'NAS構成での起動に失敗しました。\n' >&2; exit 1; }
[ "$(docker exec "$container_id" id -u)" -ne 0 ]
# Serveと同じネットワーク名前空間・IPv4ループバックから転送する。
for forwarded_host in monetail-api.tail-example.ts.net wrong.example.test; do
    response="$(docker exec "$container_id" bash -c '
        exec 3<>/dev/tcp/127.0.0.1/8080
        printf "GET /auth/enroll HTTP/1.1\r\nHost: 127.0.0.1:8080\r\nX-Forwarded-Host: %s\r\nX-Forwarded-Proto: https\r\nX-Forwarded-For: 192.0.2.10\r\nConnection: close\r\n\r\n" "$1" >&3
        cat <&3' bash "$forwarded_host")"
    if [ "$forwarded_host" = monetail-api.tail-example.ts.net ]; then
        [[ "$response" = 'HTTP/1.1 200 '* ]]
    else
        [[ "$response" = 'HTTP/1.1 400 '* ]]
    fi
done
"${compose[@]}" restart api >/dev/null
docker run --rm --user 0 --entrypoint sha256sum \
    --mount "type=bind,src=$test_dir,dst=$test_dir,readonly" \
    "$image_name" --check --status "$test_dir/before.sha256"
"${compose[@]}" stop api >/dev/null
"${compose[@]}" run --rm --no-deps api initialize-admin > "$test_dir/admin-after-restart-private.log" 2>&1
python3 - "$test_dir/admin-after-restart-private.log" <<'PY'
from pathlib import Path
import sys
assert '初期管理者は作成済みです' in Path(sys.argv[1]).read_text()
PY
printf 'NAS smoke: Compose secrets, PFX/DB preservation, DB/admin initialization, non-root startup, forwarded HTTPS/host boundary passed; real Tailscale not tested.\n'
