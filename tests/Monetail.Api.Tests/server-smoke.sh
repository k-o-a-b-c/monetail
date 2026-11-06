#!/usr/bin/env bash
set -euo pipefail
set +x
umask 077
# 既存の外部プロキシは起動せず、host待受設定と安全な隔離ネットワークでのAPI起動を検証する。
image_name="${1:-monetail-api:ci}"
test_dir="$(mktemp -d)"
project="monetail-server-check-$$"
package="${2:-}"
docker_files="${package:+$package/}deploy/docker"
base_compose="${package:+$package/}compose.yaml"
compose=(docker compose --project-name "$project" --env-file "$test_dir/server.env"
    -f "$base_compose" -f "$docker_files/compose.server.yaml" -f "$test_dir/test.yaml")
cleanup() {
    result=$?
    "${compose[@]}" down --remove-orphans >/dev/null 2>&1 || true
    docker run --rm --user 0 --entrypoint sh --mount "type=bind,src=$test_dir,dst=/cleanup" \
        "$image_name" -c 'rm -rf /cleanup/*' >/dev/null 2>&1 || true
    rm -rf -- "$test_dir"
    exit "$result"
}
trap cleanup EXIT
mkdir -p "$test_dir/db" "$test_dir/keys"
bash "$docker_files/create-oauth-certificates.sh" "$test_dir/oauth" 1 >/dev/null
sha256sum "$test_dir/oauth/"* > "$test_dir/before.sha256"
if bash "$docker_files/create-oauth-certificates.sh" "$test_dir/oauth" 1 >/dev/null 2>&1; then
    printf '既存証明書への再実行を拒否しませんでした。\n' >&2
    exit 1
fi
sha256sum --check --status "$test_dir/before.sha256"
cat > "$test_dir/server.env" <<EOF
MONETAIL_IMAGE=$image_name
MONETAIL_DATA_DIR=$test_dir
MONETAIL_ORIGIN=https://monetail.example.test
MONETAIL_RP_ID=monetail.example.test
EOF
cat > "$test_dir/test.yaml" <<EOF
services:
  api:
    image: $image_name
    # CI内では実ホストの8080番を使わず、独立bridgeとランダムportへ置換する。
    network_mode: bridge
    environment:
      ASPNETCORE_URLS: http://0.0.0.0:8080
    ports: !override
      - "127.0.0.1::8080"
EOF
docker compose --project-name "$project" --env-file "$test_dir/server.env" \
    -f "$base_compose" -f "$docker_files/compose.server.yaml" \
    config --format json > "$test_dir/production-compose.json"
"${compose[@]}" config --format json > "$test_dir/compose.json"
python3 - "$test_dir/production-compose.json" "$test_dir/compose.json" "$package" <<'PY'
import json,sys
production=json.load(open(sys.argv[1]))
assert set(production['services'])=={'api'}
prod=production['services']['api']
assert prod['network_mode']=='host'
assert not prod.get('ports')
assert prod['environment']['ASPNETCORE_URLS']=='http://127.0.0.1:8080'
assert prod['environment']['Passkeys__TrustedProxies__0']=='127.0.0.1'
config=json.load(open(sys.argv[2]))
assert set(config['services'])=={'api'}
api=config['services']['api']
if sys.argv[3]: assert 'build' not in api
assert api['network_mode']=='bridge'
assert api['environment']['ASPNETCORE_URLS']=='http://0.0.0.0:8080'
assert api['environment']['Passkeys__TrustedProxies__0']=='127.0.0.1'
assert api['ports'][0]['host_ip']=='127.0.0.1'
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
wait_healthy() {
    for attempt in {1..30}; do
        if [ "$(curl --silent --max-time 2 "http://127.0.0.1:$port/health" || true)" = Healthy ]; then return; fi
        sleep 1
    done
    printf 'サーバー構成での起動に失敗しました。\n' >&2; return 1
}
wait_healthy
[ "$(docker exec "$container_id" id -u)" -ne 0 ]
# IPv4ループバックからのHTTPS転送ヘッダーを模擬する（実プロキシは対象外）。
for forwarded_host in monetail.example.test wrong.example.test; do
    response="$(docker exec "$container_id" bash -c '
        exec 3<>/dev/tcp/127.0.0.1/8080
        printf "GET /auth/enroll HTTP/1.1\r\nHost: 127.0.0.1:8080\r\nX-Forwarded-Host: %s\r\nX-Forwarded-Proto: https\r\nX-Forwarded-For: 192.0.2.10\r\nConnection: close\r\n\r\n" "$1" >&3
        cat <&3' bash "$forwarded_host")"
    if [ "$forwarded_host" = monetail.example.test ]; then
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
if [ -n "$package" ]; then
    docker run --rm --user 0 --entrypoint bash --mount "type=bind,src=$test_dir,dst=/test" "$image_name" -c '
        cd /test
        sha256sum db/monetail.db keys/* oauth/* > data-before.sha256
        tar -cf backup.tar db keys oauth server.env
        mkdir restored
        tar -xf backup.tar -C restored
        cd restored
        sha256sum --check --status ../data-before.sha256'
    # イメージ差替えと同じコンテナ再作成でも、DB・DPキー・PFX・stateを初期化しない。
    "${compose[@]}" up -d --no-deps --no-build --force-recreate api >/dev/null
    container_id="$("${compose[@]}" ps -q api)"
    port="$(docker inspect --format '{{(index (index .NetworkSettings.Ports "8080/tcp") 0).HostPort}}' "$container_id")"
    wait_healthy
    "${compose[@]}" stop api >/dev/null
    MONETAIL_DATA_DIR="$test_dir/restored" "${compose[@]}" run --rm --no-deps api initialize-admin > "$test_dir/restored-admin-private.log" 2>&1
    python3 - "$test_dir/restored-admin-private.log" <<'PY'
from pathlib import Path
import sys
assert '初期管理者は作成済みです' in Path(sys.argv[1]).read_text()
PY
    docker run --rm --user 0 --entrypoint bash --mount "type=bind,src=$test_dir,dst=/test,readonly" "$image_name" -c '
        cd /test; sha256sum --check --status data-before.sha256
        cd restored; sha256sum --check --status ../data-before.sha256'
fi
printf 'Server smoke: externally managed proxy, host-only loopback configuration, secrets, PFX/DB preservation, non-root startup and HTTPS forwarded host boundary passed.\n'
