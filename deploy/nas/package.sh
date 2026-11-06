#!/usr/bin/env bash
set -euo pipefail
# 許可した配布ファイルだけを梱包する。作業ディレクトリの秘密ファイルやソースは含めない。
if [ "$#" -ne 3 ]; then
    printf 'Usage: bash %s api-vX.Y.Z ghcr-image@sha256:digest output-directory\n' "$0" >&2; exit 1
fi
version="$1"; image="$2"; output="$3"
[[ "$version" =~ ^api-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]] || exit 1
[[ "$image" =~ ^ghcr\.io/k-o-a-b-c/monetail-api@sha256:[0-9a-f]{64}$ ]] || exit 1
root="$(cd -- "$(dirname -- "$0")/../.." && pwd)"
mkdir -p -- "$output"
output="$(cd -- "$output" && pwd)"
name="monetail-nas-$version"
[ ! -e "$output/$name.tar.gz" ] && [ ! -e "$output/SHA256SUMS" ] || { printf '既存の配布物は上書きしません。\n' >&2; exit 1; }
staging="$(mktemp -d)"
trap 'rm -rf -- "$staging"' EXIT
mkdir -p "$staging/$name/deploy/nas" "$staging/$name/docs"
cp "$root/deploy/nas/compose.image.yaml" "$staging/$name/compose.yaml"
for file in compose.nas.yaml nas.env.example serve.json create-oauth-certificates.sh initialize-files.sh; do
    cp "$root/deploy/nas/$file" "$staging/$name/deploy/nas/$file"
done
cp "$root/docs/deployment-docker-nas.md" "$staging/$name/docs/"
printf 'MONETAIL_IMAGE=%s\n' "$image" > "$staging/$name/images.env"
printf '%s\n%s\n' "$version" "$image" > "$staging/$name/VERSION"
printf '# monetail API NAS package\n\n手順: [NASセットアップ](docs/deployment-docker-nas.md)\n\n固定イメージ: `%s`\n' "$image" > "$staging/$name/README.md"
tar --create --gzip --file "$output/$name.tar.gz" --directory "$staging" "$name"
(cd -- "$output" && sha256sum "$name.tar.gz" > SHA256SUMS)
printf '%s\n' "$output/$name.tar.gz"
