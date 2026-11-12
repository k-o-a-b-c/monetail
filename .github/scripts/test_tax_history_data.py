"""運営者が同期CLIへ渡す税率履歴JSONの構造・期間・基本的な境界を検証する。"""

from datetime import date, timedelta
import json
from pathlib import Path
import re
import unittest


FILE = Path(__file__).resolve().parents[2] / "config/tax-history.json"
CODES = {"Standard", "ReducedFood", "ReducedNewspaper", "Exempt"}
FIELDS = {"taxCategoryCode", "validFrom", "validTo", "rate"}


def parse_day(value):
    if not isinstance(value, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}", value):
        raise ValueError("日付はYYYY-MM-DD文字列で指定してください。")
    return date.fromisoformat(value)


class TaxHistoryDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = json.loads(FILE.read_text(encoding="utf-8"))

    def test_document_shape_and_rate_type(self):
        self.assertEqual({"rates"}, set(self.data))
        self.assertIsInstance(self.data["rates"], list)
        for row in self.data["rates"]:
            with self.subTest(row=row):
                self.assertEqual(FIELDS, set(row))
                self.assertIn(row["taxCategoryCode"], CODES)
                parse_day(row["validFrom"])
                if row["validTo"] is not None:
                    self.assertLessEqual(parse_day(row["validFrom"]), parse_day(row["validTo"]))
                rate = row["rate"]
                if rate is not None:
                    self.assertIsInstance(rate, str)
                    self.assertRegex(rate, r"\A(?:0|[1-9]\d*)(?:\.\d+)?\Z")
                if row["taxCategoryCode"] == "Exempt":
                    self.assertIsNone(rate)

    def test_all_categories_cover_calendar_without_gaps_or_overlap(self):
        for code in sorted(CODES):
            with self.subTest(code=code):
                rows = sorted(
                    (row for row in self.data["rates"] if row["taxCategoryCode"] == code),
                    key=lambda row: row["validFrom"],
                )
                self.assertTrue(rows)
                self.assertEqual(date.min, parse_day(rows[0]["validFrom"]))
                for index, row in enumerate(rows):
                    if index == len(rows) - 1:
                        self.assertIsNone(row["validTo"])
                    else:
                        self.assertIsNotNone(row["validTo"])
                        self.assertEqual(
                            parse_day(row["validTo"]) + timedelta(days=1),
                            parse_day(rows[index + 1]["validFrom"]),
                        )

    def test_introduction_and_current_rates(self):
        def applicable(code, day):
            day = parse_day(day)
            matches = [
                row for row in self.data["rates"]
                if row["taxCategoryCode"] == code
                and parse_day(row["validFrom"]) <= day
                and (row["validTo"] is None or day <= parse_day(row["validTo"]))
            ]
            self.assertEqual(1, len(matches))
            return matches[0]["rate"]

        self.assertIsNone(applicable("Standard", "1989-03-31"))
        self.assertEqual("3", applicable("Standard", "1989-04-01"))
        self.assertEqual("5", applicable("Standard", "1997-04-01"))
        self.assertEqual("8", applicable("Standard", "2014-04-01"))
        self.assertEqual("10", applicable("Standard", "2019-10-01"))
        self.assertEqual("10", applicable("Standard", "2026-11-12"))
        self.assertEqual("8", applicable("ReducedFood", "2026-11-12"))
        self.assertEqual("8", applicable("ReducedNewspaper", "2026-11-12"))
        self.assertIsNone(applicable("Exempt", "2026-11-12"))


if __name__ == "__main__":
    unittest.main()
