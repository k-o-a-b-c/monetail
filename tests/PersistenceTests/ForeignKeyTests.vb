Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Linq
Imports System.Text

Friend Module ForeignKeyTests
    Public Sub UpdateChildren()
        For Each foreignKeys In New Boolean() {True, False}
            Using fixture As New DatabaseFixture(foreignKeys)
                SeedRelatedRows(fixture)
                AssertIntegrity(fixture, foreignKeys)
                Dim unaffected = DatabaseSnapshot(fixture, excludedReceipt:=1)
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                PrepareReplacement(receipt, fixture.Data.UserList)
                fixture.Data.UpdateReceipt(receipt)

                Dim saved = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                Equal("changed shop", saved.ShopName)
                Equal("changed location", saved.ShopLocation)
                Equal("updated receipt", saved.Tag)
                Equal("half_up", saved.TaxRoundingMode)
                Equal(receipt.Total, Convert.ToInt32(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                Equal(2, saved.Groups.Count)
                Equal(1, saved.Groups(0).Seq)
                Equal(8, saved.Groups(0).Details(0).TaxRate.Rate)
                Equal("empty replacement group", saved.Groups(0).Tag)
                Equal(0, saved.Groups(0).Details.Count)
                Equal(2, saved.Groups(1).Seq)
                Equal(2, saved.Groups(1).GroupClassification.Code)
                Equal(2, saved.Groups(1).Details.Count)
                Equal("replacement first", saved.Groups(1).Details(0).Name)
                Equal(1, saved.Groups(1).Details(0).Seq)
                Equal("2,3", UserCodes(saved.Groups(1).Details(0)))
                Equal("replacement second", saved.Groups(1).Details(1).Name)
                Equal(2, saved.Groups(1).Details(1).Seq)
                Equal("1", UserCodes(saved.Groups(1).Details(1)))
                Equal(2L, CountRows(fixture, "t_receipt_details", 1))
                Equal(3L, CountRows(fixture, "t_receipt_detail_users", 1))
                Equal(unaffected, DatabaseSnapshot(fixture, excludedReceipt:=1))
                AssertIntegrity(fixture, foreignKeys)

                ' Removing every group also removes all details and assignments, retaining the header.
                saved.Groups.Clear()
                fixture.Data.UpdateReceipt(saved)
                Equal(0, fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1).Groups.Count)
                Equal(0L, CountRows(fixture, "t_receipt_groups", 1))
                Equal(0L, CountRows(fixture, "t_receipt_details", 1))
                Equal(0L, CountRows(fixture, "t_receipt_detail_users", 1))
                Equal(0L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                Equal(unaffected, DatabaseSnapshot(fixture, excludedReceipt:=1))
                AssertIntegrity(fixture, foreignKeys)
            End Using
        Next
    End Sub

    Public Sub DeleteChildren()
        For Each foreignKeys In New Boolean() {True, False}
            Using fixture As New DatabaseFixture(foreignKeys)
                SeedRelatedRows(fixture)
                AssertIntegrity(fixture, foreignKeys)
                Dim unaffected = DatabaseSnapshot(fixture, excludedReceipt:=1)
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                fixture.Data.DeleteReceipt(receipt)

                Equal(0L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipts WHERE seq=1")))
                Equal(0L, CountRows(fixture, "t_receipt_groups", 1))
                Equal(0L, CountRows(fixture, "t_receipt_details", 1))
                Equal(0L, CountRows(fixture, "t_receipt_detail_users", 1))
                Equal(2, fixture.Data.GetReceipts(2026, 10).Single().Seq)
                Equal(unaffected, DatabaseSnapshot(fixture, excludedReceipt:=1))
                AssertIntegrity(fixture, foreignKeys)
            End Using
        Next
    End Sub

    Public Sub UpdateRollback()
        For Each foreignKeys In New Boolean() {True, False}
            Using fixture As New DatabaseFixture(foreignKeys)
                SeedRelatedRows(fixture)
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                PrepareReplacement(receipt, fixture.Data.UserList)
                ' The failure is reached only after the header, both groups, a detail and an
                ' assignment have been replaced. A premature FK error must not pass this test.
                fixture.Execute("CREATE TRIGGER fail_replacement_assignment BEFORE INSERT ON t_receipt_detail_users " &
                    "WHEN NEW.receipt_seq=1 AND NEW.user_code=3 BEGIN " &
                    "SELECT CASE WHEN " &
                    "(SELECT tag FROM t_receipts WHERE seq=1)='updated receipt' AND " &
                    "(SELECT count(*) FROM t_receipt_groups WHERE receipt_seq=1)=2 AND " &
                    "(SELECT count(*) FROM t_receipt_details WHERE receipt_seq=1)=1 AND " &
                    "(SELECT name FROM t_receipt_details WHERE receipt_seq=1)='replacement first' AND " &
                    "(SELECT count(*) FROM t_receipt_detail_users WHERE receipt_seq=1)=1 AND " &
                    "(SELECT user_code FROM t_receipt_detail_users WHERE receipt_seq=1)=2 " &
                    "THEN RAISE(ABORT,'expected late update failure') " &
                    "ELSE RAISE(ABORT,'replacement state was not reached') END; END;")
                Dim before = DatabaseSnapshot(fixture)
                AssertIntegrity(fixture, foreignKeys)
                ExpectLateFailure(Sub() fixture.Data.UpdateReceipt(receipt), "expected late update failure")
                Equal(before, DatabaseSnapshot(fixture), "Failed update must restore every table and schema entry")
                AssertIntegrity(fixture, foreignKeys)
            End Using
        Next
    End Sub

    Public Sub DeleteRollback()
        For Each foreignKeys In New Boolean() {True, False}
            Using fixture As New DatabaseFixture(foreignKeys)
                SeedRelatedRows(fixture)
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                ' Fail at the parent only after every targeted child row has been deleted.
                fixture.Execute("CREATE TRIGGER fail_receipt_delete BEFORE DELETE ON t_receipts WHEN OLD.seq=1 BEGIN " &
                    "SELECT CASE WHEN " &
                    "NOT EXISTS(SELECT 1 FROM t_receipt_detail_users WHERE receipt_seq=OLD.seq) AND " &
                    "NOT EXISTS(SELECT 1 FROM t_receipt_details WHERE receipt_seq=OLD.seq) AND " &
                    "NOT EXISTS(SELECT 1 FROM t_receipt_groups WHERE receipt_seq=OLD.seq) " &
                    "THEN RAISE(ABORT,'expected late delete failure') " &
                    "ELSE RAISE(ABORT,'receipt delete ran before child deletion') END; END;")
                Dim before = DatabaseSnapshot(fixture)
                AssertIntegrity(fixture, foreignKeys)
                ExpectLateFailure(Sub() fixture.Data.DeleteReceipt(receipt), "expected late delete failure")
                Equal(before, DatabaseSnapshot(fixture), "Failed delete must restore every table and schema entry")
                AssertIntegrity(fixture, foreignKeys)
            End Using
        Next
    End Sub

    Private Sub SeedRelatedRows(fixture As DatabaseFixture)
        fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'second','second-test-account',2),(3,'third','third-test-account',3);" &
            "INSERT INTO m_group_classification(code,name,seq,default_tax_category_code) VALUES(2,'other group',2,3);" &
            "INSERT INTO t_receipt_groups(receipt_seq,seq,group_classification_code,tag) VALUES(1,3,2,'original empty group');" &
            "INSERT INTO t_receipt_details(receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_code,tag) " &
            "VALUES(1,2,2,'original extra',31,2,1,5,0,'original extra tag');" &
            "INSERT INTO t_receipt_detail_users(receipt_seq,group_seq,detail_seq,user_code) " &
            "VALUES(1,1,1,2),(1,2,1,3),(1,2,2,1),(1,2,2,2);")
        fixture.AddReceipt(2, "unrelated shop", "floor", New Date(2026, 10, 17))
        ' The neighboring receipt deliberately reuses group/detail sequence numbers.
        fixture.Execute("INSERT INTO t_receipt_detail_users(receipt_seq,group_seq,detail_seq,user_code) VALUES(2,1,1,1),(2,1,1,3);")
    End Sub

    Private Sub PrepareReplacement(receipt As Receipt, users As List(Of User))
        receipt.Date = New Date(2026, 10, 18, 13, 14, 15)
        receipt.ShopName = "changed shop"
        receipt.ShopLocation = "changed location"
        receipt.Tag = "updated receipt"
        receipt.TaxRoundingMode = "half_up"
        Dim first = Detail(47, quantity:=2, discountFee:=3, discountRate:=5)
        first.Name = "replacement first"
        first.Tag = "first tag"
        first.Users.Add(users.Single(Function(u) u.Code = 2))
        first.Users.Add(users.Single(Function(u) u.Code = 3))
        Dim second = Detail(17, taxable:=False)
        second.Name = "replacement second"
        second.Tag = "second tag"
        second.Users.Add(users.Single(Function(u) u.Code = 1))
        receipt.Groups.Clear()
        Dim empty = Group(8)
        empty.Tag = "empty replacement group"
        receipt.Groups.Add(empty)
        Dim populated = Group(10, first, second)
        populated.GroupClassification.Code = 2
        populated.Tag = "populated replacement group"
        receipt.Groups.Add(populated)
    End Sub

    Private Function UserCodes(detail As ReceiptDetail) As String
        Return String.Join(",", detail.Users.Select(Function(u) u.Code).OrderBy(Function(code) code))
    End Function

    Private Function CountRows(fixture As DatabaseFixture, table As String, receiptSeq As Integer) As Long
        Return Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM " & table & " WHERE receipt_seq=" & receiptSeq.ToString(CultureInfo.InvariantCulture)))
    End Function

    Private Sub AssertIntegrity(fixture As DatabaseFixture, foreignKeys As Boolean)
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = "PRAGMA foreign_keys"
            Equal(If(foreignKeys, 1L, 0L), Convert.ToInt64(command.ExecuteScalar()), "Actual DataAccess connection FK setting")
            command.CommandText = "PRAGMA foreign_key_check"
            Using reader = command.ExecuteReader()
                Equal(False, reader.Read(), "Every foreign key must remain valid")
            End Using
        End Using
    End Sub

    Private Sub ExpectLateFailure(action As Action, expectedMessage As String)
        Try
            action()
        Catch ex As SQLiteException
            Equal(True, ex.Message.Contains(expectedMessage), "Expected the targeted late trigger failure, got " & ex.Message)
            Return
        End Try
        Throw New Exception("Expected SQLite failure: " & expectedMessage)
    End Sub

    Private Function DatabaseSnapshot(fixture As DatabaseFixture, Optional excludedReceipt As Integer? = Nothing) As String
        Dim result As New StringBuilder()
        result.Append(SnapshotQuery(fixture, "SELECT type,name,tbl_name,sql FROM sqlite_master ORDER BY type,name"))
        Dim tables As New List(Of String)()
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
            Using reader = command.ExecuteReader()
                While reader.Read()
                    tables.Add(reader.GetString(0))
                End While
            End Using
        End Using
        For Each table In tables
            result.Append(table).AppendLine()
            Dim query = "SELECT * FROM """ & table.Replace("""", """""") & """"
            If excludedReceipt.HasValue Then
                If table = "t_receipts" Then
                    query &= " WHERE seq<>" & excludedReceipt.Value.ToString(CultureInfo.InvariantCulture)
                ElseIf {"t_receipt_groups", "t_receipt_details", "t_receipt_detail_users"}.Contains(table) Then
                    query &= " WHERE receipt_seq<>" & excludedReceipt.Value.ToString(CultureInfo.InvariantCulture)
                End If
            End If
            result.Append(SnapshotQuery(fixture, query & " ORDER BY rowid"))
        Next
        Return result.ToString()
    End Function

    Private Function SnapshotQuery(fixture As DatabaseFixture, sql As String) As String
        Dim result As New StringBuilder()
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = sql
            Using reader = command.ExecuteReader()
                While reader.Read()
                    For column As Integer = 0 To reader.FieldCount - 1
                        If reader.IsDBNull(column) Then
                            result.Append("null;")
                        Else
                            Dim value = Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture)
                            result.Append(value.Length).Append(":"c).Append(value).Append(";"c)
                        End If
                    Next
                    result.AppendLine()
                End While
            End Using
        End Using
        Return result.ToString()
    End Function
End Module
