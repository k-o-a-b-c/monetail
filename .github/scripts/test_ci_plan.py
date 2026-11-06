"""変更範囲・成功記録・入力の変化が、検証の取り落としにつながらないことを確認する。"""

from contextlib import redirect_stdout
from io import StringIO
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from ci_plan import fingerprints, main, plan, proof, resolve, tracked_inputs
from select_tests import FLAGS


class FingerprintTests(unittest.TestCase):
    def setUp(self):
        self.records = {
            "README.md": "100644 blob docs",
            ".github/workflows/tests.yml": "100644 blob checks",
            ".github/workflows/winui.yml": "100644 blob ui-checks",
            "src/Monetail.Api/Program.cs": "100644 blob api",
            "tests/Monetail.Api.Tests/HealthTests.cs": "100644 blob api-test",
            "src/Monetail.Windows.Client/Authentication/BrowserOAuthLogin.cs": "100644 blob client",
            "src/Monetail.WinUI/MainWindow.xaml": "100644 blob ui",
        }

    def assert_changed(self, path, *expected):
        changed = dict(self.records)
        changed[path] = "100755 blob new-content"
        before, after = fingerprints(self.records), fingerprints(changed)
        self.assertEqual(set(expected), {flag for flag in FLAGS if before[flag] != after[flag]})

    def test_document_changes_keep_all_success_fingerprints(self):
        self.assert_changed("README.md")
        self.assert_changed("docs/new-document.md")
        without_docs = {path: value for path, value in self.records.items() if not path.endswith(".md")}
        self.assertEqual(fingerprints(self.records), fingerprints(without_docs))

    def test_ghcr_script_addition_changes_mode_and_removal_keep_product_fingerprints(self):
        before = fingerprints(self.records)
        for path in [".github/scripts/registry_image.py", ".github/scripts/test_registry_image.py"]:
            for identity in ["100644 blob initial", "100644 blob changed", "100755 blob changed"]:
                with self.subTest(path=path, identity=identity):
                    records = dict(self.records, **{path: identity})
                    self.assertEqual(before, fingerprints(records))
                    del records[path]
                    self.assertEqual(before, fingerprints(records))
        both = dict(self.records, **{".github/scripts/registry_image.py": "100644 blob ghcr",
                                    ".github/scripts/test_registry_image.py": "100644 blob ghcr-test"})
        self.assertEqual(before, fingerprints(both))

    def test_renaming_ghcr_script_outside_exception_invalidates_all_product_proofs(self):
        for path in [".github/scripts/registry_image.py", ".github/scripts/test_registry_image.py"]:
            with self.subTest(path=path):
                before = fingerprints(dict(self.records, **{path: "100644 blob same-content"}))
                renamed = dict(self.records, **{path.replace(".py", "_extra.py"): "100644 blob same-content"})
                after = fingerprints(renamed)
                self.assertEqual(set(FLAGS), {flag for flag in FLAGS if before[flag] != after[flag]})

    def test_ui_changes_do_not_invalidate_api_client_or_storage(self):
        self.assert_changed("src/Monetail.WinUI/MainWindow.xaml", "winui")
        self.assert_changed("tests/Monetail.WinUI.Smoke/InputScenario.cs", "winui")

    def test_client_changes_include_the_ui_and_real_windows_storage(self):
        self.assert_changed("src/Monetail.Windows.Client/Authentication/BrowserOAuthLogin.cs",
                            "windows_client", "windows_storage", "winui")

    def test_api_test_does_not_invalidate_the_container(self):
        self.assert_changed("tests/Monetail.Api.Tests/HealthTests.cs", "api")
        self.assert_changed("src/Monetail.Api/Program.cs", "api", "docker")

    def test_ci_definition_and_shared_settings_invalidate_their_checks(self):
        for path in [".github/workflows/tests.yml", ".github/scripts/ci_plan.py",
                     ".github/scripts/select_tests.py", ".github/scripts/test_ci_plan.py",
                     ".github/scripts/test_select_tests.py", ".github/scripts/future.py",
                     ".github/actions/ci-proof/action.yml", "Directory.Build.props"]:
            with self.subTest(path=path):
                self.assert_changed(path, *FLAGS)
        self.assert_changed(".github/workflows/winui.yml", "winui")

    def test_removals_renames_and_execution_mode_change_fingerprints(self):
        before = fingerprints(self.records)
        removed = dict(self.records)
        removed.pop("src/Monetail.WinUI/MainWindow.xaml")
        self.assertNotEqual(before["winui"], fingerprints(removed)["winui"])
        renamed = dict(removed)
        renamed["src/Monetail.WinUI/Home.xaml"] = self.records["src/Monetail.WinUI/MainWindow.xaml"]
        self.assertNotEqual(before["winui"], fingerprints(renamed)["winui"])

    def test_record_order_does_not_affect_fingerprints(self):
        self.assertEqual(fingerprints(self.records), fingerprints(dict(reversed(list(self.records.items())))))

    def test_existing_unknown_input_is_conservatively_included(self):
        self.assert_changed("existing-unclassified.json", *FLAGS)

    def test_checkout_uses_blob_identity_including_unicode_and_tabs(self):
        with patch("ci_plan.git", return_value=b"100644 blob abc\tname\tpart.cs\0" +
                   "100755 blob def\t説明.txt\0".encode()):
            self.assertEqual({"name\tpart.cs": "100644 blob abc", "説明.txt": "100755 blob def"}, tracked_inputs())


