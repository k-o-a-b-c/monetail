"""GHCR上の配布先・digest・platformを検証する。ローカルimage storeは参照しない。"""
import argparse
import base64
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request

IMAGE = 'ghcr.io/k-o-a-b-c/monetail-api'
DIGEST = r'sha256:[0-9a-f]{64}'
INDEX_TYPES = {'application/vnd.oci.image.index.v1+json',
               'application/vnd.docker.distribution.manifest.list.v2+json'}
MANIFEST_TYPES = {'application/vnd.oci.image.manifest.v1+json',
                  'application/vnd.docker.distribution.manifest.v2+json'}


class RegistryError(ValueError):
    pass


def reference_suffix(reference):
    if reference.startswith(IMAGE + '@'):
        suffix = reference[len(IMAGE) + 1:]
        if re.fullmatch(DIGEST, suffix):
            return suffix
    elif reference.startswith(IMAGE + ':'):
        suffix = reference[len(IMAGE) + 1:]
        if re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}', suffix):
            return suffix
    raise RegistryError('配布先またはイメージ参照が不正です。')


def inspect(reference, field):
    result = subprocess.run(['docker', 'buildx', 'imagetools', 'inspect', reference,
                             '--format', '{{json .' + field + '}}'],
                            capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def resolve(reference, architectures):
    suffix = reference_suffix(reference)
    expected = set(architectures)
    if not expected or not expected <= {'amd64', 'arm64'}:
        raise RegistryError('検証対象のarchitectureが不正です。')
    manifest = inspect(reference, 'Manifest')
    digest = manifest.get('digest', '')
    if not re.fullmatch(DIGEST, digest) or ('@' in reference and digest != suffix):
        raise RegistryError('GHCRのdigestが不正または指定digestと不一致です。')
    pinned = IMAGE + '@' + digest
    if manifest.get('mediaType') in INDEX_TYPES:
        platforms = []
        for child in manifest.get('manifests', []):
            if not re.fullmatch(DIGEST, child.get('digest', '')):
                raise RegistryError('子manifestのdigestが不正です。')
            platform = child.get('platform', {})
            # BuildKitのprovenanceは実行用platformとして数えない。
            if (platform == {'os': 'unknown', 'architecture': 'unknown'} and
                    child.get('annotations', {}).get('vnd.docker.reference.type') == 'attestation-manifest'):
                continue
            platforms.append((platform.get('os'), platform.get('architecture')))
    elif manifest.get('mediaType') in MANIFEST_TYPES:
        # single manifestのplatformはタグではなく確定済みdigestのconfigから読む。
        config = inspect(pinned, 'Image')
        platforms = [(config.get('os'), config.get('architecture'))]
    else:
        raise RegistryError('未対応のmanifest形式です。')
    if sorted(platforms, key=str) != sorted([('linux', arch) for arch in expected]):
        raise RegistryError('GHCRのplatformが検証対象と一致しません。')
    return pinned


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def ensure_absent(reference):
    suffix = reference_suffix(reference)
    if '@' in reference:
        raise RegistryError('既存バージョン確認にはタグを指定してください。')
    # 固定ホストだけに認証を送信し、redirectでは転送しない。
    opener = urllib.request.build_opener(NoRedirect())
    request = urllib.request.Request(
        'https://ghcr.io/token?service=ghcr.io&scope=repository:k-o-a-b-c/monetail-api:pull')
    credential = os.environ.get('GHCR_TOKEN')
    if credential:
        basic = base64.b64encode((os.environ['GITHUB_ACTOR'] + ':' + credential).encode()).decode()
        request.add_header('Authorization', 'Basic ' + basic)
    with opener.open(request, timeout=30) as response:
        token = json.load(response)['token']
    request = urllib.request.Request(
        'https://ghcr.io/v2/k-o-a-b-c/monetail-api/manifests/' + suffix,
        headers={'Authorization': 'Bearer ' + token,
                 'Accept': ', '.join(sorted(INDEX_TYPES | MANIFEST_TYPES))})
    try:
        with opener.open(request, timeout=30):
            raise RegistryError('既存バージョンは上書きできません。新しいタグを作成してください。')
    except urllib.error.HTTPError as error:
        # 認証・通信障害や、一般的な404ページを未公開と誤判定しない。
        if error.code == 404:
            payload = json.load(error)
            errors = payload.get('errors', [])
            if errors and all(item.get('code') == 'MANIFEST_UNKNOWN' for item in errors):
                return
        raise RegistryError(f'既存バージョン確認に失敗しました（HTTP {error.code}）。') from None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['resolve', 'ensure-absent'])
    parser.add_argument('reference')
    parser.add_argument('architectures', nargs='*', choices=['amd64', 'arm64'])
    args = parser.parse_args()
    try:
        if args.operation == 'resolve':
            print(resolve(args.reference, args.architectures))
        else:
            ensure_absent(args.reference)
    except urllib.error.HTTPError as error:
        print(f'GHCR検証失敗: HTTP {error.code}', file=sys.stderr)
        return 1
    except (ValueError, KeyError, TypeError, OSError, subprocess.CalledProcessError) as error:
        # HTTPの応答本文・コマンドstderr・認証情報はログへ出さない。
        detail = str(error) if isinstance(error, RegistryError) else type(error).__name__
        print(f'GHCR検証失敗: {detail}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
