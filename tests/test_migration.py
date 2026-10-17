"""Run with: python3 -m unittest discover -s tests -p 'test_migration.py' -v."""

from pathlib import Path
import sqlite3
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCHEMA = (ROOT / "docs/schema/monetail_schema.sql").read_text(encoding="utf-8")
MIGRATION = (
    ROOT / "docs/migrations/20261017_01_shop_tax_rounding.sql"
).read_text(encoding="utf-8")

# Seed every existing table, including discounts and the original user assignments.
FIXTURE = """
INSERT INTO m_user VALUES (1, 'Alice', 'alice', 1), (2, 'Bob', 'bob', 2);
INSERT INTO m_point_classification VALUES (1, 'Points', 1, 1.0);
INSERT INTO m_payment_classification VALUES
    (1, 'Cash', 1, 1, 1, NULL, NULL),
    (2, 'Card', 2, NULL, 2, '2020-01-01', '2030-12-31');
INSERT INTO m_group_classification VALUES (1, 'Food', 1, 1, 0);
INSERT INTO m_shop VALUES
    (1, 'Registered shop', 1, 1, 1, 1),
    (2, 'Shared shop', 2, NULL, NULL, NULL);
INSERT INTO m_tax_rate VALUES (4, 10, '2019-10-01'), (5, 8, '2019-10-01');
INSERT INTO t_receipts VALUES
    (1, '2026-10-01', 'Unregistered shop', '', 1, 1, 'old receipt',
        'alice', '2026-10-01 10:00:00', 41),
    (2, '2026-10-02', 'Registered shop', NULL, 0, 2, NULL,
        'bob', '2026-10-02 10:00:00', -19);
INSERT INTO t_receipt_groups VALUES
    (1, 1, 1, 'first group', 4), (2, 1, 1, NULL, 5);
INSERT INTO t_receipt_details VALUES
    (1, 1, 1, 'Discounted item', 25, 2, 3, 11, 1, 'manual discount'),
    (2, 1, 1, 'Refund', -19, 1, 0, 0, 1, NULL);
INSERT INTO t_receipt_detail_users VALUES
    (1, 1, 1, 1), (1, 1, 1, 2), (2, 1, 1, 2);
INSERT INTO t_receipt_points VALUES (1, 1, 1, 1.0, 2, 3);
INSERT INTO t_transfers VALUES
    (1, '2026-10-03', 'Transfer', 500, 1, 2, NULL,
        'alice', '2026-10-03 10:00:00');
INSERT INTO t_balances VALUES
    (1, '2026-10-01', 'Opening balance', 'alice', '2026-10-01 09:00:00');
INSERT INTO t_balance_groups VALUES (1, 1, 10000, NULL, 10000);
INSERT INTO t_payment_memo VALUES (1, '2026-10-03', 'Checked');
INSERT INTO t_exchange_rates VALUES ('2026-10-01', 'USD', 'JPY', 150.25);
"""


def create_database(foreign_keys=1):
    database = sqlite3.connect(":memory:")
    database.execute("PRAGMA foreign_keys = " + str(foreign_keys))
    database.execute("PRAGMA user_version = 73")
    database.executescript(SCHEMA)
    database.executescript(FIXTURE)
    return database


def identifier(name):
    return '"' + name.replace('"', '""') + '"'


def table_columns(database):
    tables = database.execute(
        "SELECT name FROM sqlite_master "
        "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
    )
    return {
        name: database.execute("PRAGMA table_info(" + identifier(name) + ")").fetchall()
        for (name,) in tables
    }


def snapshot_rows(database, columns):
    return {
        name: database.execute(
            "SELECT "
            + ", ".join(identifier(column[1]) for column in metadata)
            + " FROM "
            + identifier(name)
            + " ORDER BY rowid"
        ).fetchall()
        for name, metadata in columns.items()
    }


