"""CIの選択漏れ・不要な実行と、Git差分の境界を検証する。"""

import os
from contextlib import redirect_stdout
from io import StringIO
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from select_tests import FLAGS, PRODUCT, ROOT, changed_files, main, project_inputs, select


class SelectionTests(unittest.TestCase):
    def assert_flags(self, paths, *expected):
        actual = {flag for flag, enabled in select(paths).items() if enabled}
        self.assertEqual(set(expected), actual)

    def test_markdown_never_starts_product_tests(self):
        self.assert_flags(["AGENTS.md", "README.md", "docs/43-passkey-authentication-design.md",
                           "tests/README.md", "src/Monetail.Api/README.md"])

    def test_api_source_and_test_have_different_container_dependencies(self):
        self.assert_flags(["src/Monetail.Api/Program.cs"], "api", "docker")
        self.assert_flags(["tests/Monetail.Api.Tests/HealthTests.cs"], "api")
        self.assert_flags(["tests/Monetail.Api.Tests/docker-smoke.sh"], "docker")

    def test_container_configuration_does_not_start_windows_or_api_tests(self):
        self.assert_flags(["Dockerfile", ".dockerignore"], "docker")
        self.assert_flags(["compose.yaml"], "compose")

    def test_windows_form_does_not_start_calculation_migration_or_performance(self):
        self.assert_flags([PRODUCT + "Control/RegisterForm.vb"], "windows_tests", "windows_build")

    def test_model_and_expression_follow_their_actual_dependencies(self):
        self.assert_flags([PRODUCT + "Data/Data/Receipt.vb"],
                          "calculations", "windows_tests", "windows_build", "performance")
        self.assert_flags([PRODUCT + "Logic/IntegerExpression.vb"],
                          "calculations", "windows_tests", "windows_build")

    def test_read_changes_include_measurement_but_not_migration(self):
        self.assert_flags([PRODUCT + "DataAccess/DataAccess.vb"],
                          "windows_tests", "windows_build", "performance")

    def test_schema_and_json_in_docs_are_test_inputs(self):
        for path in ["docs/schema/monetail_schema.sql", "docs/migrations/20261019_01_revision_tokens.sql"]:
            with self.subTest(path=path):
                self.assert_flags([path], "migrations", "windows_tests", "performance")
        self.assert_flags(["docs/tests/tax-rounding-cases.json"], "calculations")
        self.assert_flags(["tests/test_revision_migration.py"], "migrations")

    def test_ui_test_change_does_not_rebuild_product_or_measure_reads(self):
        self.assert_flags(["tests/PersistenceTests/ReceiptInputTests.vb"], "windows_tests")
        self.assert_flags(["tests/PersistenceTests/ReceiptReadMeasurements.vb"], "windows_tests", "performance")

    def test_workflow_and_shared_build_settings_validate_all_jobs(self):
        for path in [".github/workflows/tests.yml", "Directory.Build.props", "global.json"]:
            with self.subTest(path=path):
                self.assert_flags([path], *FLAGS)
        self.assert_flags([".github/scripts/select_tests.py"])

    def test_mixed_changes_use_union_and_unknown_changes_fail(self):
        self.assert_flags(["README.md", "src/Monetail.Api/Program.cs", PRODUCT + "Control/RegisterForm.vb"],
                          "api", "docker", "windows_tests", "windows_build")
        with self.assertRaisesRegex(ValueError, "未分類"):
            select(["src/Future.Shared/Tax.cs"])

    def test_every_existing_project_input_selects_its_suite(self):
        for project, flag in [("tests/CalculationTests/CalculationTests.vbproj", "calculations"),
                              ("tests/PersistenceTests/PersistenceTests.vbproj", "windows_tests")]:
            for pattern in project_inputs(project):
                for source in ROOT.glob(pattern):
                    if source.is_file() and not source.name.endswith(".md"):
                        path = source.relative_to(ROOT).as_posix()
                        with self.subTest(project=project, path=path):
                            self.assertTrue(select([path])[flag])


class GitDiffTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.previous = Path.cwd()
        os.chdir(self.directory.name)
        self.addCleanup(self.directory.cleanup)
        self.addCleanup(os.chdir, self.previous)
        self.git("init", "-b", "main")
        self.git("config", "user.email", "ci-tests@example.invalid")
        self.git("config", "user.name", "CI tests")
        self.commit("src/Monetail.Api/Program.cs", "initial")
        self.base = self.git("rev-parse", "HEAD")
        self.git("update-ref", "refs/remotes/origin/main", self.base)

    def git(self, *args):
        return subprocess.check_output(["git", *args], stderr=subprocess.DEVNULL).decode().strip()

    def commit(self, path, text):
        file = Path(path)
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(text)
        self.git("add", "--all")
        self.git("commit", "-m", "test")
        return self.git("rev-parse", "HEAD")

    def test_push_rename_keeps_original_dependency_and_unicode_paths(self):
        self.git("mv", "src/Monetail.Api/Program.cs", "説明.md")
        head = self.commit("README.md", "docs")
        paths = changed_files({"before": self.base, "after": head}, "push")
        self.assertIn("src/Monetail.Api/Program.cs", paths)
        self.assertIn("説明.md", paths)
        self.assertTrue(select(paths)["api"])

    def test_pull_request_excludes_changes_only_on_base_branch(self):
        self.git("checkout", "-b", "topic")
        head = self.commit("README.md", "docs")
        self.git("checkout", "main")
        base = self.commit("compose.yaml", "services: {}")
        self.assertEqual(["README.md"], changed_files(
            {"pull_request": {"base": {"sha": base}, "head": {"sha": head}}}, "pull_request"))

    def test_first_branch_push_compares_against_default_branch(self):
        head = self.commit("README.md", "docs")
        event = {"before": "0" * 40, "after": head, "ref": "refs/heads/topic",
                 "repository": {"default_branch": "main"}}
        self.assertEqual(["README.md"], changed_files(event, "push"))
        Path("event.json").write_text(json.dumps(event))
        with patch.dict(os.environ, {"GITHUB_EVENT_NAME": "push", "GITHUB_EVENT_PATH": "event.json",
                                     "GITHUB_OUTPUT": "output.txt", "GITHUB_STEP_SUMMARY": "summary.md"}):
            with redirect_stdout(StringIO()):
                main()
        self.assertEqual({flag: "false" for flag in FLAGS},
                         dict(line.split("=") for line in Path("output.txt").read_text().splitlines()))
        self.assertIn("省略", Path("summary.md").read_text())

    def test_initial_default_branch_push_includes_all_files_and_deleted_files_are_kept(self):
        initial = {"before": "0" * 40, "after": self.base, "ref": "refs/heads/main",
                   "repository": {"default_branch": "main"}}
        self.assertEqual(["src/Monetail.Api/Program.cs"], changed_files(initial, "push"))
        self.git("rm", "src/Monetail.Api/Program.cs")
        head = self.commit("README.md", "docs")
        self.assertIn("src/Monetail.Api/Program.cs", changed_files({"before": self.base, "after": head}, "push"))


if __name__ == "__main__":
    unittest.main()
