"""変更ファイルとテストの参照先から、必要なCI処理を選ぶ。"""

import fnmatch
import json
import os
from pathlib import Path
import posixpath
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
FLAGS = ("api", "docker", "compose", "calculations", "migrations",
         "windows_tests", "windows_build", "performance", "windows_client", "windows_storage", "winui")
PRODUCT = "HouseholdAccountsBeta/HouseholdAccountsBeta/"


def matches(path, pattern):
    """MSBuildの再帰パターンを、直下のファイルも含めて照合する。"""
    if fnmatch.fnmatchcase(path, pattern):
        return True
    return "/**/" in pattern and matches(path, pattern.replace("/**/", "/", 1))


def project_inputs(project, visited=None):
    """テストプロジェクトと参照プロジェクトの入力パターンを集める。"""
    visited = set() if visited is None else visited
    if project in visited:
        return set()
    visited.add(project)
    directory = posixpath.dirname(project)
    extension = ".cs" if project.endswith(".csproj") else ".vb"
    inputs = {project, directory + "/**/*" + extension}
    for item in ET.parse(ROOT / project).iter():
        tag = item.tag.rsplit("}", 1)[-1]
        include = item.get("Include")
        if not include or tag not in {"Compile", "None", "Content", "EmbeddedResource", "ProjectReference"}:
            continue
        path = posixpath.normpath(posixpath.join(directory, include.replace("\\", "/")))
        if tag == "ProjectReference":
            inputs.update(project_inputs(path, visited))
        else:
            inputs.add(path)
    return inputs


def selected_inputs(paths, *, strict=True):
    """各検証へ影響するファイルを集める。変更の未分類は既定で拒否する。"""
    result = {flag: set() for flag in FLAGS}
    calculation_inputs = project_inputs("tests/CalculationTests/CalculationTests.vbproj")
    windows_inputs = project_inputs("tests/PersistenceTests/PersistenceTests.vbproj")
    api_inputs = project_inputs("tests/Monetail.Api.Tests/Monetail.Api.Tests.csproj")
    client_project = "tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj"
    client_inputs = project_inputs(client_project) if (ROOT / client_project).exists() else set()
    storage_project = "tests/Monetail.Windows.Storage.Tests/Monetail.Windows.Storage.Tests.csproj"
    storage_inputs = project_inputs(storage_project) if (ROOT / storage_project).exists() else set()
    removed_project_roots = {
        posixpath.dirname(path) + "/" for path in paths
        if path.endswith((".csproj", ".vbproj")) and not (ROOT / path).is_file()
        and not any((ROOT / posixpath.dirname(path)).glob("*.*proj"))
    }
    performance_helpers = {
        "Program.vb", "ReceiptReadMeasurements.vb", "DatabaseFixture.vb",
        "BalanceBoundaryTests.vb", "ConcurrencyTests.vb", "PaymentReceiptReadTests.vb",
        "PersistenceTests.vbproj",
    }
    unknown = []
    for path in paths:
        if path.endswith(".md") or path in {".gitignore", ".gitattributes"}:
            continue
        if path == ".github/workflows/tests.yml" or path.startswith((".github/scripts/", ".github/actions/ci-proof/")) or path in {
            "global.json", "NuGet.config", "nuget.config", "Directory.Build.props",
            "Directory.Build.targets", "Directory.Packages.props",
        }:
            for inputs in result.values():
                inputs.add(path)
            continue
        if path == ".github/workflows/winui.yml" or path.startswith(("src/Monetail.WinUI/", "tests/Monetail.WinUI.Smoke/")):
            result["winui"].add(path)
            continue
        known = False

        def enable(*flags):
            nonlocal known
            known = True
            for flag in flags:
                result[flag].add(path)

        if path.startswith("src/Monetail.Api/"):
            enable("api", "docker")
        if path == "tests/Monetail.Api.Tests/docker-smoke.sh":
            enable("docker")
        elif path.startswith("tests/Monetail.Api.Tests/"):
            enable("api")
        if any(matches(path, pattern) for pattern in api_inputs):
            enable("api")
        if path.startswith("src/Monetail.Windows.Client/"):
            enable("windows_client", "windows_storage", "winui")
        if path.startswith("tests/Monetail.Windows.Client.Tests/"):
            enable("windows_client")
        if path.startswith("tests/Monetail.Windows.Storage.Tests/"):
            enable("windows_storage")
        if any(matches(path, pattern) for pattern in client_inputs):
            enable("windows_client")
        if any(matches(path, pattern) for pattern in storage_inputs):
            enable("windows_storage")
        if path in {"Dockerfile", ".dockerignore"}:
            enable("docker")
        if path == "compose.yaml":
            enable("compose")
        if path.startswith("HouseholdAccountsBeta/"):
            enable("windows_build")
        if any(matches(path, pattern) for pattern in calculation_inputs):
            enable("calculations")
        if any(matches(path, pattern) for pattern in windows_inputs):
            enable("windows_tests")
        if (path.startswith("tests/CalculationTests/") or
                path.startswith("docs/tests/") and path.endswith(".json")):
            enable("calculations")
        if path.startswith(("tests/PersistenceTests/", "tests/OriginalControls.TestBuild/")):
            enable("windows_tests")
        if (path.startswith(("docs/schema/", "docs/migrations/")) and path.endswith(".sql") or
                fnmatch.fnmatchcase(path, "tests/test_*migration.py")):
            enable("migrations")
        if (path.startswith((PRODUCT + "Data/", PRODUCT + "DataAccess/")) and path.endswith(".vb") or
                path in {PRODUCT + "Logic/Calculation.vb", "tests/Shared/Assertions.vb"} or
                path.startswith("tests/PersistenceTests/") and path.rsplit("/", 1)[-1] in performance_helpers or
                path.endswith(".sql") and any(matches(path, pattern) for pattern in windows_inputs)):
            enable("performance")
        # 残存する参照先の検証を優先し、未参照になったプロジェクトの削除だけ省略する。
        if (not known and not (ROOT / path).is_file()
                and any(path.startswith(root) for root in removed_project_roots)):
            known = True
        if not known:
            unknown.append(path)
    if unknown and strict:
        raise ValueError("CI対象が未分類です。依存先を確認して選択条件を追加してください: " + ", ".join(unknown))
    # 全入力の指紋では既存の未分類ファイルも全検証へ含め、黙って依存を落とさない。
    for inputs in result.values():
        inputs.update(unknown)
    return result


