Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Linq
Imports System.Text

Friend Module PaymentMemoTests
    Public Sub BatchRoundTrips()
        For Each foreignKeys In New Boolean() {True, False}
            Using fixture As New DatabaseFixture(foreignKeys)
                SeedMemos(fixture)
                Dim unrelated = MemoRows(fixture, 4)
                Dim changes As New List(Of PaymentMemo) From {
                    MakeMemo(1, New Date(2026, 10, 20, 13, 14, 15), "メモ '引用'" & vbCrLf & "2行目"),
                    MakeMemo(2, Nothing, String.Empty),
                    MakeMemo(3, New Date(2026, 10, 21, 9, 8, 7), Nothing)
                }
                fixture.Data.RegisterPaymentMemo(changes)
                AssertSaved(fixture, changes)
                Equal(4L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_payment_memo")))
                Equal(unrelated, MemoRows(fixture, 4), "一括登録の対象外のメモを保持する")

                ' 日時をクリアする更新と、NULLから日時への更新を両方確認する。
                changes(0).CheckDate = Nothing
                changes(0).Tag = Nothing
                changes(1).CheckDate = New Date(2026, 10, 22, 6, 5, 4)
                changes(1).Tag = "更新 'メモ'"
                changes(2).CheckDate = Nothing
                changes(2).Tag = String.Empty
                fixture.Data.RegisterPaymentMemo(changes)
                AssertSaved(fixture, changes)
                Equal(4L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_payment_memo")))
                Equal(unrelated, MemoRows(fixture, 4), "再更新でも対象外のメモを保持する")
                AssertForeignKeys(fixture, foreignKeys)
            End Using
        Next
    End Sub

    Public Sub BatchRollback()
        For Each foreignKeys In New Boolean() {True, False}
            For Each failUpdate In New Boolean() {False, True}
                Using fixture As New DatabaseFixture(foreignKeys)
                    SeedMemos(fixture)
                    If failUpdate Then
                        fixture.Execute("INSERT INTO t_payment_memo(payment_classification_code,check_date,tag) " &
                            "VALUES(3,NULL,'original last memo');")
                    End If
                    Dim changes As New List(Of PaymentMemo) From {
                        MakeMemo(1, Nothing, "changed first memo"),
                        MakeMemo(2, New Date(2026, 10, 23, 14, 15, 16), "inserted middle memo"),
                        MakeMemo(3, New Date(2026, 10, 24, 17, 18, 19), "changed last memo")
                    }
                    ' 末尾行へのINSERT/UPDATE時点で、先行する更新と追加が実行済みと保証する。
                    fixture.Execute("CREATE TRIGGER fail_last_memo BEFORE " & If(failUpdate, "UPDATE", "INSERT") &
                        " ON t_payment_memo WHEN NEW.payment_classification_code=3 BEGIN " &
                        "SELECT CASE WHEN " &
                        "(SELECT tag FROM t_payment_memo WHERE payment_classification_code=1)='changed first memo' AND " &
                        "(SELECT check_date FROM t_payment_memo WHERE payment_classification_code=1) IS NULL AND " &
                        "(SELECT tag FROM t_payment_memo WHERE payment_classification_code=2)='inserted middle memo' " &
                        "THEN RAISE(ABORT,'expected late memo failure') " &
                        "ELSE RAISE(ABORT,'preceding memo changes were not reached') END; END;")
                    Dim before = MemoRows(fixture)
                    ExpectLateFailure(Sub() fixture.Data.RegisterPaymentMemo(changes))
                    Equal(before, MemoRows(fixture), "失敗時は先行する更新・追加も含め全メモを元に戻す")
                    AssertForeignKeys(fixture, foreignKeys)

                    ' 失敗したトランザクションを残さず、同じ接続で改めて保存できる。
                    fixture.Execute("DROP TRIGGER fail_last_memo;")
                    Dim unrelated = MemoRows(fixture, 4)
                    fixture.Data.RegisterPaymentMemo(changes)
                    AssertSaved(fixture, changes)
                    Equal(4L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_payment_memo")))
                    Equal(unrelated, MemoRows(fixture, 4), "再保存でも対象外のメモを保持する")
                    AssertForeignKeys(fixture, foreignKeys)
                End Using
            Next
        Next
    End Sub

    Public Sub EmptyBatch()
        For Each foreignKeys In New Boolean() {True, False}
            Using fixture As New DatabaseFixture(foreignKeys)
                SeedMemos(fixture)
                Dim before = MemoRows(fixture)
                fixture.Data.RegisterPaymentMemo(New List(Of PaymentMemo))
                Equal(before, MemoRows(fixture), "空リストで既存のメモを変更しない")
                AssertForeignKeys(fixture, foreignKeys)

                Dim changes As New List(Of PaymentMemo) From {MakeMemo(2, Nothing, "after empty batch")}
                fixture.Data.RegisterPaymentMemo(changes)
                AssertSaved(fixture, changes)
                Equal(3L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_payment_memo")))
            End Using
        Next
    End Sub

    Private Sub SeedMemos(fixture As DatabaseFixture)
        fixture.Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) " &
            "VALUES(2,'card',2,1),(3,'bank',3,1),(4,'unrelated cash',4,1);" &
            "INSERT INTO t_payment_memo(payment_classification_code,check_date,tag) " &
            "VALUES(1,'2026-10-16 01:02:03','original first memo')," &
            "(4,'2026-10-15 04:05:06','unrelated memo');")
    End Sub

    Private Function MakeMemo(code As Integer, checkDate As Date?, tag As String) As PaymentMemo
        Return New PaymentMemo With {
            .PaymentClassification = New PaymentClassification With {.Code = code},
            .CheckDate = checkDate,
            .Tag = tag
        }
    End Function

    Private Sub AssertSaved(fixture As DatabaseFixture, expected As List(Of PaymentMemo))
        Dim before = MemoRows(fixture)
        Dim loaded = fixture.Data.GetPaymentMemoList()
        Equal(4, loaded.Count, "全支払方法のメモを読み込める")
        For Each memo In expected
            Dim actual = loaded.Single(Function(value) value.PaymentClassification.Code = memo.PaymentClassification.Code)
            Equal(memo.CheckDate, actual.CheckDate, "確認日時が往復する")
            Equal(memo.Tag, actual.Tag, "タグのNULL・空文字・文字列を区別して保持する")
            Dim filter = " FROM t_payment_memo WHERE payment_classification_code=" &
                memo.PaymentClassification.Code.ToString(CultureInfo.InvariantCulture)
            Equal(If(memo.CheckDate.HasValue, 0L, 1L), Convert.ToInt64(fixture.Scalar("SELECT check_date IS NULL" & filter)))
            Equal(If(memo.Tag Is Nothing, 1L, 0L), Convert.ToInt64(fixture.Scalar("SELECT tag IS NULL" & filter)))
        Next
        Equal(before, MemoRows(fixture), "読込では保存値を変更しない")
    End Sub

    Private Sub ExpectLateFailure(action As Action)
        Try
            action()
        Catch ex As SQLiteException
            Equal(True, ex.Message.Contains("expected late memo failure"), "末尾のSQLiteエラーが呼出元に伝わる: " & ex.Message)
            Return
        End Try
        Throw New Exception("Expected SQLite failure at the last payment memo")
    End Sub

    Private Sub AssertForeignKeys(fixture As DatabaseFixture, enabled As Boolean)
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = "PRAGMA foreign_keys"
            Equal(If(enabled, 1L, 0L), Convert.ToInt64(command.ExecuteScalar()), "接続の外部キー設定を変更しない")
            command.CommandText = "PRAGMA foreign_key_check"
            Using reader = command.ExecuteReader()
                Equal(False, reader.Read(), "外部キーの整合性を保つ")
            End Using
        End Using
    End Sub

    Private Function MemoRows(fixture As DatabaseFixture, Optional code As Integer? = Nothing) As String
        Dim result As New StringBuilder()
        Using command = fixture.ActualDataConnection.CreateCommand()
            ' quoteでDB上の値を文字列として取り、NULLと空文字も区別する。
            command.CommandText = "SELECT payment_classification_code,quote(check_date),quote(tag) FROM t_payment_memo"
            If code.HasValue Then
                command.CommandText &= " WHERE payment_classification_code=@code"
                command.Parameters.AddWithValue("code", code.Value)
            End If
            command.CommandText &= " ORDER BY payment_classification_code"
            Using reader = command.ExecuteReader()
                While reader.Read()
                    For column As Integer = 0 To reader.FieldCount - 1
                        Dim value = Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture)
                        result.Append(value.Length).Append(":"c).Append(value).Append(";"c)
                    Next
                    result.AppendLine()
                End While
            End Using
        End Using
        Return result.ToString()
    End Function
End Module
