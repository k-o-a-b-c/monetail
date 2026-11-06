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

    def test_new_windows_client_source_validates_portable_and_desktop_without_legacy(self):
        self.assert_flags(["src/Monetail.Windows.Client/Authentication/BrowserOAuthLogin.cs"],
                          "windows_client", "windows_storage", "winui")
        self.assert_flags(["src/Monetail.Windows.Client/Monetail.Windows.Client.csproj"],
                          "windows_client", "windows_storage", "winui")

    def test_removed_projects_can_be_deleted_but_not_reintroduced(self):
        paths = ["src/Removed/Removed.csproj", "src/Removed/Example.cs"]
        self.assert_flags(paths)
        with self.assertRaisesRegex(ValueError, "未分類"):
            select(["src/Removed/Example.cs"])
        # 削除したソースを別のテストが参照している場合は、その検証を省略しない。
        with patch("select_tests.project_inputs", return_value={"src/Removed/Example.cs"}):
            self.assertTrue(select(paths)["windows_client"])

    def test_winui_source_and_smoke_select_the_reusable_job(self):
        self.assert_flags(["src/Monetail.WinUI/Monetail.WinUI.csproj", "src/Monetail.WinUI/MainWindow.xaml",
                           "src/Monetail.WinUI/MainWindow.xaml.cs", "tests/Monetail.WinUI.Smoke/run.ps1"], "winui")

    def test_winui_workflow_does_not_start_unrelated_api_or_legacy_jobs(self):
        self.assert_flags([".github/workflows/winui.yml"], "winui")

    def test_new_windows_tests_select_only_their_platform(self):
        self.assert_flags(["tests/Monetail.Windows.Client.Tests/OAuthTests.cs"], "windows_client")
        self.assert_flags(["tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj"],
                          "windows_client")
        self.assert_flags(["tests/Monetail.Windows.Storage.Tests/DpapiStorageTests.cs"], "windows_storage")

    def test_container_configuration_does_not_start_windows_or_api_tests(self):
        self.assert_flags(["Dockerfile", ".dockerignore"], "docker")
        self.assert_flags(["compose.yaml"], "docker", "compose")

    def test_removed_deployment_paths_still_trigger_compose_checks(self):
        for path in ["deploy/nas/compose.image.yaml", "deploy/nas/package.sh"]:
            with self.subTest(path=path):
                self.assert_flags([path], "docker", "compose")

    def test_server_deployment_validates_merged_configuration_and_real_container(self):
        for path in ["deploy/docker/compose.server.yaml", 
                     "deploy/docker/server.env.example", "deploy/docker/create-oauth-certificates.sh",
                     "tests/Monetail.Api.Tests/server-smoke.sh"]:
            with self.subTest(path=path):
                self.assert_flags([path], "docker", "compose")

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
                self.assert_flags([path], "api", "migrations", "windows_tests", "performance")
        self.assert_flags(["docs/tests/tax-rounding-cases.json"], "calculations")
        self.assert_flags(["tests/test_revision_migration.py"], "migrations")
        self.assert_flags(["docs/migrations/future.sql"], "migrations")

    def test_ui_test_change_does_not_rebuild_product_or_measure_reads(self):
        self.assert_flags(["tests/PersistenceTests/ReceiptInputTests.vb"], "windows_tests")
        self.assert_flags(["tests/PersistenceTests/ReceiptReadMeasurements.vb"], "windows_tests", "performance")
        self.assert_flags(["tests/Shared/ReceiptFixtures.vb"], "calculations", "windows_tests")
        self.assert_flags(["tests/Shared/Assertions.vb"], "calculations", "windows_tests", "performance")

    def test_workflow_and_shared_build_settings_validate_all_jobs(self):
        for path in [".github/workflows/tests.yml", ".github/actions/nuget-cache/action.yml", "Directory.Build.props", "global.json"]:
            with self.subTest(path=path):
                self.assert_flags([path], *FLAGS)
        self.assert_flags([".github/scripts/select_tests.py"], *FLAGS)

    def test_release_distribution_selects_api_and_containers(self):
        self.assert_flags([".github/workflows/release-api.yml"], "api", "docker", "compose")
        self.assert_flags(["tests/Monetail.Api.Tests/release-smoke.sh"], "docker", "compose")

    def test_ghcr_only_scripts_do_not_select_product_checks(self):
        paths = [".github/scripts/registry_image.py", ".github/scripts/test_registry_image.py"]
        for path in paths:
            with self.subTest(path=path):
                self.assert_flags([path])
        self.assert_flags(paths)

    def test_ghcr_scripts_do_not_hide_mixed_product_changes(self):
        paths = [".github/scripts/registry_image.py", ".github/scripts/test_registry_image.py"]
        self.assert_flags(paths + ["src/Monetail.Api/Program.cs"], "api", "docker")
        self.assert_flags(paths + ["src/Monetail.WinUI/MainWindow.xaml"], "winui")
        with self.assertRaisesRegex(ValueError, "未分類"):
            select(paths + ["src/Future.Shared/Tax.cs"])

    def test_ghcr_exception_does_not_apply_to_shared_or_new_scripts(self):
        for path in [".github/scripts/select_tests.py", ".github/scripts/test_select_tests.py",
                     ".github/scripts/ci_plan.py", ".github/scripts/test_ci_plan.py",
                     ".github/scripts/registry_image_extra.py", ".github/scripts/test_registry_image_extra.py",
                     ".github/scripts/future.py"]:
            with self.subTest(path=path):
                self.assert_flags([path], *FLAGS)

    def test_mixed_changes_use_union_and_unknown_changes_fail(self):
        self.assert_flags(["README.md", "src/Monetail.Api/Program.cs", PRODUCT + "Control/RegisterForm.vb"],
                          "api", "docker", "windows_tests", "windows_build")
        with self.assertRaisesRegex(ValueError, "未分類"):
            select(["src/Future.Shared/Tax.cs"])

    def test_every_existing_project_input_selects_its_suite(self):
        projects = [("tests/CalculationTests/CalculationTests.vbproj", "calculations"),
                    ("tests/PersistenceTests/PersistenceTests.vbproj", "windows_tests"),
                    ("tests/Monetail.Api.Tests/Monetail.Api.Tests.csproj", "api"),
                    ("tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj", "windows_client"),
                    ("tests/Monetail.Windows.Storage.Tests/Monetail.Windows.Storage.Tests.csproj", "windows_storage")]
        for project, flag in projects:
            if not (ROOT / project).exists():
                continue
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
        self.assertEqual([], changed_files({"deleted": True, "before": head, "after": "0" * 40}, "push"))


if __name__ == "__main__":
    unittest.main()
