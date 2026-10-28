#!/usr/bin/env bash
set -euo pipefail

# 非root起動と、.NETが解決した既定地域・DB未接続での稼働確認を検証する。
image_name="${1:-monetail-api:ci}"
container_id=""
test_key_dir="$(mktemp -d)"
trap 'result=$?; if [ -n "$container_id" ]; then if [ "$result" -ne 0 ]; then docker logs "$container_id" >&2; fi; docker rm --force "$container_id" >/dev/null; fi; rm -rf "$test_key_dir"; exit "$result"' EXIT

# 隔離した確認用鍵を用意する。本番の証明書や秘密情報は使わない。
for purpose in signing encryption; do
    openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=synthetic-$purpose" \
        -keyout "$test_key_dir/$purpose.key" -out "$test_key_dir/$purpose.crt" >/dev/null 2>&1
    openssl pkcs12 -export -inkey "$test_key_dir/$purpose.key" -in "$test_key_dir/$purpose.crt" \
        -out "$test_key_dir/$purpose.pfx" -passout pass:
    chmod 444 "$test_key_dir/$purpose.pfx"
done
chmod 755 "$test_key_dir"

for test_zone in Etc/UTC America/New_York; do
    container_id="$(docker run --detach --publish 127.0.0.1::8080 \
        --env "TZ=$test_zone" \
        --env 'Passkeys__Origin=https://monetail.example.test' \
        --env 'Passkeys__RpId=monetail.example.test' \
        --env 'Passkeys__DataProtectionKeysPath=/tmp/monetail-test-keys' \
        --mount "type=bind,src=$test_key_dir,dst=/test-oauth,readonly" \
        --env 'DesktopOAuth__SigningCertificatePath=/test-oauth/signing.pfx' \
        --env 'DesktopOAuth__EncryptionCertificatePath=/test-oauth/encryption.pfx' \
        --env 'ConnectionStrings__Monetail=Data Source=/tmp/unused-test.db' "$image_name")"
    host_port="$(docker inspect --format '{{(index (index .NetworkSettings.Ports "8080/tcp") 0).HostPort}}' "$container_id")"
    healthy=false
    for attempt in {1..30}; do
        if response="$(curl --fail --silent --max-time 2 "http://127.0.0.1:$host_port/health")" && [ "$response" = "Healthy" ]; then
            container_user="$(docker exec "$container_id" id -u)"
            [ "$container_user" -ne 0 ]
            startup_log="$(docker logs "$container_id" 2>&1)"
            [[ "$startup_log" == *"セッション期限の既定地域=$test_zone"* ]]
            docker exec "$container_id" sh -c 'test ! -e /tmp/unused-test.db'
            printf 'Docker /health: Healthy; non-root UID: %s; .NET timezone: %s; DB not created\n' "$container_user" "$test_zone"
            healthy=true
            break
        fi
        sleep 1
    done
    if [ "$healthy" != true ]; then
        printf 'Docker /health did not become healthy.\n' >&2
        exit 1
    fi
    docker rm --force "$container_id" >/dev/null
    container_id=""
done
