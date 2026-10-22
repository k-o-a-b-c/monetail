Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Numerics
#If NETFRAMEWORK Then
Imports System.Data.SQLite
#Else
Imports Microsoft.Data.Sqlite
#End If

Module Program
    Private passed As Integer

    Sub Main()
#If Not NETFRAMEWORK Then
        SQLitePCL.raw.SetProvider(New SQLitePCL.SQLite3Provider_e_sqlite3())
#End If
        Check("exact rate precision and integer arithmetic", AddressOf ExactArithmetic)
        Check("all old rows except planned changes survive", AddressOf PreservedRows)
        Check("rate totals and signed Decimal corrections", AddressOf MonetaryCases)
        Check("same class keys users empty groups and memo contents", AddressOf MergedKeys)
        Check("NULL empty and single group notes", AddressOf Notes)
        Check("transaction rollback and same connection recovery", AddressOf Rollback)
        Check("reapplication rejects without changes", AddressOf Reapplication)
        Check("missing tax orphan flags and invalid rates reject", AddressOf InvalidData)
        Check("unknown columns triggers views and indexes are protected", AddressOf CustomSchema)
        Check("new schema constraints and distinct excluded code", AddressOf Constraints)
        Check("all default categories and no invented periods", AddressOf Categories)
        Check("existing caller transaction is preserved", AddressOf CallerTransaction)
        Check("copy report existing files source WAL and failure safety", AddressOf CopyFiles)
        Console.WriteLine(passed.ToString(CultureInfo.InvariantCulture) & "/13 checks passed.")
    End Sub

    Private Sub Check(name As String, test As Action)
        test()
        passed += 1
        Console.WriteLine("PASS " & name)
    End Sub

    Private Sub Equal(expected As Object, actual As Object)
        If Convert.ToString(expected, CultureInfo.InvariantCulture) <> Convert.ToString(actual, CultureInfo.InvariantCulture) Then
            Throw New Exception("expected: " & Convert.ToString(expected, CultureInfo.InvariantCulture) & "; actual: " & Convert.ToString(actual, CultureInfo.InvariantCulture))
        End If
    End Sub

    Private Sub Reject(test As Action)
        Try
            test()
        Catch
            Return
        End Try
        Throw New Exception("Expected rejection")
    End Sub

    Private Function Database(Optional foreignKeys As Integer = 1) As DbConnection
#If NETFRAMEWORK Then
        Dim connection As DbConnection = New SQLiteConnection("Data Source=:memory:;Version=3;")
#Else
        Dim connection As DbConnection = New SqliteConnection("Data Source=:memory:")
