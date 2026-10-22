Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Linq
Imports ConcurrencyTests

Friend Module RevisionTests
    Public Sub StaleDeletesRejected()
        EachCase(
            Sub(fixture, second, kind)
                Dim stale = LoadItem(second, kind)
                Dim current = MakeItem(kind, "newer editor", 3, LoadItem(fixture.Data, kind))
                Save(fixture.Data, kind, current, registering:=False)
                Dim before = Snapshot(fixture)
                RejectWithoutWrites(fixture, second, kind, stale, Sub() DeleteItem(second, kind, stale))
                Equal(before, Snapshot(fixture), "Stale delete must preserve the complete first writer and every other row")
                AssertSaved(fixture, kind, current)
                ' 再読込後の最新モデルでは削除できる。
                DeleteItem(second, kind, LoadItem(second, kind))
                Equal(0L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM " & ParentTable(kind) & " WHERE seq=1")))
                AssertForeignKeys(fixture.ActualDataConnection)
            End Sub)
    End Sub

    Public Sub DeletedAndReusedIdsRejected()
        EachCase(
            Sub(fixture, second, kind)
                Dim stale = LoadItem(second, kind)
                Dim oldToken = Token(kind, stale)
                DeleteItem(fixture.Data, kind, LoadItem(fixture.Data, kind))
                Dim deleted = Snapshot(fixture)
                RejectWithoutWrites(fixture, second, kind, stale, Sub() Save(second, kind, stale, registering:=False))
                RejectWithoutWrites(fixture, second, kind, stale, Sub() DeleteItem(second, kind, stale))
                Equal(deleted, Snapshot(fixture), "A row deleted by another connection must stay deleted")

                ' MAX(seq)+1が同じIDを再利用しても、以前の編集内容は別の行へ適用しない。
                Dim replacement = MakeItem(kind, "replacement row", 2)
                Save(fixture.Data, kind, replacement, registering:=True)
                Equal(1, Sequence(kind, replacement), "The fixture must exercise actual sequence reuse")
                Equal(False, oldToken = Token(kind, replacement), "Reused IDs must receive a distinct token")
                AssertFreshToken(fixture, kind, replacement)
                Dim reused = Snapshot(fixture)
                RejectWithoutWrites(fixture, second, kind, stale, Sub() Save(second, kind, stale, registering:=False))
                RejectWithoutWrites(fixture, second, kind, stale, Sub() DeleteItem(second, kind, stale))
                Equal(reused, Snapshot(fixture), "A stale snapshot must not edit or delete the replacement row")
                AssertSaved(fixture, kind, replacement)
                AssertForeignKeys(fixture.ActualDataConnection)
            End Sub)
    End Sub

    Public Sub CurrentSavesAndCopies()
        EachCase(
            Sub(fixture, second, kind)
                Dim item = LoadItem(fixture.Data, kind)
                If kind = EntityKind.Receipt Then
                    AssertFreshToken(fixture, kind, item)
                Else
                    Equal("0", Token(kind, item), "Unchanged transfer and balance migrations keep the initial token")
                End If
                Dim unaffected = Snapshot(fixture, kind, 1, 2)
                Dim previous = Token(kind, item)
                For Each shapeNumber In New Integer() {2, 3}
                    MakeItem(kind, "sequential save " & shapeNumber.ToString(CultureInfo.InvariantCulture), shapeNumber, item)
                    Save(fixture.Data, kind, item, registering:=False)
                    AssertFreshToken(fixture, kind, item)
                    Equal(False, previous = Token(kind, item), "Every successful save must refresh the same model's token")
                    AssertSaved(fixture, kind, item)
                    previous = Token(kind, item)
                Next

                ' 流用と同じモデルを新規登録し、元行のトークンを引き継がないことを確認する。
                SetSequence(kind, item, 0)
                Save(second, kind, item, registering:=True)
                Equal(2, Sequence(kind, item))
                AssertFreshToken(fixture, kind, item)
                Equal(False, previous = Token(kind, item), "A copy must receive its own new token")
                Equal(previous, Convert.ToString(fixture.Scalar("SELECT revision_token FROM " & ParentTable(kind) & " WHERE seq=1")), "Copy must retain the source row's token")
                Dim copyToken = Token(kind, item)
                Save(second, kind, item, registering:=False)
                Equal(False, copyToken = Token(kind, item), "The copied model must be immediately editable")
                AssertFreshToken(fixture, kind, item)
                AssertSaved(fixture, kind, item)
                Equal(unaffected, Snapshot(fixture, kind, 1, 2), "Sequential saves and copy must preserve every unrelated row")
                AssertForeignKeys(fixture.ActualDataConnection)
            End Sub)
    End Sub

    Public Sub FailedSavesPreserveTokens()
        EachCase(
            Sub(fixture, second, kind)
                For Each registering In New Boolean() {True, False}
                    Dim item = MakeItem(kind, "failed save retry", 2, If(registering, Nothing, LoadItem(second, kind)))
                    If registering Then SetToken(kind, item, "source-token-before-copy")
                    Dim originalToken = Token(kind, item)
                    Dim targetSeq = If(registering, 2, 1)
                    Dim before = Snapshot(fixture)
                    Dim unaffected = Snapshot(fixture, kind, targetSeq)
                    Dim triggerTable As String
                    Dim triggerEvent As String
                    Dim condition As String
                    Select Case kind
                        Case EntityKind.Receipt
                            triggerTable = "t_receipt_detail_users"
                            triggerEvent = "INSERT"
                            condition = "NEW.receipt_seq=" & targetSeq.ToString(CultureInfo.InvariantCulture) & " AND NEW.group_seq=2 AND NEW.user_code=1"
                        Case EntityKind.Transfer
                            triggerTable = "t_transfers"
                            triggerEvent = If(registering, "INSERT", "UPDATE")
                            condition = "NEW.seq=" & targetSeq.ToString(CultureInfo.InvariantCulture)
                        Case EntityKind.Balance
                            triggerTable = "t_balance_groups"
                            triggerEvent = "INSERT"
                            condition = "NEW.balance_seq=" & targetSeq.ToString(CultureInfo.InvariantCulture) & " AND NEW.payment_classification_code=1"
                        Case Else
                            Throw New ArgumentOutOfRangeException(NameOf(kind))
                    End Select
                    fixture.Execute("CREATE TRIGGER fail_revision_save AFTER " & triggerEvent & " ON " & triggerTable & " WHEN " & condition & " BEGIN SELECT RAISE(ABORT,'late revision save failure'); END;")
                    Dim withTrigger = Snapshot(fixture)
                    Dim updates As Integer = 0
                    Dim observe As SQLiteUpdateEventHandler = Sub(sender, args) updates += 1
                    AddHandler fixture.ActualDataConnection.Update, observe
                    ' 失敗は別接続で起こすため、その実接続のイベントも監視する。
                    Dim secondConnection = DirectCast(GetType(DataAccess).GetField("Connection", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).GetValue(second), SQLiteConnection)
                    AddHandler secondConnection.Update, observe
                    Try
                        ExpectLateSaveFailure(Sub() Save(second, kind, item, registering))
                    Finally
                        RemoveHandler fixture.ActualDataConnection.Update, observe
                        RemoveHandler secondConnection.Update, observe
                    End Try
                    Equal(True, updates > 0, "The injected failure must occur after a physical write, not before the transaction starts")
                    Equal(originalToken, Token(kind, item), "A rolled-back save must not publish its proposed token to the model")
                    Equal(withTrigger, Snapshot(fixture), "Late failure must restore all rows, stored types, and tokens")
                    fixture.Execute("DROP TRIGGER fail_revision_save")
                    Equal(before, Snapshot(fixture), "Removing the test trigger must reveal the exact pre-save database")

                    Save(second, kind, item, registering)
                    AssertFreshToken(fixture, kind, item)
                    Equal(False, originalToken = Token(kind, item), "Retry must refresh the token only after success")
                    AssertSaved(fixture, kind, item)
                    Equal(unaffected, Snapshot(fixture, kind, targetSeq), "Retry must preserve every unrelated row")
                    AssertForeignKeys(fixture.ActualDataConnection)
                Next
            End Sub)
    End Sub

    Private Sub ExpectLateSaveFailure(action As Action)
        Try
            action()
        Catch ex As SQLiteException
            Equal(True, ex.Message.Contains("late revision save failure"), "The intended late trigger must cause the failure: " & ex.Message)
            Return
        End Try
        Throw New Exception("Expected the late revision save trigger to abort the transaction")
    End Sub

    Public Sub AllReaderTokens()
        EachCase(
            Sub(fixture, second, kind)
                Dim current = LoadItem(fixture.Data, kind)
                AssertReaderTokens(fixture, second, kind, current)
                MakeItem(kind, "reader token", 2, current)
                Save(fixture.Data, kind, current, registering:=False)
                AssertFreshToken(fixture, kind, current)
                AssertReaderTokens(fixture, second, kind, current)
            End Sub)
    End Sub

    Private Sub AssertReaderTokens(fixture As DatabaseFixture, data As DataAccess, kind As EntityKind, current As Object)
        Dim before = Snapshot(fixture)
        Dim readers As New List(Of Object)
        Select Case kind
            Case EntityKind.Receipt
                readers.Add(data.GetReceipts(2026, 10).Single())
                readers.Add(data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1), DirectCast(current, Receipt).PaymentClassification).Single())
            Case EntityKind.Transfer
                readers.Add(data.GetTransfers(New Date(2026, 10, 1)).Single())
                readers.Add(data.GetPaymentTransfers(New Date(2026, 10, 1), New Date(2026, 11, 1), DirectCast(current, Transfer).FromPaymentClassification).Single())
            Case EntityKind.Balance
                Dim groups = DirectCast(current, Balance).Groups
                readers.Add(data.GetBalances(New Date(2026, 10, 1)).Single())
                readers.Add(data.GetLastBalance(New Date(2026, 11, 1), groups.First().PaymentClassification))
                readers.Add(data.GetLastValuationAmount(New Date(2026, 11, 1), groups.First(Function(group) group.ValuationAmount.HasValue).PaymentClassification))
        End Select
        Equal(If(kind = EntityKind.Balance, 3, 2), readers.Count)
        For Each loaded In readers
            Equal(False, loaded Is Nothing, "Each production reader must return the seeded row")
            Equal(Sequence(kind, current), Sequence(kind, loaded))
            Equal(Token(kind, current), Token(kind, loaded), "Every header reader must return the current revision token")
            AssertSaved(fixture, kind, loaded)
        Next
        Equal(before, Snapshot(fixture), "Reading tokens must not change any saved value or storage type")
    End Sub

    Public Sub UnknownTokensRejected()
        EachCase(
            Sub(fixture, second, kind)
                For Each unknown In New String() {Nothing, String.Empty, "unknown-revision"}
                    Dim item = LoadItem(second, kind)
                    SetToken(kind, item, unknown)
                    ChangeStaleTag(kind, item)
                    Dim before = Snapshot(fixture)
                    RejectWithoutWrites(fixture, second, kind, item, Sub() Save(second, kind, item, registering:=False))
                    RejectWithoutWrites(fixture, second, kind, item, Sub() DeleteItem(second, kind, item))
                    Equal(before, Snapshot(fixture), "Unknown or missing tokens must never change the saved row")
                Next
            End Sub)
    End Sub

    Friend Sub RejectWithoutWrites(fixture As DatabaseFixture, data As DataAccess, kind As EntityKind, item As Object, action As Action)
        Dim originalToken = Token(kind, item)
        Dim updates As Integer
        Dim observe As SQLiteUpdateEventHandler = Sub(sender, args) updates += 1
        Dim connection = DirectCast(GetType(DataAccess).GetField("Connection", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).GetValue(data), SQLiteConnection)
        AddHandler connection.Update, observe
        Try
            Throws(Of DBConcurrencyException)(action)
        Finally
            RemoveHandler connection.Update, observe
        End Try
        Equal(0, updates, "Rejected edits and deletes must be detected before any parent or child row is mutated")
        Equal(originalToken, Token(kind, item), "A conflict must leave the edit snapshot's token unchanged")
    End Sub

    Friend Function Token(kind As EntityKind, item As Object) As String
        Select Case kind
            Case EntityKind.Receipt : Return DirectCast(item, Receipt).RevisionToken
            Case EntityKind.Transfer : Return DirectCast(item, Transfer).RevisionToken
            Case EntityKind.Balance : Return DirectCast(item, Balance).RevisionToken
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Sub SetToken(kind As EntityKind, item As Object, value As String)
        Select Case kind
            Case EntityKind.Receipt : DirectCast(item, Receipt).RevisionToken = value
            Case EntityKind.Transfer : DirectCast(item, Transfer).RevisionToken = value
            Case EntityKind.Balance : DirectCast(item, Balance).RevisionToken = value
        End Select
    End Sub

    Private Sub SetSequence(kind As EntityKind, item As Object, value As Integer)
        Select Case kind
            Case EntityKind.Receipt : DirectCast(item, Receipt).Seq = value
            Case EntityKind.Transfer : DirectCast(item, Transfer).Seq = value
            Case EntityKind.Balance : DirectCast(item, Balance).Seq = value
        End Select
    End Sub

    Private Sub AssertFreshToken(fixture As DatabaseFixture, kind As EntityKind, item As Object)
        Dim parsed As Guid
        Equal(32, Token(kind, item).Length, "Application tokens must use the complete GUID N representation")
        Equal(True, Guid.TryParseExact(Token(kind, item), "N", parsed), "Application saves must issue GUID tokens")
        Equal(Token(kind, item), Convert.ToString(fixture.Scalar("SELECT revision_token FROM " & ParentTable(kind) & " WHERE seq=" & Sequence(kind, item).ToString(CultureInfo.InvariantCulture))), "Published model token must match the committed row")
    End Sub

    Friend Sub DeleteItem(data As DataAccess, kind As EntityKind, item As Object)
        Select Case kind
            Case EntityKind.Receipt : data.DeleteReceipt(DirectCast(item, Receipt))
            Case EntityKind.Transfer : data.DeleteTransfer(DirectCast(item, Transfer))
            Case EntityKind.Balance : data.DeleteBalance(DirectCast(item, Balance))
        End Select
    End Sub
End Module
