#!/usr/bin/env bash
set -euo pipefail

# 作成したイメージを起動し、HTTP応答と実行ユーザーを確認する。
image_name="${1:-monetail-api:ci}"
container_id="$(docker run --detach --publish 127.0.0.1::8080 "$image_name")"
trap 'result=$?; if [ "$result" -ne 0 ]; then docker logs "$container_id" >&2; fi; docker rm --force "$container_id" >/dev/null; exit "$result"' EXIT

host_port="$(docker inspect --format '{{(index (index .NetworkSettings.Ports "8080/tcp") 0).HostPort}}' "$container_id")"

for attempt in {1..30}; do
    if response="$(curl --fail --silent --max-time 2 "http://127.0.0.1:$host_port/health")" && [ "$response" = "Healthy" ]; then
        container_user="$(docker exec "$container_id" id -u)"
        [ "$container_user" -ne 0 ]
        printf 'Docker /health: Healthy; non-root UID: %s\n' "$container_user"
        exit 0
    fi
    sleep 1
done

printf 'Docker /health did not become healthy.\n' >&2
exit 1