#End If
        connection.Open()
        DetailTaxMigration.Execute(connection, "PRAGMA foreign_keys=" & foreignKeys.ToString(CultureInfo.InvariantCulture))
        For Each fileName In {"schema.sql", "rounding.sql", "revision.sql"}
            DetailTaxMigration.Execute(connection, File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)))
        Next
        DetailTaxMigration.Execute(connection,
            "PRAGMA user_version=73;" &
            "INSERT INTO m_user VALUES(1,'A','a',1),(2,'B','b',2);" &
            "INSERT INTO m_point_classification VALUES(1,'Point',1,1.25);" &
            "INSERT INTO m_payment_classification VALUES(1,'Cash',1,1,1,NULL,NULL),(2,'Card',2,NULL,2,'2020-01-01','2030-12-31');" &
            "INSERT INTO m_group_classification VALUES(1,'Food',1,1,0),(2,'Other',2,1,1),(3,'Excluded',3,0,0);" &
            "INSERT INTO m_shop(code,name,default_group_classification_code) VALUES(1,'Shop',1);" &
            "INSERT INTO m_tax_rate VALUES(0,0,NULL),(4,10,'2019-10-01'),(5,8,'2019-10-01'),(8,10,'2020-01-01');" &
            "INSERT INTO t_receipts(seq,date,shop_name,tax_type,payment_classification_code,update_user,update_datetime,total,revision_token) VALUES(1,'2026-10-01','Shop',1,1,'a','2026-10-01',999,'saved');" &
            "INSERT INTO t_receipt_groups VALUES(1,2,1,'same ',4),(1,4,1,'same ',8),(1,6,2,NULL,5),(1,8,3,'',0);" &
            "INSERT INTO t_receipt_details VALUES(1,2,1,'Item A',19,1,0,0,1,NULL),(1,4,1,'Item B',19,1,0,0,1,'日本語'),(1,4,2,'Excluded',1000,1,0,0,0,''),(1,4,3,'Refund',-100,1,0,0,1,NULL);" &
            "INSERT INTO t_receipt_detail_users VALUES(1,2,1,1),(1,2,1,2),(1,4,1,1),(1,4,1,2);" &
            "INSERT INTO t_receipt_points VALUES(1,1,1,1.25,2,3);" &
            "INSERT INTO t_transfers VALUES(1,'2026-10-01','Transfer',200,1,2,NULL,'a','2026-10-01','transfer-token');" &
            "INSERT INTO t_balances VALUES(1,'2026-10-01',NULL,'a','2026-10-01','balance-token');" &
            "INSERT INTO t_balance_groups VALUES(1,1,5000,NULL,5000);" &
            "INSERT INTO t_payment_memo VALUES(1,'2026-10-01','Checked');" &
            "INSERT INTO t_exchange_rates VALUES('2026-10-01','USD','JPY',150.25);")
        Return connection
    End Function

    Private Function Snapshot(connection As DbConnection) As String
        Dim result As New List(Of String)
        For Each row As System.Data.DataRow In DetailTaxMigration.Read(connection, "SELECT name,sql FROM sqlite_master ORDER BY name").Rows
            result.Add(CStr(row("name")) & ":" & Convert.ToString(row("sql"), CultureInfo.InvariantCulture))
            If CStr(row("name")).StartsWith("sqlite_", StringComparison.Ordinal) Then Continue For
            If row.IsNull("sql") OrElse Not CStr(row("sql")).StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase) Then Continue For
            result.Add(TableSnapshot(connection, CStr(row("name"))))
        Next
        result.Add(Convert.ToString(DetailTaxMigration.Scalar(connection, "PRAGMA foreign_keys"), CultureInfo.InvariantCulture))
        result.Add(Convert.ToString(DetailTaxMigration.Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture))
        Return String.Join(vbLf, result)
    End Function

    Private Function TableSnapshot(connection As DbConnection, table As String, ParamArray except As String()) As String
        Dim columns = DetailTaxMigration.Read(connection, "PRAGMA table_info(" & table & ")").Rows.Cast(Of System.Data.DataRow)().Select(Function(r) CStr(r("name"))).Where(Function(n) Not except.Contains(n))
        Dim sql = "SELECT " & String.Join(",", columns.Select(Function(c) "typeof(" & c & ")||':'||quote(" & c & ") AS " & c)) & " FROM " & table & " ORDER BY rowid"
        Return String.Join(vbLf, DetailTaxMigration.Read(connection, sql).Rows.Cast(Of System.Data.DataRow)().Select(Function(r) String.Join("|", r.ItemArray.Select(Function(v) CStr(v)))))
    End Function

    Private Sub ExactArithmetic()
        For Each culture In {"en-US", "de-DE"}
            Dim previous = CultureInfo.CurrentCulture
            Try
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture)
                Equal("0.14", ExactMoney.RateText(ExactMoney.ParseRate("0.14")))
                Equal("9986", ExactMoney.Net(10000, 0, 1, ExactMoney.ParseRate("0.14")))
                Equal("998765432", ExactMoney.Net(1000000000, 0, 1, ExactMoney.ParseRate("0.1234567890123456789012345678")))
                Equal("-998765433", ExactMoney.Net(-1000000000, 0, 1, ExactMoney.ParseRate("0.1234567890123456789012345678")))
                Equal("-110", ExactMoney.Taxed(-100, 10, 1, "floor"))
                Equal("-6", ExactMoney.Taxed(-5, 10, 1, "half_up"))
                Equal("-6", ExactMoney.Taxed(-5, 10, 1, "floor"))
                Equal("79228162514264337593543950335", ExactMoney.RateText(ExactMoney.ParseRate("79228162514264337593543950335")))
                Equal("0.0000000000000000000000000001", ExactMoney.ParseRate("1e-28").ToString("F28", CultureInfo.InvariantCulture))
                Equal("0.3", ExactMoney.RateText(ExactMoney.ParseRate("0.30000000000000000000000000000")))
                For Each invalid In {"1e-29", "79228162514264337593543950336", "0.10000000000000000000000000001", "NaN", "Infinity", "0,14", "", "1e99999999999"}
                    Reject(Sub() ExactMoney.ParseRate(invalid))
                Next
                Equal(New BigInteger(Integer.MaxValue) * Integer.MaxValue, ExactMoney.Net(Integer.MaxValue, 0, Integer.MaxValue, 0D))
            Finally
                CultureInfo.CurrentCulture = previous
            End Try
        Next
    End Sub

    Private Sub PreservedRows()
        For Each enabled In {0, 1}
            Using db = Database(enabled)
                Console.WriteLine("INFO " & db.GetType().Assembly.GetName().Name & " SQLite=" & CStr(DetailTaxMigration.Scalar(db, "SELECT sqlite_version()")) & " FK=" & enabled.ToString(CultureInfo.InvariantCulture))
                Dim names = {"m_user", "m_shop", "m_payment_classification", "m_point_classification", "t_receipt_points", "t_transfers", "t_balances", "t_balance_groups", "t_payment_memo", "t_exchange_rates"}
                Dim before = names.ToDictionary(Function(n) n, Function(n) TableSnapshot(db, n))
                Dim headerBefore = TableSnapshot(db, "t_receipts", "total", "revision_token")
                DetailTaxMigration.Run(db)
                Equal(headerBefore, TableSnapshot(db, "t_receipts", "total", "revision_token"))
                For Each name In names
                    Equal(before(name), TableSnapshot(db, name))
                Next
                Equal(73, DetailTaxMigration.Scalar(db, "PRAGMA user_version"))
                Equal(enabled, DetailTaxMigration.Scalar(db, "PRAGMA foreign_keys"))
                Equal("a|2026-10-01", DetailTaxMigration.Scalar(db, "SELECT update_user||'|'||update_datetime FROM t_receipts"))
                Equal("2019-10-01", DetailTaxMigration.Scalar(db, "SELECT CAST(start_date AS TEXT) FROM m_tax_rate WHERE code=4"))
                Equal(999, DetailTaxMigration.Scalar(db, "SELECT stored_total FROM detail_tax_receipt_audit"))
                Equal("saved", DetailTaxMigration.Scalar(db, "SELECT old_revision FROM detail_tax_receipt_audit"))
                Equal(1, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM t_receipts WHERE revision_token<>'saved' AND total=(SELECT new_total FROM detail_tax_receipt_audit)"))
                Equal(0, DetailTaxMigration.Read(db, "PRAGMA foreign_key_check").Rows.Count)
                Equal("ok", DetailTaxMigration.Scalar(db, "PRAGMA integrity_check"))
            End Using
        Next
        ' 空のDB・空レシートでも移行済み記録と更新版を作る。
        Using db = Database()
            DetailTaxMigration.Execute(db, "DELETE FROM t_receipt_detail_users;DELETE FROM t_receipt_details;DELETE FROM t_receipt_groups")
            DetailTaxMigration.Run(db)
            Equal(0, DetailTaxMigration.Scalar(db, "SELECT total FROM t_receipts"))
        End Using
    End Sub

    Private Sub MonetaryCases()
        Dim cases = {
            New Object() {19, 19, 0, 0D, 1, "floor", 41, 41, 0, 41},
            New Object() {-100, 0, 1000, 0D, 1, "floor", 890, 890, -110, 1000},
            New Object() {-100, 0, 0, 0D, 1, "floor", -111, -110, -110, 0},
            New Object() {0, 0, 10000, 0.14D, 1, "floor", 9985, 9986, 0, 9986},
            New Object() {19, 19, 0, 0D, 1, "half_up", 42, 42, 0, 42},
            New Object() {19, 19, 1000, 0D, 0, "floor", 1038, 1038, 0, 1038}}
        For Each test In cases
            Using db = Database()
                DetailTaxMigration.Execute(db, "DELETE FROM t_receipt_detail_users;DELETE FROM t_receipt_details;UPDATE t_receipts SET tax_type=@p0,tax_rounding_mode=@p1", CInt(test(4)), CStr(test(5)))
                DetailTaxMigration.Execute(db, "INSERT INTO t_receipt_details VALUES(1,2,1,'A',@p0,1,0,0,1,NULL),(1,4,1,'B',@p1,1,0,0,1,NULL),(1,4,2,'C',@p2,1,0,@p3,0,NULL)", CInt(test(0)), CInt(test(1)), CInt(test(2)), CDbl(CDec(test(3))))
                DetailTaxMigration.Run(db)
                Equal(CInt(test(6)), DetailTaxMigration.Scalar(db, "SELECT legacy_total FROM detail_tax_receipt_audit"))
                Equal(CInt(test(7)), DetailTaxMigration.Scalar(db, "SELECT new_total FROM detail_tax_receipt_audit"))
                Equal(CInt(test(8)), DetailTaxMigration.Scalar(db, "SELECT new_incomes FROM detail_tax_receipt_audit"))
                Equal(CInt(test(9)), DetailTaxMigration.Scalar(db, "SELECT new_expenses FROM detail_tax_receipt_audit"))
                Equal("text", DetailTaxMigration.Scalar(db, "SELECT typeof(discount_rate) FROM t_receipt_details LIMIT 1"))
                Equal(ExactMoney.RateText(CDec(test(3))), DetailTaxMigration.Scalar(db, "SELECT discount_rate FROM t_receipt_details WHERE seq=3"))
            End Using
        Next
        For Each scenario In {Tuple.Create(1, "floor", 61), Tuple.Create(1, "half_up", 63), Tuple.Create(0, "floor", 57)}
            Using db = Database()
                DetailTaxMigration.Execute(db, "DELETE FROM t_receipt_detail_users;DELETE FROM t_receipt_details;UPDATE t_receipts SET tax_type=@p0,tax_rounding_mode=@p1;UPDATE t_receipt_groups SET tax=5 WHERE seq=2", scenario.Item1, scenario.Item2)
                DetailTaxMigration.Execute(db, "INSERT INTO t_receipt_details VALUES(1,2,1,'8%',19,1,0,0,1,NULL),(1,4,1,'10%',19,2,0,0,1,NULL)")
                DetailTaxMigration.Run(db)
                Equal(scenario.Item3, DetailTaxMigration.Scalar(db, "SELECT total FROM t_receipts"))
                Equal(1, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM t_receipt_groups WHERE group_classification_code=1"))
            End Using
        Next
    End Sub

    Private Sub MergedKeys()
        Using db = Database()
            DetailTaxMigration.Run(db)
            Equal(3, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM t_receipt_groups"))
            Equal(4, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM t_receipt_details WHERE group_seq=2"))
            Equal(4, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM t_receipt_detail_users WHERE group_seq=2"))
            Equal("1,2", DetailTaxMigration.Scalar(db, "SELECT group_concat(DISTINCT detail_seq) FROM t_receipt_detail_users"))
            Equal(2, DetailTaxMigration.Scalar(db, "SELECT new_seq FROM detail_tax_detail_map WHERE old_group_seq=4 AND old_seq=1"))
            Equal("【統合元 1】" & vbLf & "same " & vbLf & "【統合元 2】" & vbLf & "same ", DetailTaxMigration.Scalar(db, "SELECT tag FROM t_receipt_groups WHERE seq=2"))
            Equal(4, DetailTaxMigration.Scalar(db, "SELECT tax_code FROM t_receipt_details WHERE seq=1"))
            Equal(8, DetailTaxMigration.Scalar(db, "SELECT tax_code FROM t_receipt_details WHERE seq=2"))
        End Using
        Using db = Database()
            DetailTaxMigration.Execute(db,
                "INSERT INTO t_receipts(seq,date,shop_name,tax_type,payment_classification_code,update_user,update_datetime,total) VALUES(2,'2026-10-02','Other',0,2,'b','2026-10-02',NULL);" &
                "INSERT INTO t_receipt_groups VALUES(2,2,1,NULL,5);" &
                "INSERT INTO t_receipt_details VALUES(2,2,1,'Other parent',7,1,0,0,1,NULL);" &
                "INSERT INTO t_receipt_detail_users VALUES(2,2,1,2)")
            DetailTaxMigration.Run(db)
            Equal(7, DetailTaxMigration.Scalar(db, "SELECT total FROM t_receipts WHERE seq=2"))
            Equal(1, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM t_receipt_detail_users WHERE receipt_seq=2 AND group_seq=2 AND detail_seq=1 AND user_code=2"))
            Equal(True, DetailTaxMigration.Scalar(db, "SELECT stored_total FROM detail_tax_receipt_audit WHERE receipt_seq=2") Is DBNull.Value)
        End Using
    End Sub

    Private Sub Notes()
        For Each noteCase In {Tuple.Create(CObj(DBNull.Value), CObj(DBNull.Value), CObj(DBNull.Value)), Tuple.Create(CObj(DBNull.Value), CObj(""), CObj("")), Tuple.Create(CObj("  "), CObj(""), CObj("【統合元 1】" & vbLf & "  "))}
            Using db = Database()
                DetailTaxMigration.Execute(db, "UPDATE t_receipt_groups SET tag=@p0 WHERE seq=2;UPDATE t_receipt_groups SET tag=@p1 WHERE seq=4", noteCase.Item1, noteCase.Item2)
                DetailTaxMigration.Run(db)
                Equal(noteCase.Item3 Is DBNull.Value, DetailTaxMigration.Scalar(db, "SELECT tag FROM t_receipt_groups WHERE seq=2") Is DBNull.Value)
                Equal(noteCase.Item3, DetailTaxMigration.Scalar(db, "SELECT tag FROM t_receipt_groups WHERE seq=2"))
                Equal(True, DetailTaxMigration.Scalar(db, "SELECT tag FROM t_receipt_groups WHERE seq=6") Is DBNull.Value)
                Equal("", DetailTaxMigration.Scalar(db, "SELECT tag FROM t_receipt_groups WHERE seq=8"))
            End Using
        Next
    End Sub

    Private Sub Rollback()
        For Each enabled In {0, 1}
            For Each stage In {"created", "copied", "replaced", "validated"}
                Using db = Database(enabled)
                    Dim before = Snapshot(db)
                    Reject(Sub() DetailTaxMigration.Run(db, Sub(current)
                                                               If current = stage Then Throw New Exception("Injected")
                                                           End Sub))
                    Equal(before, Snapshot(db))
                    DetailTaxMigration.Run(db)
                    Equal(1, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM detail_tax_migration"))
                End Using
            Next
        Next
    End Sub

    Private Sub Reapplication()
        Using db = Database()
            DetailTaxMigration.Run(db)
            Dim before = Snapshot(db)
            Reject(Sub() DetailTaxMigration.Run(db))
            Equal(before, Snapshot(db))
        End Using
    End Sub

    Private Sub InvalidData()
        For Each sql In {"UPDATE t_receipt_groups SET tax=99", "UPDATE t_receipt_details SET discount_rate='NaN'", "UPDATE t_receipt_details SET discount_rate='0.10000000000000000000000000001x'", "UPDATE t_receipt_details SET discount_rate=1e-29", "UPDATE t_receipt_details SET tax_target=2", "UPDATE t_receipt_details SET fee=1.5", "UPDATE t_receipt_details SET fee=2147483648", "UPDATE t_receipt_details SET fee=2147483647,quantity=2147483647", "UPDATE t_receipt_detail_users SET user_code=99 WHERE user_code=1", "UPDATE t_receipts SET tax_rounding_mode='other'", "UPDATE t_receipts SET tax_type=3"}
            Using db = Database(0)
                If sql.Contains("tax_rounding_mode") Then DetailTaxMigration.Execute(db, "PRAGMA ignore_check_constraints=ON")
                DetailTaxMigration.Execute(db, sql)
                Dim before = Snapshot(db)
                Reject(Sub() DetailTaxMigration.Run(db))
                Equal(before, Snapshot(db))
            End Using
        Next
    End Sub

    Private Sub CustomSchema()
        For Each sql In {"ALTER TABLE t_receipt_details ADD COLUMN custom TEXT", "CREATE TRIGGER custom AFTER UPDATE ON t_receipts BEGIN SELECT 1;END", "CREATE VIEW custom AS SELECT * FROM t_receipt_groups", "CREATE INDEX custom ON t_receipt_groups(tax)", "CREATE TABLE custom(receipt_seq INTEGER,group_seq INTEGER,FOREIGN KEY(receipt_seq,group_seq) REFERENCES t_receipt_groups(receipt_seq,seq))"}
            Using db = Database()
                DetailTaxMigration.Execute(db, sql)
                Dim before = Snapshot(db)
                Reject(Sub() DetailTaxMigration.Run(db))
                Equal(before, Snapshot(db))
            End Using
        Next
        Using db = Database()
            DetailTaxMigration.Execute(db, "CREATE INDEX custom ON t_receipt_details(name)")
            DetailTaxMigration.Run(db)
            Equal(1, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='custom'"))
        End Using
    End Sub

    Private Sub Constraints()
        Using db = Database()
            DetailTaxMigration.Execute(db, "INSERT INTO t_receipt_details VALUES(1,8,1,'Zero taxable',10,1,0,0,1,NULL),(1,8,2,'Excluded',10,1,0,0,0,NULL)")
            DetailTaxMigration.Run(db)
            Dim tables = DetailTaxMigration.Read(db, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").Rows.Cast(Of System.Data.DataRow)().Select(Function(r) CStr(r("name"))).ToList()
            Equal(22, tables.Count)
            Equal(122, tables.Sum(Function(name) DetailTaxMigration.Read(db, "PRAGMA table_info(" & name & ")").Rows.Count))
            Equal(21, tables.Sum(Function(name) DetailTaxMigration.Read(db, "PRAGMA foreign_key_list(" & name & ")").Rows.Cast(Of System.Data.DataRow)().Select(Function(r) CInt(r("id"))).Distinct().Count()))
            Equal(0, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('t_receipt_groups') WHERE name='tax'"))
            Equal(0, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('t_receipt_details') WHERE name='tax_target'"))
            Equal(0, DetailTaxMigration.Scalar(db, "SELECT rate FROM m_tax_rate WHERE code=0 AND tax_treatment='taxable'"))
            Equal(1, DetailTaxMigration.Scalar(db, "SELECT code FROM m_tax_rate WHERE tax_treatment='out_of_scope'"))
            Equal(0, DetailTaxMigration.Scalar(db, "SELECT tax_code FROM t_receipt_details WHERE name='Zero taxable'"))
            Equal(1, DetailTaxMigration.Scalar(db, "SELECT tax_code FROM t_receipt_details WHERE name='Excluded' AND group_seq=8"))
            For Each sql In {"INSERT INTO t_receipt_groups VALUES(1,99,1,NULL)", "UPDATE t_receipt_details SET tax_code=99", "UPDATE m_tax_rate SET rate=1 WHERE tax_treatment='out_of_scope'", "INSERT INTO m_tax_category_rule VALUES(1,'invalid',4)", "INSERT INTO m_tax_category_rule VALUES(1,'2030-02-31',4)", "INSERT INTO m_tax_category_rule VALUES(99,'2030-01-01',4)"}
                Reject(Sub() DetailTaxMigration.Execute(db, sql))
            Next
        End Using
    End Sub

    Private Sub Categories()
        Using db = Database()
            DetailTaxMigration.Run(db)
            Equal("1,2,3", DetailTaxMigration.Scalar(db, "SELECT group_concat(default_tax_category_code) FROM m_group_classification ORDER BY code"))
            Equal(0, DetailTaxMigration.Scalar(db, "SELECT COUNT(*) FROM m_tax_category_rule"))
            DetailTaxMigration.Execute(db, "INSERT INTO m_tax_category VALUES(99,'Additional');INSERT INTO m_tax_category_rule VALUES(99,'2030-01-01',4),(99,'2030-02-01',5),(99,'2030-03-01',4)")
            Equal(5, DetailTaxMigration.Scalar(db, "SELECT tax_code FROM m_tax_category_rule WHERE tax_category_code=99 AND start_date<='2030-02-28' ORDER BY start_date DESC LIMIT 1"))
        End Using
    End Sub

    Private Sub CallerTransaction()
        For Each enabled In {0, 1}
            Using db = Database(enabled)
                DetailTaxMigration.Execute(db, "BEGIN;UPDATE t_receipts SET tag='pending'")
                Reject(Sub() DetailTaxMigration.Run(db))
                Equal("pending", DetailTaxMigration.Scalar(db, "SELECT tag FROM t_receipts"))
                DetailTaxMigration.Execute(db, "ROLLBACK")
                Equal(True, DetailTaxMigration.Scalar(db, "SELECT tag FROM t_receipts") Is DBNull.Value)
                Equal(enabled, DetailTaxMigration.Scalar(db, "PRAGMA foreign_keys"))
            End Using
        Next
    End Sub

    Private Sub CopyFiles()
        Dim folder = Path.Combine(Path.GetTempPath(), "monetail-detail-tax-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        Try
            Dim sourcePath = Path.Combine(folder, "source.sqlite")
            Dim copyPath = Path.Combine(folder, "copy.sqlite")
            Dim reportPath = Path.Combine(folder, "report.csv")
            Using seed = Database()
#If NETFRAMEWORK Then
                Using fileDb As New SQLiteConnection("Data Source=" & sourcePath & ";Pooling=False")
                    fileDb.Open()
                    DirectCast(seed, SQLiteConnection).BackupDatabase(fileDb, "main", "main", -1, Nothing, 0)
#Else
                Using fileDb As New SqliteConnection("Data Source=" & sourcePath & ";Pooling=False")
                    fileDb.Open()
                    DirectCast(seed, SqliteConnection).BackupDatabase(fileDb)
#End If
                    DetailTaxMigration.Execute(fileDb, "PRAGMA journal_mode=WAL;UPDATE t_receipts SET total=123,tag='WAL value'")
                    Dim before = Snapshot(fileDb)
                    MigrationFiles.MigrateCopy(sourcePath, copyPath, reportPath)
                    Equal(before, Snapshot(fileDb))
                    Equal(True, File.ReadAllText(reportPath).Contains("1,123,931,931,0,808"))
                    Dim copyBytes = Convert.ToBase64String(File.ReadAllBytes(copyPath))
                    Reject(Sub() MigrationFiles.MigrateCopy(sourcePath, copyPath, reportPath))
                    Equal(copyBytes, Convert.ToBase64String(File.ReadAllBytes(copyPath)))
                    Reject(Sub() MigrationFiles.MigrateCopy(sourcePath, sourcePath, reportPath))
                    Equal(before, Snapshot(fileDb))
                End Using
            End Using
            File.Delete(copyPath)
            Reject(Sub() MigrationFiles.MigrateCopy(sourcePath, copyPath, reportPath))
            Equal(False, File.Exists(copyPath))
            File.Delete(reportPath)
#If NETFRAMEWORK Then
            Using broken As New SQLiteConnection("Data Source=" & sourcePath & ";Pooling=False")
#Else
            Using broken As New SqliteConnection("Data Source=" & sourcePath & ";Pooling=False")
#End If
                broken.Open()
                DetailTaxMigration.Execute(broken, "UPDATE t_receipt_groups SET tax=99")
            End Using
            Reject(Sub() MigrationFiles.MigrateCopy(sourcePath, copyPath, reportPath))
            Equal(False, File.Exists(copyPath))
            Equal(False, File.Exists(reportPath))
        Finally
            Directory.Delete(folder, True)
        End Try
    End Sub
End Module