class PlanningTests(unittest.TestCase):
    def setUp(self):
        self.records = {"src/Monetail.Api/Program.cs": "100644 blob api", "src/Monetail.WinUI/MainWindow.xaml": "100644 blob ui"}
        self.all_required = {"required": dict.fromkeys(FLAGS, True), "fingerprints": fingerprints(self.records)}

    def test_pr_keeps_the_full_diff_even_for_a_document_only_last_commit(self):
        with patch("ci_plan.changed_files", return_value=["src/Monetail.Api/Program.cs", "README.md"]):
            current = plan({}, "pull_request", self.records)
        self.assertTrue(current["required"]["api"])
        self.assertFalse(current["required"]["winui"])
        self.assertTrue(resolve(current, {})["api"])
        self.assertFalse(resolve(current, {"api": "true"})["api"])

    def test_failed_or_canceled_checks_without_success_record_are_retried(self):
        reused = dict.fromkeys(FLAGS, "true")
        reused["api"] = "false"
        self.assertEqual({"api"}, {flag for flag, run in resolve(self.all_required, reused).items() if run})

    def test_missing_record_runs_the_check_and_unrelated_proof_cannot_enable_a_check(self):
        current = {"required": dict.fromkeys(FLAGS, False), "fingerprints": self.all_required["fingerprints"]}
        current["required"]["api"] = True
        self.assertEqual({"api"}, {flag for flag, run in resolve(current, {"winui": "true"}).items() if run})

    def test_main_rechecks_all_current_inputs_after_an_unvalidated_earlier_push(self):
        with patch("ci_plan.changed_files", return_value=["README.md"]):
            current = plan({"ref": "refs/heads/main"}, "push", self.records)
        self.assertTrue(current["required"]["api"])
        self.assertTrue(current["required"]["winui"])
        self.assertFalse(resolve(current, {"api": "true", "docker": "true", "winui": "true"})["api"])

    def test_ghcr_only_pr_requires_no_product_proofs(self):
        with patch("ci_plan.changed_files", return_value=[".github/scripts/registry_image.py",
                                                           ".github/scripts/test_registry_image.py"]):
            current = plan({}, "pull_request", self.records)
        self.assertFalse(any(current["required"].values()))
        self.assertFalse(any(resolve(current, {}).values()))

    def test_main_ghcr_only_push_still_requires_checks_without_success_proof(self):
        with patch("ci_plan.changed_files", return_value=[".github/scripts/test_registry_image.py"]):
            current = plan({"ref": "refs/heads/main"}, "push", self.records)
        self.assertEqual({"api", "docker", "winui"},
                         {flag for flag, run in resolve(current, {}).items() if run})

    def test_main_ghcr_only_push_reuses_success_but_keeps_unvalidated_product_changes(self):
        for unvalidated_api in [False, True]:
            with self.subTest(unvalidated_api=unvalidated_api), tempfile.TemporaryDirectory() as directory:
                records = dict(self.records, **{".github/scripts/registry_image.py": "100644 blob new-ghcr",
                                               ".github/scripts/test_registry_image.py": "100644 blob new-test"})
                if unvalidated_api:
                    records["src/Monetail.Api/Program.cs"] = "100644 blob unvalidated-api"
                with patch("ci_plan.changed_files", return_value=[".github/scripts/registry_image.py"]):
                    current = plan({"ref": "refs/heads/main"}, "push", records)
                # pushでは全入力の成功を照合し、直近差分がGHCRだけでも未検証APIを見落とさない。
                self.assertTrue(current["required"]["api"])
                self.assertTrue(current["required"]["docker"])
                self.assertTrue(current["required"]["winui"])
                reused = {}
                for flag, digest in fingerprints(self.records).items():
                    options = {"directory": Path(directory)}
                    proof("save", flag, digest, **options)
                    hit = proof("restore", flag, current["fingerprints"][flag],
                                needed=current["required"][flag], cache_hit=True, **options)
                    reused[flag] = str(hit).lower()
                runs = {flag for flag, run in resolve(current, reused).items() if run}
                self.assertEqual({"api", "docker"} if unvalidated_api else set(), runs)

    def test_manual_run_selects_all_existing_inputs(self):
        current = plan({}, "workflow_dispatch", self.records)
        self.assertTrue(current["required"]["api"])
        self.assertTrue(current["required"]["winui"])

    def test_unknown_change_fails_before_success_results_can_be_reused(self):
        with patch("ci_plan.changed_files", return_value=["src/Future.Shared/New.cs"]):
            with self.assertRaisesRegex(ValueError, "未分類"):
                plan({}, "pull_request", self.records)

    def test_incomplete_plan_fails_instead_of_silently_skipping_checks(self):
        del self.all_required["required"]["api"]
        with self.assertRaises(ValueError):
            resolve(self.all_required, {})

    def test_resolution_outputs_run_flags_and_explains_reuse(self):
        with tempfile.TemporaryDirectory() as directory:
            output, summary = Path(directory) / "outputs", Path(directory) / "summary"
            with patch.dict(os.environ, {"CI_PLAN": json.dumps(self.all_required),
                                         "CI_REUSED": json.dumps({"api": "true"}),
                                         "GITHUB_OUTPUT": str(output), "GITHUB_STEP_SUMMARY": str(summary)}), \
                    patch("sys.argv", ["ci_plan.py", "resolve"]), redirect_stdout(StringIO()):
                main()
            values = dict(line.split("=") for line in output.read_text().splitlines())
            self.assertEqual("false", values["api"])
            self.assertEqual("true", values["winui"])
            self.assertIn("api: 同じ入力の成功結果を再利用", summary.read_text())


