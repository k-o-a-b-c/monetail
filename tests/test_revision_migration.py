"""Run with: python3 -m unittest discover -s tests -p 'test_*migration.py' -v."""

from pathlib import Path
import sqlite3
import unittest

from test_migration import (
    MIGRATION as TAX_MIGRATION,
    create_database,
    identifier,
    table_columns,
)


ROOT = Path(__file__).resolve().parents[1]
MIGRATION = (
    ROOT / "docs/migrations/20261019_01_revision_tokens.sql"
).read_text(encoding="utf-8")
HEADERS = ("t_receipts", "t_transfers", "t_balances")


def create_revision_database(foreign_keys=1):
    database = create_database(foreign_keys)
    database.executescript(TAX_MIGRATION)
    # 小数率・NULL・日本語・引用符をSQLiteの格納型ごと比較する。
    database.execute("UPDATE t_receipt_details SET discount_rate=2.5 WHERE receipt_seq=1")
    database.execute("UPDATE t_receipts SET tag=? WHERE seq=1", ("日本語 '引用'\n　",))
    database.commit()
    return database


def physical_rows(database, columns):
    result = {}
    for table, metadata in columns.items():
        fields = []
        for column in metadata:
            name = identifier(column[1])
            fields.extend(("typeof(" + name + ")", "quote(" + name + ")"))
        result[table] = database.execute(
            "SELECT " + ",".join(fields) + " FROM " + identifier(table) + " ORDER BY rowid"
        ).fetchall()
    return result


class RevisionTokenMigrationTests(unittest.TestCase):
    def setUp(self):
        self.database = create_revision_database()
        self.addCleanup(self.database.close)
        self.original_columns = table_columns(self.database)
        self.original_rows = physical_rows(self.database, self.original_columns)

    def migrate(self):
        self.database.executescript(MIGRATION)

    def test_preserves_every_saved_value_and_storage_type(self):
        self.assertTrue(all(self.original_rows.values()), "Fixture must cover every table")
        self.migrate()
        self.assertEqual(self.original_rows, physical_rows(self.database, self.original_columns))
        self.assertEqual(
            [(1, 41), (2, -19)],
            self.database.execute("SELECT seq,total FROM t_receipts ORDER BY seq").fetchall(),
        )
        self.assertEqual(
            ("real", "2.5"),
            self.database.execute(
                "SELECT typeof(discount_rate),quote(discount_rate) "
                "FROM t_receipt_details WHERE receipt_seq=1"
            ).fetchone(),
        )
        self.assertEqual([], self.database.execute("PRAGMA foreign_key_check").fetchall())
        self.assertEqual(("ok",), self.database.execute("PRAGMA integrity_check").fetchone())

    def test_adds_exactly_one_nonnull_text_token_to_each_header(self):
        self.migrate()
        migrated = table_columns(self.database)
        self.assertEqual(set(self.original_columns), set(migrated))
        for table, old_columns in self.original_columns.items():
            with self.subTest(table=table):
                expected = old_columns[:]
                if table in HEADERS:
                    expected.append((len(old_columns), "revision_token", "TEXT", 1, "'0'", 0))
                self.assertEqual(expected, migrated[table])

    def test_existing_and_new_rows_default_to_text_zero_and_null_is_rejected(self):
        self.migrate()
        self.database.executescript(
            "INSERT INTO t_receipts(seq,date,shop_name,tax_type,payment_classification_code,update_user,update_datetime) "
            "VALUES(99,'2026-10-19','new receipt',1,1,'alice','2026-10-19');"
            "INSERT INTO t_transfers(seq,date,fee,from_payment_classification_code,to_payment_classification_code,update_user,update_datetime) "
            "VALUES(99,'2026-10-19',10,1,2,'alice','2026-10-19');"
            "INSERT INTO t_balances(seq,date,update_user,update_datetime) "
            "VALUES(99,'2026-10-19','alice','2026-10-19');"
        )
        for table in HEADERS:
            with self.subTest(table=table):
                rows = self.database.execute(
                    "SELECT revision_token,typeof(revision_token) FROM " + table + " ORDER BY seq"
                ).fetchall()
                self.assertTrue(rows)
                self.assertEqual([("0", "text")] * len(rows), rows)
                token = "4c7b39a70ab74b1f996d60efab23e5a8"
                self.database.execute("UPDATE " + table + " SET revision_token=? WHERE seq=99", (token,))
                self.assertEqual(
                    (token, "text"),
                    self.database.execute(
                        "SELECT revision_token,typeof(revision_token) FROM " + table + " WHERE seq=99"
                    ).fetchone(),
                )
                with self.assertRaises(sqlite3.IntegrityError):
                    self.database.execute("UPDATE " + table + " SET revision_token=NULL WHERE seq=99")
                self.assertEqual(
                    (token,),
                    self.database.execute("SELECT revision_token FROM " + table + " WHERE seq=99").fetchone(),
                )

    def test_retains_foreign_keys_and_connection_settings_with_both_fk_modes(self):
        for enabled in (0, 1):
            with self.subTest(foreign_keys=enabled):
                database = create_revision_database(enabled)
                try:
                    columns = table_columns(database)
                    rows = physical_rows(database, columns)
                    foreign_keys = {
                        table: database.execute("PRAGMA foreign_key_list(" + identifier(table) + ")").fetchall()
                        for table in columns
                    }
                    database.executescript(MIGRATION)
                    self.assertEqual((73,), database.execute("PRAGMA user_version").fetchone())
                    self.assertEqual((enabled,), database.execute("PRAGMA foreign_keys").fetchone())
                    self.assertEqual(rows, physical_rows(database, columns))
                    for table, expected in foreign_keys.items():
                        self.assertEqual(
                            expected,
                            database.execute("PRAGMA foreign_key_list(" + identifier(table) + ")").fetchall(),
                        )
                    self.assertEqual([], database.execute("PRAGMA foreign_key_check").fetchall())
                finally:
                    database.close()

    def test_reapplication_fails_without_changing_rows_tokens_or_schema(self):
        self.migrate()
        self.database.execute("UPDATE t_receipts SET revision_token='saved-token' WHERE seq=1")
        self.database.commit()
        columns = table_columns(self.database)
        rows = physical_rows(self.database, columns)
        schema = self.database.execute("SELECT * FROM sqlite_master ORDER BY name").fetchall()
        with self.assertRaisesRegex(sqlite3.OperationalError, "duplicate column name"):
            self.database.executescript(MIGRATION)
        self.database.rollback()
        self.assertFalse(self.database.in_transaction)
        self.assertEqual(columns, table_columns(self.database))
        self.assertEqual(rows, physical_rows(self.database, columns))
        self.assertEqual(schema, self.database.execute("SELECT * FROM sqlite_master ORDER BY name").fetchall())


if __name__ == "__main__":
    unittest.main()
