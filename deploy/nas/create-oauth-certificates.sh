#!/usr/bin/env bash
set -euo pipefail
# 秘密値をトレースしない。既存ディレクトリには一切書き込まない。
set +x
umask 077
if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
    printf 'Usage: bash %s /absolute/new/oauth-directory [validity-days]\n' "$0" >&2
    exit 1
fi
output_dir="$1"
validity_days="${2:-365}"
[[ "$output_dir" = /* && "$validity_days" =~ ^[1-9][0-9]*$ ]] || {
    printf '絶対パスと正の有効日数を指定してください。\n' >&2
    exit 1
}
command -v openssl >/dev/null
# mkdirの排他性を使い、再実行・同時実行・既存シンボリックリンクを拒否する。
if ! mkdir -m 700 -- "$output_dir"; then
    printf '出力先が既存または作成不可です。既存の証明書は変更しません。\n' >&2
    exit 1
fi
temporary_dir="$(mktemp -d "$output_dir/.working.XXXXXXXX")"
trap 'rm -rf -- "$temporary_dir"' EXIT
for purpose in signing encryption; do
    openssl rand -hex 32 > "$temporary_dir/$purpose.password"
    usage=digitalSignature
    [ "$purpose" != encryption ] || usage=keyEncipherment
    openssl req -x509 -newkey rsa:3072 -nodes -sha256 -days "$validity_days" \
        -subj "/CN=monetail-oauth-$purpose" -addext "keyUsage=critical,$usage" \
        -keyout "$temporary_dir/$purpose.key" -out "$temporary_dir/$purpose.crt" >/dev/null 2>&1
    openssl pkcs12 -export -inkey "$temporary_dir/$purpose.key" \
        -in "$temporary_dir/$purpose.crt" -out "$output_dir/$purpose.pfx" \
        -passout "file:$temporary_dir/$purpose.password"
done
# 乱数は16進数だけなので、JSONの追加エスケープは不要。
printf '{"DesktopOAuth":{"SigningCertificatePassword":"%s","EncryptionCertificatePassword":"%s"}}\n' \
    "$(cat "$temporary_dir/signing.password")" "$(cat "$temporary_dir/encryption.password")" \
    > "$output_dir/appsettings.Production.json"
chmod 600 "$output_dir/"*.pfx "$output_dir/appsettings.Production.json"
printf 'OAuth用PFXと読込設定を作成しました。有効日数: %s。秘密値は表示しません。\n' "$validity_days"