class TaxRoundingMigrationTests(unittest.TestCase):
    def setUp(self):
        self.database = create_database()
        self.addCleanup(self.database.close)
        self.original_columns = table_columns(self.database)
        self.original_rows = snapshot_rows(self.database, self.original_columns)

    def migrate(self):
        self.database.executescript(MIGRATION)

    def test_preserves_every_original_row_including_totals_and_user_assignments(self):
        self.assertTrue(all(self.original_rows.values()), "Fixture must cover every table")
        self.migrate()
        self.assertEqual(
            self.original_rows,
            snapshot_rows(self.database, self.original_columns),
        )
        self.assertEqual(
            [(1, 41), (2, -19)],
            self.database.execute("SELECT seq, total FROM t_receipts ORDER BY seq").fetchall(),
        )
        self.assertEqual([], self.database.execute("PRAGMA foreign_key_check").fetchall())
        self.assertEqual(("ok",), self.database.execute("PRAGMA integrity_check").fetchone())

    def test_adds_exactly_two_columns_with_floor_defaults_and_validated_modes(self):
        self.migrate()
        migrated_columns = table_columns(self.database)
        self.assertEqual(set(self.original_columns), set(migrated_columns))
        for table, old_columns in self.original_columns.items():
            with self.subTest(table=table):
                expected = old_columns[:]
                if table in ("m_shop", "t_receipts"):
                    expected.append(
                        (len(old_columns), "tax_rounding_mode", "TEXT", 1, "'floor'", 0)
                    )
                self.assertEqual(expected, migrated_columns[table])

        for table in ("m_shop", "t_receipts"):
            with self.subTest(table=table):
                self.assertEqual(
                    [("floor",), ("floor",)],
                    self.database.execute(
                        "SELECT tax_rounding_mode FROM " + table + " ORDER BY rowid"
                    ).fetchall(),
                )
                for mode in ("half_up", "floor"):
                    self.database.execute(
                        "UPDATE " + table + " SET tax_rounding_mode = ?", (mode,)
                    )
                for invalid in (None, "", "ceiling", "FLOOR"):
                    with self.subTest(invalid=invalid), self.assertRaises(sqlite3.IntegrityError):
                        self.database.execute(
                            "UPDATE " + table + " SET tax_rounding_mode = ?", (invalid,)
                        )

        self.database.execute("INSERT INTO m_shop(code, name) VALUES (99, 'New master')")
        self.database.execute(
            "INSERT INTO t_receipts "
            "(seq, date, shop_name, tax_type, payment_classification_code, update_user, update_datetime) "
            "VALUES (99, '2026-10-04', 'New free-text shop', 1, 1, 'alice', '2026-10-04')"
        )
        for table, key in (("m_shop", "code"), ("t_receipts", "seq")):
            self.assertEqual(
                ("floor",),
                self.database.execute(
                    "SELECT tax_rounding_mode FROM " + table + " WHERE " + key + " = 99"
                ).fetchone(),
            )

    def test_retains_original_foreign_keys_without_receipt_shop_binding(self):
        before = {
            table: self.database.execute(
                "PRAGMA foreign_key_list(" + identifier(table) + ")"
            ).fetchall()
            for table in self.original_columns
        }
        self.migrate()
        for table, expected in before.items():
            self.assertEqual(
                expected,
                self.database.execute(
                    "PRAGMA foreign_key_list(" + identifier(table) + ")"
                ).fetchall(),
            )
        self.assertNotIn("shop_code", [row[1] for row in table_columns(self.database)["t_receipts"]])
        self.assertNotIn("m_shop", [row[2] for row in before["t_receipts"]])

    def test_preserves_nonzero_user_version_and_both_foreign_key_settings(self):
        for enabled in (0, 1):
            with self.subTest(foreign_keys=enabled):
                database = create_database(enabled)
                try:
                    database.executescript(MIGRATION)
                    self.assertEqual((73,), database.execute("PRAGMA user_version").fetchone())
                    self.assertEqual((enabled,), database.execute("PRAGMA foreign_keys").fetchone())
                finally:
                    database.close()

    def test_reapplication_fails_without_changing_the_migrated_database(self):
        self.migrate()
        columns = table_columns(self.database)
        rows = snapshot_rows(self.database, columns)
        schema = self.database.execute("SELECT * FROM sqlite_master ORDER BY name").fetchall()
        with self.assertRaisesRegex(sqlite3.OperationalError, "duplicate column name"):
            self.database.executescript(MIGRATION)
        # A failed migration is rolled back before the connection is reused.
        self.database.rollback()
        self.assertFalse(self.database.in_transaction)
        self.assertEqual(columns, table_columns(self.database))
        self.assertEqual(rows, snapshot_rows(self.database, columns))
        self.assertEqual(
            schema,
            self.database.execute("SELECT * FROM sqlite_master ORDER BY name").fetchall(),
        )


if __name__ == "__main__":
    unittest.main()