class ProofTests(unittest.TestCase):
    def test_only_an_exact_restored_success_can_be_reused(self):
        with tempfile.TemporaryDirectory() as directory:
            options = {"directory": Path(directory)}
            digest = "a" * 64
            self.assertFalse(proof("restore", "api", digest, cache_hit=True, **options))
            proof("save", "api", digest, **options)
            self.assertTrue(proof("restore", "api", digest, cache_hit=True, **options))
            self.assertFalse(proof("restore", "api", digest, cache_hit=False, **options))
            self.assertFalse(proof("restore", "api", "b" * 64, cache_hit=True, **options))
            self.assertFalse(proof("restore", "docker", digest, cache_hit=True, **options))

    def test_force_and_unneeded_checks_do_not_reuse_success(self):
        with tempfile.TemporaryDirectory() as directory:
            options = {"directory": Path(directory)}
            digest = "a" * 64
            proof("save", "api", digest, **options)
            self.assertFalse(proof("restore", "api", digest, cache_hit=True, force=True, **options))
            self.assertFalse(proof("restore", "api", digest, cache_hit=True, needed=False, **options))

    def test_invalid_or_disabled_save_does_not_create_a_success_record(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            proof("save", "api", "a" * 64, needed=False, directory=root)
            self.assertEqual([], list(root.iterdir()))
            for flag, digest in [("../api", "a" * 64), ("api", ""), ("api", "not-a-digest")]:
                with self.subTest(flag=flag, digest=digest), self.assertRaises(ValueError):
                    proof("save", flag, digest, directory=root)

    def test_corrupted_or_missing_success_file_requires_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            options = {"directory": Path(directory)}
            proof("save", "api", "a" * 64, **options)
            (Path(directory) / "api" / "success").write_text("interrupted")
            self.assertFalse(proof("restore", "api", "a" * 64, cache_hit=True, **options))


if __name__ == "__main__":
    unittest.main()
