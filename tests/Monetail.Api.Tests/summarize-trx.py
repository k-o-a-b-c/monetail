"""標準TRXから実行結果とクラス別・ケース別時間を集計する。"""

from collections import Counter
import sys
import xml.etree.ElementTree as ET


root = ET.parse(sys.argv[1]).getroot()
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
classes = {
    test.attrib["id"]: test.find("t:TestMethod", ns).attrib["className"]
    for test in root.findall("t:TestDefinitions/t:UnitTest", ns)
}
results = root.findall("t:Results/t:UnitTestResult", ns)
if not results:
    raise SystemExit("TRXにAPIテスト結果がありません。")

seconds_by_class = Counter()
cases_by_class = Counter()
outcomes = Counter()
slow = []
for result in results:
    attrs = result.attrib
    hours, minutes, seconds = map(float, attrs["duration"].split(":"))
    duration = hours * 3600 + minutes * 60 + seconds
    class_name = classes[attrs["testId"]]
    seconds_by_class[class_name] += duration
    cases_by_class[class_name] += 1
    outcomes[attrs["outcome"]] += 1
    slow.append((duration, attrs["testName"]))

print("## APIテスト実行時間")
print()
print(", ".join(f"{key} {count}" for key, count in sorted(outcomes.items())))
print()
print("| クラス | 件数 | ケース時間の合計（秒） |")
print("| --- | ---: | ---: |")
for name, duration in seconds_by_class.most_common():
    print(f"| `{name}` | {cases_by_class[name]} | {duration:.2f} |")
print()
print("並列実行を含むため、ケース時間の合計はジョブの経過時間と異なります。")
print()
print("### 時間の長い20ケース")
print()
for duration, name in sorted(slow, reverse=True)[:20]:
    safe_name = name.replace("|", "&#124;").replace("`", "&#96;").replace("\n", " ").replace("\r", " ")
    print(f"- {duration:.2f}秒 `{safe_name}`")
