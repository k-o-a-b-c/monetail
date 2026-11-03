"""検証入力のGitオブジェクトと成功記録を照合し、必要なCIだけ実行する。"""

import hashlib
import json
import os
from pathlib import Path
import re
import sys

from select_tests import FLAGS, changed_files, git, select, selected_inputs


def tracked_inputs():
    """作業ディレクトリの生成物ではなく、実際にチェックアウトしたツリーを使う。"""
    records = {}
    for entry in git("ls-tree", "-rz", "--full-tree", "HEAD").split(b"\0"):
        if entry:
            metadata, path = entry.split(b"\t", 1)
            records[path.decode("utf-8")] = metadata.decode("ascii")
    return records


def fingerprints(records):
    inputs = selected_inputs(records, strict=False)
    return {flag: hashlib.sha256(json.dumps(
        [(path, records[path]) for path in sorted(inputs[flag])],
        ensure_ascii=False, separators=(",", ":")).encode()).hexdigest() for flag in FLAGS}


def plan(event, event_name, records):
    # mainは各入力の成功記録を一通り確認し、未検証の変更を後続pushで取り落とさない。
    if event_name == "workflow_dispatch":
        required = {flag: bool(paths) for flag, paths in selected_inputs(records, strict=False).items()}
    else:
        paths = changed_files(event, event_name)
        required = select(paths)  # 未分類の変更は、成功記録の検索より先に拒否する。
        if event_name == "push" and not event.get("deleted"):
            required = {flag: bool(paths) for flag, paths in selected_inputs(records, strict=False).items()}
    return {"required": required, "fingerprints": fingerprints(records)}


def resolve(current, reused):
    if set(current["required"]) != set(FLAGS) or set(current["fingerprints"]) != set(FLAGS):
        raise ValueError("CI計画の検証対象が一致しません。")
    return {flag: required and reused.get(flag) != "true" for flag, required in current["required"].items()}


def proof(mode, flag, digest, *, needed=True, cache_hit=False, force=False, directory=Path(".ci-proof")):
    if flag not in FLAGS or not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise ValueError("不正なCI成功記録の指定です。")
    record = directory / flag / "success"
    if mode == "save":
        if needed:
            record.parent.mkdir(parents=True, exist_ok=True)
            repository, run_id = os.getenv("GITHUB_REPOSITORY"), os.getenv("GITHUB_RUN_ID")
            source = f"https://github.com/{repository}/actions/runs/{run_id}" if repository and run_id else None
            record.write_text(json.dumps({"fingerprint": digest, "source": source}) + "\n")
        return False
    if mode != "restore":
        raise ValueError("不正なCI成功記録の操作です。")
    if not needed or force or not cache_hit:
        return False
    try:
        saved = json.loads(record.read_text())
        return isinstance(saved, dict) and saved.get("fingerprint") == digest
    except (OSError, ValueError, UnicodeError):
        return False


def output(name, value):
    with open(os.environ["GITHUB_OUTPUT"], "a") as file:
        file.write(name + "=" + (json.dumps(value, separators=(",", ":")) if not isinstance(value, str) else value) + "\n")


def main():
    command = sys.argv[1]
    if command == "plan":
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
        current = plan(event, os.environ["GITHUB_EVENT_NAME"], tracked_inputs())
        output("plan", current)
        output("fingerprints", current["fingerprints"])
    elif command == "resolve":
        current = json.loads(os.environ["CI_PLAN"])
        reused = json.loads(os.environ["CI_REUSED"])
        result = resolve(current, reused)
        for flag, run in result.items():
            output(flag, str(run).lower())
        summary = "## CIの実行対象\n\n" + "\n".join(
            f"- {flag}: " + ("実行" if result[flag] else "同じ入力の成功結果を再利用" if current["required"][flag] else "影響なし")
            for flag in FLAGS) + "\n"
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as file:
            file.write(summary)
        print(summary)
    elif command == "proof":
        reused = proof(os.environ["CI_PROOF_MODE"], os.environ["CI_CHECK"], os.environ["CI_FINGERPRINT"],
                       needed=os.environ["CI_NEEDED"] == "true", cache_hit=os.environ["CI_CACHE_HIT"] == "true",
                       force=os.environ["CI_FORCE"] == "true")
        output("reused", str(reused).lower())
        if reused:
            saved = json.loads((Path(".ci-proof") / os.environ["CI_CHECK"] / "success").read_text())
            if saved.get("source"):
                with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as file:
                    file.write(f"- {os.environ['CI_CHECK']}: [再利用した成功CI]({saved['source']})\n")
    else:
        raise ValueError("不正なCI計画の操作です。")


if __name__ == "__main__":
    main()
