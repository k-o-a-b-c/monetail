Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Text

Friend Module BalanceForeignKeyTests
    Public Sub DeleteChildren()
        For Each foreignKeys In New Boolean() {True, False}
            For Each withChildren In New Boolean() {False, True}
                Using fixture As New DatabaseFixture(foreignKeys)
                    SeedRows(fixture, withChildren)
                    AssertIntegrity(fixture, foreignKeys)
                    Dim unaffected = DatabaseSnapshot(fixture, excludedBalance:=1)
                    fixture.Data.DeleteBalance(New Balance With {.Seq = 1})
                    AssertDeleted(fixture, unaffected, foreignKeys)
                End Using
            Next
        Next
    End Sub

    Public Sub DeleteRollback()
        For Each foreignKeys In New Boolean() {True, False}
            Using fixture As New DatabaseFixture(foreignKeys)
                SeedRows(fixture, withChildren:=True)
                Dim balance As New Balance With {.Seq = 1}
                Dim unaffected = DatabaseSnapshot(fixture, excludedBalance:=1)
                ' 子の削除に到達したことを確認してから、親の削除を失敗させる。
                fixture.Execute("CREATE TRIGGER fail_balance_delete BEFORE DELETE ON t_balances WHEN OLD.seq=1 BEGIN " &
                    "SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM t_balance_groups WHERE balance_seq=OLD.seq) " &
                    "THEN RAISE(ABORT,'expected late balance delete failure') " &
                    "ELSE RAISE(ABORT,'balance delete ran before child deletion') END; END;")
                Dim before = DatabaseSnapshot(fixture)
                AssertIntegrity(fixture, foreignKeys)
                ExpectLateFailure(Sub() fixture.Data.DeleteBalance(balance))
                Equal(before, DatabaseSnapshot(fixture), "Failed balance delete must restore every table and schema entry")
                AssertIntegrity(fixture, foreignKeys)

                ' 同じ接続で再試行でき、復元された子も削除できることを確認する。
                fixture.Execute("DROP TRIGGER fail_balance_delete")
                fixture.Data.DeleteBalance(balance)
                AssertDeleted(fixture, unaffected, foreignKeys)
            End Using
        Next
    End Sub

    Private Sub SeedRows(fixture As DatabaseFixture, withChildren As Boolean)
        fixture.Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'bank',2,1);" &
            "INSERT INTO t_balances(seq,date,tag,update_user,update_datetime) " &
            "VALUES(1,'2026-10-16','target','test','2026-10-16'),(2,'2026-10-17','neighbor','test','2026-10-17');" &
            "INSERT INTO t_balance_groups(balance_seq,payment_classification_code,balance,valuation_amount,tag) " &
            "VALUES(2,1,301,NULL,'neighbor cash'),(2,2,402,450,'neighbor bank');" &
            "INSERT INTO m_point_classification(code,name,seq,default_rate) VALUES(1,'points',1,0.01);" &
            "INSERT INTO t_receipt_points(receipt_seq,seq,code,rate,fixed_points,total_points) VALUES(1,1,1,0.01,2,3);" &
            "INSERT INTO t_transfers(seq,date,name,fee,from_payment_classification_code,to_payment_classification_code,tag,update_user,update_datetime) " &
            "VALUES(1,'2026-10-16','transfer',25,1,2,'unchanged','test','2026-10-16');" &
            "INSERT INTO t_payment_memo(payment_classification_code,check_date,tag) VALUES(1,'2026-10-16','unchanged');" &
            "INSERT INTO t_exchange_rates(rate_date,base_currency,quote_currency,middle_rate) VALUES('2026-10-16','USD','JPY',150.5);")
        If withChildren Then
            fixture.Execute("INSERT INTO t_balance_groups(balance_seq,payment_classification_code,balance,valuation_amount,tag) " &
                "VALUES(1,1,101,NULL,'target cash'),(1,2,202,250,'target bank');")
        End If
    End Sub

    Private Sub AssertDeleted(fixture As DatabaseFixture, unaffected As String, foreignKeys As Boolean)
        Equal(0L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_balances WHERE seq=1")))
        Equal(0L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_balance_groups WHERE balance_seq=1")))
        Equal(unaffected, DatabaseSnapshot(fixture, excludedBalance:=1), "Other balances and every unrelated table must be unchanged")
        AssertIntegrity(fixture, foreignKeys)
    End Sub

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

    Private Sub ExpectLateFailure(action As Action)
        Const expectedMessage As String = "expected late balance delete failure"
        Try
            action()
        Catch ex As SQLiteException
            Equal(True, ex.Message.Contains(expectedMessage), "Expected the targeted late trigger failure, got " & ex.Message)
            Return
        End Try
        Throw New Exception("Expected SQLite failure: " & expectedMessage)
    End Sub

    Private Function DatabaseSnapshot(fixture As DatabaseFixture, Optional excludedBalance As Integer? = Nothing) As String
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
            If excludedBalance.HasValue Then
                If table = "t_balances" Then
                    query &= " WHERE seq<>" & excludedBalance.Value.ToString(CultureInfo.InvariantCulture)
                ElseIf table = "t_balance_groups" Then
                    query &= " WHERE balance_seq<>" & excludedBalance.Value.ToString(CultureInfo.InvariantCulture)
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