def select(paths):
    """変更の影響がある処理だけを有効にする。"""
    return {flag: bool(inputs) for flag, inputs in selected_inputs(paths).items()}


def git(*args):
    """Gitをシェル展開なしで呼び出す。"""
    return subprocess.check_output(["git", *args])


def changed_files(event, event_name):
    """PRは分岐点以降、pushは更新前後の変更を削除・改名も含めて取る。"""
    if event_name == "pull_request":
        base = event["pull_request"]["base"]["sha"]
        head = event["pull_request"]["head"]["sha"]
        base = git("merge-base", base, head).decode().strip()
    elif event_name == "push":
        if event.get("deleted"):
            return []  # ブランチ削除には検証するコード変更がない。
        base, head = event["before"], event["after"]
        if set(base) == {"0"}:
            default = event["repository"]["default_branch"]
            if event["ref"] != "refs/heads/" + default:
                base = git("merge-base", "origin/" + default, head).decode().strip()
            else:
                base = subprocess.check_output(
                    ["git", "hash-object", "-w", "-t", "tree", "--stdin"], input=b"").decode().strip()
    else:
        raise ValueError("未対応のイベントです: " + event_name)
    # 改名は削除＋追加として扱い、移動元に必要だった検証も落とさない。
    raw = git("diff", "--name-only", "--no-renames", "-z", base, head)
    return [path.decode("utf-8") for path in raw.split(b"\0") if path]


def main():
    """選択結果をActionsの出力と実行概要へ書き出す。"""
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    paths = changed_files(event, os.environ["GITHUB_EVENT_NAME"])
    result = select(paths)
    with open(os.environ["GITHUB_OUTPUT"], "a") as output:
        for flag, enabled in result.items():
            output.write(f"{flag}={str(enabled).lower()}\n")
    summary = "## CIの実行対象\n\n" + "\n".join(
        f"- {flag}: {'実行' if enabled else '省略'}" for flag, enabled in result.items()) + "\n"
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as output:
        output.write(summary)
    print(summary)


if __name__ == "__main__":
    main()
