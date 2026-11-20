"""Prevent high-confidence *new* WinUI structural violations during migration.

This deliberately does not attempt to prove MVVM, accessibility, or lifecycle correctness.
Those require code review and WinUI smoke tests.
"""

from collections import Counter
from dataclasses import dataclass
import argparse
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

XAML = "src/Monetail.WinUI/MainWindow.xaml"
WINUI_ROOT = "src/Monetail.WinUI/"
X_NAME = "{http://schemas.microsoft.com/winfx/2006/xaml}Name"
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")
SHOW_PAGE = re.compile(r"\bShowPage\s*\(")
BAD_UI_NAMESPACE = re.compile(r"\bWindows\.UI\.Xaml\b")
BAD_WPF_NAMESPACE = re.compile(r"\bSystem\.Windows\b(?!\.Input\b)")


@dataclass(frozen=True)
class Finding:
    path: str
    line: int
    message: str


def git(*args: str) -> str:
    result = subprocess.run(["git", *args], capture_output=True, text=True, check=False)
    if result.returncode:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout


def changed_lines(diff: str) -> list[tuple[str, int, str]]:
    """Return added lines with their new-file line numbers from a zero-context diff."""
    entries = []
    path = ""
    next_line = 0
    for row in diff.splitlines():
        if row.startswith("+++ b/"):
            path = row[6:]
        elif row.startswith("+++ /dev/null"):
            path = ""
        elif match := HUNK.match(row):
            next_line = int(match.group(1))
        elif row.startswith("+") and not row.startswith("+++"):
            if path:
                entries.append((path, next_line, row[1:]))
            next_line += 1
        elif row.startswith(" "):
            next_line += 1
    return entries


def new_line_findings(diff: str) -> list[Finding]:
    issues = []
    for path, line, original in changed_lines(diff):
        if not path.startswith(WINUI_ROOT) or not path.endswith((".cs", ".xaml")):
            continue
        content = original.split("//", 1)[0].strip()
        if not content or content.startswith("<!--") or content.startswith("*"):
            continue
        if BAD_UI_NAMESPACE.search(content):
            issues.append(Finding(path, line, "WinUI 3ではWindows.UI.Xamlを使わない"))
        if BAD_WPF_NAMESPACE.search(content):
            issues.append(Finding(path, line, "WinUI 3ではSystem.WindowsのUI APIを使わない"))
        if path.startswith("src/Monetail.WinUI/MainWindow") and path.endswith(".cs") and SHOW_PAGE.search(content):
            issues.append(Finding(path, line, "MainWindowへ旧ShowPage方式の呼び出しを追加しない"))
    return issues


def mainwindow_elements(source: str) -> Counter[str]:
    if not source.strip():
        return Counter()
    root = ET.fromstring(source)
    elements: Counter[str] = Counter()
    for node in root.iter():
        kind = node.tag.rsplit("}", 1)[-1]
        name = node.attrib.get(X_NAME, "")
        if kind == "ContentDialog":
            elements[f"ContentDialog:{name or '(unnamed)'}"] += 1
        if kind in {"Grid", "Page", "UserControl", "ContentControl"} and name.endswith("Page"):
            elements[f"screen:{name}"] += 1
    return elements


def mainwindow_findings(before: str, after: str) -> list[Finding]:
    old = mainwindow_elements(before)
    new = mainwindow_elements(after)
    findings = []
    for item in sorted(new.keys() | old.keys()):
        if new[item] > old[item]:
            if item.startswith("ContentDialog:"):
                reason = f"MainWindow.xamlへ新規ContentDialogを先行配置しない ({item})"
            else:
                reason = f"MainWindow.xamlへ新規画面を先行配置しない ({item})"
            findings.append(Finding(XAML, 1, reason))
    return findings


def run(base: str, head: str) -> list[Finding]:
    diff = git("diff", "--no-ext-diff", "--unified=0", base, head, "--", WINUI_ROOT)
    findings = new_line_findings(diff)
    if any(path == XAML for path, _, _ in changed_lines(diff)):
        old_source = git("show", f"{base}:{XAML}")
        new_source = git("show", f"{head}:{XAML}")
        findings.extend(mainwindow_findings(old_source, new_source))
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="", help="PR base SHA or push-before SHA")
    parser.add_argument("--head", default="HEAD")
    args = parser.parse_args()
    base = args.base
    if not base or set(base) == {"0"}:
        base = "HEAD^"
    try:
        issues = run(base, args.head)
    except (RuntimeError, ET.ParseError) as exc:
        print(f"WinUI構造検査の実行に失敗: {exc}", file=sys.stderr)
        return 2
    for issue in issues:
        print(f"::error file={issue.path},line={issue.line}::{issue.message}")
    if issues:
        print(f"WinUI構造検査: 新規違反 {len(issues)} 件")
        return 1
    print("WinUI構造検査: 対象の新規違反なし（MVVM/寿命等の手動レビューは別途必要）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
