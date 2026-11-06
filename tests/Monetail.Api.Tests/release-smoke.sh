#!/usr/bin/env bash
set -euo pipefail
set +x
umask 077
image="${1:-monetail-api:ci}"
root="$(pwd)"
work="$(mktemp -d)"
tag="ghcr.io/k-o-a-b-c/monetail-api:v0.0.$$"
cleanup() {
    status=$?
    docker run --rm --user 0 --entrypoint sh --mount "type=bind,src=$work,dst=/test" "$image" -c 'rm -rf /test/*' >/dev/null 2>&1 || true
    docker image rm "$tag" >/dev/null 2>&1 || true
    rm -rf -- "$work"
    exit "$status"
}
trap cleanup EXIT
chmod 711 "$work"
bash deploy/nas/package.sh api-v1.2.3 "ghcr.io/k-o-a-b-c/monetail-api@sha256:$(printf '1%.0s' {1..64})" "$work/artifacts" >/dev/null
(cd "$work/artifacts" && sha256sum --check --status SHA256SUMS)
tar -xzf "$work/artifacts/monetail-nas-api-v1.2.3.tar.gz" -C "$work"
package="$work/monetail-nas-api-v1.2.3"
python3 - "$package" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1])
expected={'README.md','VERSION','images.env','compose.yaml','docs/deployment-docker-nas.md',
          'deploy/nas/compose.nas.yaml','deploy/nas/nas.env.example',
          'deploy/nas/create-oauth-certificates.sh','deploy/nas/initialize-files.sh'}
assert {str(p.relative_to(root)) for p in root.rglob('*') if p.is_file()}==expected
assert 'ghcr.io/k-o-a-b-c/monetail-api@sha256:' in (root/'images.env').read_text()
PY
# 公開レジストリへは接続・pushせず、同じ検証イメージへローカルで固定タグを付ける。
docker tag "$image" "$tag"
bash "$package/deploy/nas/initialize-files.sh" "$work/data" "$tag" >/dev/null
test ! -e "$work/data/tailscale"
docker run --rm --user 0 --entrypoint bash --mount "type=bind,src=$work,dst=/test" "$image" -c 'cd /test; sha256sum data/oauth/* > files-before.sha256'
if bash "$package/deploy/nas/initialize-files.sh" "$work/data" "$tag" >/dev/null 2>&1; then exit 1; fi
docker run --rm --user 0 --entrypoint bash --mount "type=bind,src=$work,dst=/test,readonly" "$image" -c 'cd /test; sha256sum --check --status files-before.sha256'
# ソースのないアーカイブ内をカレントにして起動し、buildが不要なことも検証する。
(cd "$package" && bash "$root/tests/Monetail.Api.Tests/nas-smoke.sh" "$image" "$package")
printf 'Release smoke: source-free archive, initialization refusal, image-only Compose, update and backup/restore preservation passed.\n'
