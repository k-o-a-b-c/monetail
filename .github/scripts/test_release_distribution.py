"""配布前の検証根拠と固定バージョン以外の初期化拒否を確認する。"""
import copy
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('release_gate', ROOT / 'deploy/nas/verify-release.py')
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class ReleaseDistributionTests(unittest.TestCase):
    sha = 'a' * 40

    def run_record(self):
        return dict(head_sha=self.sha, head_branch='main', event='push', conclusion='success',
                    head_repository={'full_name': 'k-o-a-b-c/monetail'})

    def test_exact_main_push_success_accepts_fixed_version(self):
        self.assertEqual('v1.2.3', gate.verified_version('api-v1.2.3', self.sha, {'workflow_runs': [self.run_record()]}))

    def test_pr_fork_wrong_branch_commit_and_non_success_are_not_release_proof(self):
        for field, value in [('event', 'pull_request'), ('head_branch', 'feature'),
                             ('head_sha', 'b' * 40), ('conclusion', 'failure'), ('conclusion', 'cancelled'),
                             ('conclusion', None), ('head_repository', {'full_name': 'fork/monetail'})]:
            with self.subTest(field=field, value=value):
                record = copy.deepcopy(self.run_record()); record[field] = value
                with self.assertRaises(ValueError):
                    gate.verified_version('api-v1.2.3', self.sha, {'workflow_runs': [record]})
        with self.assertRaises(ValueError):
            gate.verified_version('api-v1.2.3', self.sha, {'workflow_runs': []})

    def test_moving_or_malformed_versions_are_rejected(self):
        for tag in ['latest', 'api-latest', 'v1.2.3', 'api-v01.2.3', 'api-v1.2', 'api-v1.2.3;echo unsafe']:
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                gate.verified_version(tag, self.sha, {'workflow_runs': [self.run_record()]})

    def test_initial_files_reject_unpinned_or_wrong_images_before_creating_data(self):
        with tempfile.TemporaryDirectory() as work:
            data = Path(work) / 'data'
            for image in ['ghcr.io/k-o-a-b-c/monetail-api:latest', 'ghcr.io/k-o-a-b-c/monetail-api',
                          'wrong.example/monetail-api:v1.2.3', 'ghcr.io/k-o-a-b-c/monetail-api@sha256:bad']:
                result = subprocess.run(['bash', str(ROOT / 'deploy/nas/initialize-files.sh'), str(data), image],
                                        capture_output=True, text=True)
                self.assertNotEqual(0, result.returncode)
                self.assertFalse(data.exists())
