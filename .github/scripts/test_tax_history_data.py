"""Git管理する日本の消費税履歴JSONの構造と既知の境界日を検証する。"""

from datetime import date
import json
from pathlib import Path
import re
import unittest


DATA = Path(__file__).resolve().parents[2] / "config/tax-history.json"
CODES = {"Standard", "ReducedFood", "ReducedNewspaper", "Exempt"}
FIELDS = {"taxCategoryCode", "validFrom", "validTo", "rate"}
DECIMAL = re.compile(r"(0|[1-9][0-9]*)(\.[0-9]+)?\Z")


class TaxHistoryDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        payload = json.loads(DATA.read_text(encoding="utf-8"))
        assert set(payload) == {"rates"} and isinstance(payload["rates"], list)
        cls.rates = payload["rates"]

    def test_four_categories_and_nonoverlapping_valid_periods(self):
        previous = {}
        seen = set()
        for row in self.rates:
            with self.subTest(row=row):
                self.assertEqual(FIELDS, set(row))
                code = row["taxCategoryCode"]
                self.assertIn(code, CODES)
                start = date.fromisoformat(row["validFrom"])
                self.assertEqual(start.isoformat(), row["validFrom"])
                end = None if row["validTo"] is None else date.fromisoformat(row["validTo"])
                if end is not None:
                    self.assertEqual(end.isoformat(), row["validTo"])
                    self.assertLessEqual(start, end)
                key = (code, start)
                self.assertNotIn(key, seen)
                seen.add(key)
                if code in previous:
                    old_end = previous[code]
                    self.assertIsNotNone(old_end)
                    self.assertLess(old_end, start)
                previous[code] = end
                rate = row["rate"]
                if code == "Exempt":
                    self.assertIsNone(rate)
                elif rate is not None:
                    self.assertIsInstance(rate, str)
                    self.assertRegex(rate, DECIMAL)
        self.assertEqual(CODES, set(previous))

    def test_implementation_boundaries_and_pre_tax_null(self):
        missing = object()

        def rate_for(code, day):
            hits = [
                row for row in self.rates
                if row["taxCategoryCode"] == code
                and row["validFrom"] <= day
                and (row["validTo"] is None or day <= row["validTo"])
            ]
            self.assertLessEqual(len(hits), 1)
            return hits[0]["rate"] if hits else missing

        for code in CODES:
            self.assertIsNone(rate_for(code, "1989-03-31"))
        for code in CODES - {"Exempt"}:
            for day, expected in [
                ("1989-04-01", "3"),
                ("1997-04-01", "5"),
                ("2014-04-01", "8"),
            ]:
                self.assertEqual(expected, rate_for(code, day))
        self.assertEqual("10", rate_for("Standard", "2019-10-01"))
        self.assertEqual("8", rate_for("ReducedFood", "2019-10-01"))
        self.assertEqual("8", rate_for("ReducedNewspaper", "2019-10-01"))
        self.assertIsNone(rate_for("Exempt", "2026-11-12"))


if __name__ == "__main__":
    unittest.main()
