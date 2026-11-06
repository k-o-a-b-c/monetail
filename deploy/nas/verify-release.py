"""配布タグと同じコミットのmain push成功だけを配布根拠として認める。"""
import json
import re
import sys


def verified_version(tag, sha, payload):
    if not re.fullmatch(r"api-v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", tag):
        raise ValueError("api-vX.Y.Zタグが必要です。")
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("コミットIDが不正です。")
    if not any(run.get("head_sha") == sha and run.get("head_branch") == "main" and
               run.get("event") == "push" and run.get("conclusion") == "success" and
               run.get("head_repository", {}).get("full_name") == "k-o-a-b-c/monetail"
               for run in payload.get("workflow_runs", [])):
        raise ValueError("同じコミットのmain push Tests成功が必要です。")
    return tag.removeprefix("api-")


if __name__ == "__main__":
    try:
        print(verified_version(sys.argv[1], sys.argv[2], json.load(sys.stdin)))
    except (ValueError, IndexError, TypeError, AttributeError) as error:
        sys.exit(str(error))
