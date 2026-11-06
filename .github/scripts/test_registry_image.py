"""配布済みmanifestの検証と、未公開・障害の区別を確認する。"""
import io
import json
import subprocess
import unittest
import urllib.error
from unittest.mock import patch, Mock

import registry_image as registry


class RegistryImageTests(unittest.TestCase):
    digest = 'sha256:' + 'a' * 64
    reference = registry.IMAGE + ':build-test'

    def manifest(self):
        return {'digest': self.digest, 'mediaType': next(iter(registry.MANIFEST_TYPES))}

    def child(self, arch):
        return {'digest': 'sha256:' + 'b' * 64, 'platform': {'os': 'linux', 'architecture': arch}}

    def index(self, *arches):
        return dict(self.manifest(), mediaType=next(iter(registry.INDEX_TYPES)),
                    manifests=[self.child(arch) for arch in arches])

    def test_single_manifest_uses_remote_digest_and_pinned_config_for_both_architectures(self):
        for arch in ['amd64', 'arm64']:
            with self.subTest(arch=arch), patch.object(registry, 'inspect', side_effect=[
                    self.manifest(), {'os': 'linux', 'architecture': arch}]) as inspect:
                pinned = registry.IMAGE + '@' + self.digest
                self.assertEqual(pinned, registry.resolve(self.reference, [arch]))
                self.assertEqual((pinned, 'Image'), inspect.call_args.args)

    def test_buildkit_index_with_attestation_and_final_multiarch_index(self):
        for arches in [('amd64',), ('arm64',), ('amd64', 'arm64')]:
            manifest = self.index(*arches)
            manifest['manifests'].append(dict(self.child('unknown'),
                platform={'os': 'unknown', 'architecture': 'unknown'},
                annotations={'vnd.docker.reference.type': 'attestation-manifest'}))
            with self.subTest(arches=arches), patch.object(registry, 'inspect', return_value=manifest):
                self.assertEqual(registry.IMAGE + '@' + self.digest, registry.resolve(self.reference, arches))

    def test_wrong_repository_malformed_digest_or_reference_rejected_before_inspection(self):
        for ref in ['other.example/image:v1', registry.IMAGE + '-other:v1',
                    registry.IMAGE + '@sha256:bad', registry.IMAGE + ':x/path',
                    registry.IMAGE + ':v1\n', registry.IMAGE + '@sha256:' + 'A' * 64]:
            with self.subTest(ref=ref), patch.object(registry, 'inspect') as inspect:
                with self.assertRaises(ValueError):
                    registry.resolve(ref, ['amd64'])
                inspect.assert_not_called()

    def test_invalid_or_different_remote_digest_is_rejected(self):
        for digest in ['', 'sha256:bad', 'sha512:' + 'a' * 64, 'sha256:' + 'A' * 64,
                       'sha256:' + 'b' * 64]:
            manifest = dict(self.manifest(), digest=digest)
            with self.subTest(digest=digest), patch.object(registry, 'inspect', return_value=manifest):
                with self.assertRaises(ValueError):
                    registry.resolve(registry.IMAGE + '@' + self.digest, ['amd64'])

    def test_missing_wrong_duplicate_or_extra_platform_is_rejected(self):
        manifests = [self.index(), self.index('arm64'), self.index('amd64', 'amd64'),
                     self.index('amd64', 'arm64')]
        wrong_os = self.index('amd64'); wrong_os['manifests'][0]['platform']['os'] = 'windows'
        unknown = self.index('unknown')
        invalid_child = self.index('amd64'); invalid_child['manifests'][0]['digest'] = 'bad'
        manifests += [wrong_os, unknown, invalid_child, dict(self.manifest(), mediaType='unsupported')]
        for manifest in manifests:
            with self.subTest(manifest=manifest), patch.object(registry, 'inspect', return_value=manifest):
                with self.assertRaises(ValueError):
                    registry.resolve(self.reference, ['amd64'])
        with patch.object(registry, 'inspect', return_value=self.index('amd64')):
            with self.assertRaises(ValueError):
                registry.resolve(self.reference, ['amd64', 'arm64'])

    def test_single_manifest_wrong_architecture_or_os_is_rejected(self):
        for config in [{'os': 'linux', 'architecture': 'arm64'},
                       {'os': 'windows', 'architecture': 'amd64'}]:
            with self.subTest(config=config), patch.object(registry, 'inspect',
                    side_effect=[self.manifest(), config]), self.assertRaises(ValueError):
                registry.resolve(self.reference, ['amd64'])

    def test_inspect_uses_remote_buildx_and_propagates_failure(self):
        with patch.object(subprocess, 'run', return_value=Mock(stdout='{"digest":"test"}')) as run:
            self.assertEqual({'digest': 'test'}, registry.inspect(self.reference, 'Manifest'))
            self.assertEqual(['docker', 'buildx', 'imagetools', 'inspect', self.reference,
                              '--format', '{{json .Manifest}}'], run.call_args.args[0])
        with patch.object(subprocess, 'run', side_effect=subprocess.CalledProcessError(1, 'docker')):
            with self.assertRaises(subprocess.CalledProcessError):
                registry.resolve(self.reference, ['amd64'])

    def check_absence(self, response):
        opener = Mock()
        opener.open.side_effect = [io.BytesIO(b'{"token":"test-only"}'), response]
        with patch.object(registry.urllib.request, 'build_opener', return_value=opener):
            registry.ensure_absent(self.reference)

    def http_error(self, status, code='MANIFEST_UNKNOWN'):
        return urllib.error.HTTPError('https://ghcr.io/test', status, 'test', {},
            io.BytesIO(json.dumps({'errors': [{'code': code}]}).encode()))

    def test_only_registry_manifest_unknown_404_allows_publication(self):
        self.check_absence(self.http_error(404))

    def test_existing_tag_auth_network_and_other_http_failures_block_publication(self):
        for response in [io.BytesIO(b'{}'), self.http_error(401), self.http_error(403),
                         self.http_error(429), self.http_error(500), self.http_error(302),
                         self.http_error(404, 'UNAUTHORIZED'), self.http_error(404, 'NAME_UNKNOWN'),
                         urllib.error.URLError('test connection failure'), TimeoutError('test timeout')]:
            with self.subTest(response=response), self.assertRaises((ValueError, OSError)):
                self.check_absence(response)
        for body in [b'not a registry response', b'{}', b'{"errors":[]}']:
            with self.subTest(body=body), self.assertRaises(ValueError):
                self.check_absence(urllib.error.HTTPError('https://ghcr.io/test', 404, 'test', {}, io.BytesIO(body)))

    def test_token_endpoint_failure_does_not_probe_manifest(self):
        opener = Mock(); opener.open.side_effect = self.http_error(403)
        with patch.object(registry.urllib.request, 'build_opener', return_value=opener):
            with self.assertRaises(urllib.error.HTTPError):
                registry.ensure_absent(self.reference)
        self.assertEqual(1, opener.open.call_count)

    def test_credentials_only_go_to_fixed_token_endpoint_and_redirects_are_rejected(self):
        opener = Mock()
        opener.open.side_effect = [io.BytesIO(b'{"token":"test-only"}'), self.http_error(404)]
        with patch.dict(registry.os.environ, {'GHCR_TOKEN': 'dummy', 'GITHUB_ACTOR': 'dummy'}), \
                patch.object(registry.urllib.request, 'build_opener', return_value=opener):
            registry.ensure_absent(self.reference)
        token_request, manifest_request = [call.args[0] for call in opener.open.call_args_list]
        self.assertTrue(token_request.full_url.startswith('https://ghcr.io/token?'))
        self.assertTrue(token_request.get_header('Authorization').startswith('Basic '))
        self.assertEqual('Bearer test-only', manifest_request.get_header('Authorization'))
        self.assertIsNone(registry.NoRedirect().redirect_request(None, None, 302, '', {}, 'https://other.example'))

    def test_invalid_repository_is_rejected_before_authentication(self):
        with patch.object(registry.urllib.request, 'build_opener') as opener:
            with self.assertRaises(ValueError):
                registry.ensure_absent('ghcr.io/other/image:v1')
            opener.assert_not_called()

    def test_workflow_uses_one_resolver_for_both_architectures_and_final_manifest(self):
        from pathlib import Path
        workflow = (Path(__file__).resolve().parents[1] / 'workflows/release-api.yml').read_text()
        self.assertNotIn('RepoDigests', workflow)
        self.assertIn('registry_image.py resolve "$tag" "$ARCH"', workflow)
        self.assertIn('registry_image.py resolve "$digest" "$arch"', workflow)
        self.assertIn('registry_image.py resolve "$IMAGE:$VERSION" amd64 arm64', workflow)
        self.assertIn('registry_image.py ensure-absent "$IMAGE:$VERSION"', workflow)
