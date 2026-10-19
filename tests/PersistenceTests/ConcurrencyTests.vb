Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Friend Module ConcurrencyTests
    Private Enum EntityKind
        Receipt
        Transfer
        Balance
    End Enum

    Private Const EventTimeoutSeconds As Integer = 10
    Private Const WorkerTimeoutSeconds As Integer = 20

    Public Sub ConcurrentRegistration()
        EachCase(
            Sub(fixture, second, kind)
                Dim firstItem = MakeItem(kind, "first writer", 2)
                Dim secondItem = MakeItem(kind, "second writer", 3)
                Dim unaffected = Snapshot(fixture, kind, 2, 3)
                Dim firstConnection = fixture.ActualDataConnection
                Dim secondConnection = DataConnection(second)
                firstConnection.DefaultTimeout = WorkerTimeoutSeconds
                secondConnection.DefaultTimeout = WorkerTimeoutSeconds
                Dim beginStatement As String = Nothing
                Dim callbackTimedOut As Integer
                Dim firstWorker As Task = Nothing
                Dim secondWorker As Task = Nothing

                Using firstInserted As New ManualResetEventSlim(), secondBeginning As New ManualResetEventSlim(), releaseFirst As New ManualResetEventSlim()
                    ' 親INSERT後の書込ロックを保持し、別接続のBEGIN実行を待つ。
                    Dim pauseFirst As SQLiteUpdateEventHandler =
                        Sub(sender, args)
                            If args.Table <> ParentTable(kind) OrElse args.Event <> UpdateEventType.Insert Then Return
                            firstInserted.Set()
                            If Not releaseFirst.Wait(TimeSpan.FromSeconds(WorkerTimeoutSeconds)) Then Interlocked.Exchange(callbackTimedOut, 1)
                        End Sub
                    Dim observeSecond As SQLiteTraceEventHandler =
                        Sub(sender, args)
                            If Not args.Statement.TrimStart().StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) Then Return
                            beginStatement = args.Statement
                            secondBeginning.Set()
                        End Sub
                    AddHandler firstConnection.Update, pauseFirst
                    AddHandler secondConnection.Trace, observeSecond
                    Try
                        firstWorker = StartWorker(Sub() Save(fixture.Data, kind, firstItem, registering:=True))
                        Equal(True, firstInserted.Wait(TimeSpan.FromSeconds(EventTimeoutSeconds)), "First writer must reach its parent INSERT")
                        secondWorker = StartWorker(Sub() Save(second, kind, secondItem, registering:=True))
                        Equal(True, secondBeginning.Wait(TimeSpan.FromSeconds(EventTimeoutSeconds)), "Second writer must execute BEGIN while the first writer holds its lock")
                        Equal("BEGIN IMMEDIATE", beginStatement.Trim().TrimEnd(";"c).ToUpperInvariant(), "Provider transaction SQL")
                        Equal(False, secondWorker.IsCompleted, "Second save must still be pending while the first writer is held")
                        Equal(0, Sequence(kind, secondItem), "Second writer must not allocate a sequence before acquiring its transaction")
                    Finally
                        releaseFirst.Set()
                        Try
                            JoinWorkers(firstWorker, secondWorker)
                        Finally
                            RemoveHandler firstConnection.Update, pauseFirst
                            RemoveHandler secondConnection.Trace, observeSecond
                        End Try
                    End Try
                End Using

                Equal(0, callbackTimedOut, "Writer hook must be released by the test, not by its timeout")
                Equal(2, Sequence(kind, firstItem))
                Equal(3, Sequence(kind, secondItem))
                AssertSaved(fixture, kind, firstItem)
                AssertSaved(fixture, kind, secondItem)
                Equal(unaffected, Snapshot(fixture, kind, 2, 3), "Concurrent saves must preserve the neighbor and all unrelated rows")
                AssertForeignKeys(firstConnection)
                Console.WriteLine("INFO concurrent " & kind.ToString() & ": " & beginStatement & "; sequences=2,3; timeout=" & WorkerTimeoutSeconds.ToString(CultureInfo.InvariantCulture) & "s")
            End Sub)
    End Sub

    Public Sub HeldWriteLock()
        EachCase(
            Sub(fixture, second, kind)
                Dim connection = DataConnection(second)
                connection.DefaultTimeout = 1
                For Each registering In New Boolean() {True, False}
                    Dim item = If(registering, MakeItem(kind, "retry insert", 2), MakeItem(kind, "retry update", 3, LoadItem(second, kind)))
                    Dim targetSeq = If(registering, 2, 1)
                    Dim before = Snapshot(fixture)
                    Dim unaffected = Snapshot(fixture, kind, targetSeq)
                    ' BeginTransactionの書込ロックを解放せず、実際のタイムアウトを起こす。
                    Using held = fixture.ActualDataConnection.BeginTransaction()
                        Try
                            ExpectContention(Sub() Save(second, kind, item, registering), kind.ToString() & If(registering, " register", " update"))
                            Equal(before, Snapshot(fixture), "A rejected save must not change any saved row or schema entry")
                            Equal(If(registering, 0, 1), Sequence(kind, item), "Lock acquisition fails before sequence assignment or child mutation")
                        Finally
                            held.Rollback()
                        End Try
                    End Using
                    Equal(before, Snapshot(fixture), "A rejected save must also leave no partial rows after the holder releases its transaction")
                    ' 失敗した同じDataAccessとモデルを、ロック解放後に再使用する。
                    Save(second, kind, item, registering)
                    Equal(targetSeq, Sequence(kind, item))
                    AssertSaved(fixture, kind, item)
                    Equal(unaffected, Snapshot(fixture, kind, targetSeq), "Retry must preserve every unrelated saved row")
                    AssertForeignKeys(connection)
                Next
            End Sub)
    End Sub

    Public Sub StaleEditsOverwrite()
        EachCase(
            Sub(fixture, second, kind)
                Dim neighbor = MakeItem(kind, "unchanged neighbor", 3)
                Save(fixture.Data, kind, neighbor, registering:=True)
                Equal(2, Sequence(kind, neighbor))
                AssertSaved(fixture, kind, neighbor)
                ' 両方の読込を先に完了し、二つ目が最新の保存内容を再読込しないようにする。
                Dim firstSnapshot = LoadItem(fixture.Data, kind)
                Dim staleSnapshot = LoadItem(second, kind)
                Equal(False, Object.ReferenceEquals(firstSnapshot, staleSnapshot), "Separate readers must return separate edit snapshots")
                AssertSaved(fixture, kind, firstSnapshot)
                AssertSaved(fixture, kind, staleSnapshot)
                Dim unaffected = Snapshot(fixture, kind, 1)

                MakeItem(kind, "first editor", 3, firstSnapshot)
                Save(fixture.Data, kind, firstSnapshot, registering:=False)
                AssertSaved(fixture, kind, firstSnapshot)

                ChangeStaleTag(kind, staleSnapshot)
                Save(second, kind, staleSnapshot, registering:=False)
                ' 現行仕様の後勝ちを固定する。先行編集のヘッダー・子との混在は認めない。
                AssertSaved(fixture, kind, staleSnapshot)
                Equal(unaffected, Snapshot(fixture, kind, 1), "A stale edit must preserve the neighbor and all unrelated rows")
                AssertForeignKeys(DataConnection(second))
            End Sub)
    End Sub

    Private Sub EachCase(action As Action(Of DatabaseFixture, DataAccess, EntityKind))
        Dim failures As New List(Of String)()
        For Each foreignKeys In New Boolean() {True, False}
            For Each kind In [Enum].GetValues(GetType(EntityKind)).Cast(Of EntityKind)()
                Try
                    Using fixture As New DatabaseFixture(foreignKeys)
                        SeedNeighbors(fixture)
                        Using second As New DataAccess(fixture.ConnectionString)
                            Equal(False, Object.ReferenceEquals(fixture.ActualDataConnection, DataConnection(second)))
                            For Each connection In New SQLiteConnection() {fixture.ActualDataConnection, DataConnection(second)}
                                Equal(If(foreignKeys, 1L, 0L), Convert.ToInt64(Scalar(connection, "PRAGMA foreign_keys")), "Actual DataAccess connection FK setting")
                                Equal("delete", Convert.ToString(Scalar(connection, "PRAGMA journal_mode")), "Default rollback journal configuration")
                                AssertForeignKeys(connection)
                            Next
                            Console.WriteLine("INFO concurrency configuration: entity=" & kind.ToString() & "; foreign_keys=" & foreignKeys.ToString() & "; SQLite=" & Convert.ToString(Scalar(fixture.ActualDataConnection, "SELECT sqlite_version()")) & "; provider=" & GetType(SQLiteConnection).Assembly.GetName().Version.ToString() & "; journal=delete")
                            action(fixture, second, kind)
                        End Using
                    End Using
                Catch ex As Exception
                    failures.Add(kind.ToString() & ", FK=" & foreignKeys.ToString() & ": " & ex.ToString())
                End Try
            Next
        Next
        If failures.Count > 0 Then Throw New Exception(String.Join(Environment.NewLine, failures))
    End Sub

    Private Sub SeedNeighbors(fixture As DatabaseFixture)
        fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'other','other account',2);" &
            "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'bank',2,2);" &
            "INSERT INTO m_point_classification(code,name,seq,default_rate) VALUES(1,'points',1,0.01);" &
            "INSERT INTO t_receipt_points(receipt_seq,seq,code,rate,fixed_points,total_points) VALUES(1,1,1,0.01,2,3);" &
            "INSERT INTO t_transfers(seq,date,name,fee,from_payment_classification_code,to_payment_classification_code,tag,update_user,update_datetime) VALUES(1,'2026-10-16 12:00:00','neighbor transfer',25,1,2,'neighbor','test','2026-10-16');" &
            "INSERT INTO t_balances(seq,date,tag,update_user,update_datetime) VALUES(1,'2026-10-16 12:00:00','neighbor balance','test','2026-10-16');" &
            "INSERT INTO t_balance_groups(balance_seq,payment_classification_code,balance,valuation_amount,tag) VALUES(1,1,101,NULL,'cash'),(1,2,-202,-250,'bank');" &
            "INSERT INTO t_payment_memo(payment_classification_code,check_date,tag) VALUES(1,'2026-10-16','unrelated memo');" &
            "INSERT INTO t_exchange_rates(rate_date,base_currency,quote_currency,middle_rate) VALUES('2026-10-16','USD','JPY',150.5);")
    End Sub

    Private Function DataConnection(data As DataAccess) As SQLiteConnection
        Return DirectCast(GetType(DataAccess).GetField("Connection", BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(data), SQLiteConnection)
    End Function

    Private Function StartWorker(action As Action) As Task
        Return Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
    End Function

    Private Sub JoinWorkers(ParamArray workers As Task())
        Dim started = workers.Where(Function(worker) worker IsNot Nothing).ToArray()
        ' 実行中の接続やイベントを破棄しない。終了不能なら他のテストを続けない。
        If Not Task.WaitAll(started, TimeSpan.FromSeconds(WorkerTimeoutSeconds)) Then
            Environment.FailFast("Concurrent persistence workers did not stop within the cleanup deadline.")
        End If
    End Sub

    Private Sub ExpectContention(action As Action, context As String)
        Try
            action()
        Catch ex As SQLiteException
            Dim code = CInt(ex.ResultCode) And &HFF
            Equal(True, code = CInt(SQLiteErrorCode.Busy) OrElse code = CInt(SQLiteErrorCode.Locked), "Expected SQLite Busy/Locked, got " & ex.ToString())
            Console.WriteLine("INFO held lock " & context & ": " & ex.ResultCode.ToString() & "; timeout=1s")
            Return
        End Try
        Throw New Exception("Expected SQLite Busy/Locked: " & context)
    End Sub

    Private Function ParentTable(kind As EntityKind) As String
        Select Case kind
            Case EntityKind.Receipt : Return "t_receipts"
            Case EntityKind.Transfer : Return "t_transfers"
            Case EntityKind.Balance : Return "t_balances"
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Function Sequence(kind As EntityKind, item As Object) As Integer
        Select Case kind
            Case EntityKind.Receipt : Return DirectCast(item, Receipt).Seq
            Case EntityKind.Transfer : Return DirectCast(item, Transfer).Seq
            Case EntityKind.Balance : Return DirectCast(item, Balance).Seq
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Function LoadItem(data As DataAccess, kind As EntityKind) As Object
        Select Case kind
            Case EntityKind.Receipt : Return data.GetReceipts(2026, 10).Single(Function(item) item.Seq = 1)
            Case EntityKind.Transfer : Return data.GetTransfers(New Date(2026, 10, 1)).Single(Function(item) item.Seq = 1)
            Case EntityKind.Balance : Return data.GetBalances(New Date(2026, 10, 1)).Single(Function(item) item.Seq = 1)
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Sub Save(data As DataAccess, kind As EntityKind, item As Object, registering As Boolean)
        Select Case kind
            Case EntityKind.Receipt
                If registering Then data.RegisterReceipt(DirectCast(item, Receipt)) Else data.UpdateReceipt(DirectCast(item, Receipt))
            Case EntityKind.Transfer
                If registering Then data.RegisterTransfer(DirectCast(item, Transfer)) Else data.UpdateTransfer(DirectCast(item, Transfer))
            Case EntityKind.Balance
                If registering Then data.RegisterBalance(DirectCast(item, Balance)) Else data.UpdateBalance(DirectCast(item, Balance))
        End Select
    End Sub

    Private Function MakeItem(kind As EntityKind, label As String, shapeNumber As Integer, Optional existing As Object = Nothing) As Object
        Dim itemDate = New Date(2026, 10, 17 + shapeNumber, 9 + shapeNumber, 23, 45)
        Dim cash As New PaymentClassification With {.Code = 1}
        Dim bank As New PaymentClassification With {.Code = 2}
        Select Case kind
            Case EntityKind.Receipt
                Dim receipt = If(TryCast(existing, Receipt), New Receipt())
                receipt.Date = itemDate
                receipt.ShopName = label & " shop"
                receipt.ShopLocation = label & " location"
                receipt.Tag = label & " header"
                receipt.PaymentClassification = bank
                receipt.TaxType = shapeNumber Mod 2
                receipt.TaxRoundingMode = If(shapeNumber = 2, "half_up", "floor")
                receipt.Groups.Clear()
                For index As Integer = 1 To If(shapeNumber = 2, 2, 1)
                    Dim group = ReceiptFixtures.Group(If(index = 1, 8, 10))
                    group.Tag = label & " group " & index.ToString(CultureInfo.InvariantCulture)
                    For detailIndex As Integer = 1 To If(shapeNumber = 3, 2, 1)
                        Dim detail = ReceiptFixtures.Detail(100 * shapeNumber + detailIndex, taxable:=(detailIndex = 1), quantity:=shapeNumber, discountFee:=3, discountRate:=2.5)
                        detail.Name = label & " item " & detailIndex.ToString(CultureInfo.InvariantCulture)
                        detail.Tag = label & " detail " & detailIndex.ToString(CultureInfo.InvariantCulture)
                        detail.Users.Add(New User With {.Code = 2})
                        If index = 2 Then detail.Users.Add(New User With {.Code = 1})
                        group.Details.Add(detail)
                    Next
                    receipt.Groups.Add(group)
                Next
                Return receipt
            Case EntityKind.Transfer
                Dim transfer = If(TryCast(existing, Transfer), New Transfer())
                transfer.Date = itemDate
                transfer.Name = label & " transfer"
                transfer.Tag = label & " header"
                transfer.Fee = 100 * shapeNumber + 17
                transfer.FromPaymentClassification = bank
                transfer.ToPaymentClassification = cash
                Return transfer
            Case EntityKind.Balance
                Dim balance = If(TryCast(existing, Balance), New Balance())
                balance.Date = itemDate
                balance.Tag = label & " header"
                balance.Groups.Clear()
                balance.Groups.Add(New BalanceGroup With {.PaymentClassification = bank, .Balance = -100 * shapeNumber, .ValuationAmount = If(shapeNumber = 2, Nothing, CType(-350, Integer?)), .Tag = label & " bank"})
                If shapeNumber = 2 Then balance.Groups.Add(New BalanceGroup With {.PaymentClassification = cash, .Balance = 221, .ValuationAmount = 250, .Tag = label & " cash"})
                Return balance
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Sub ChangeStaleTag(kind As EntityKind, item As Object)
        Select Case kind
            Case EntityKind.Receipt
                DirectCast(item, Receipt).Tag = "stale second editor"
                DirectCast(item, Receipt).Groups(0).Details(0).Tag = "stale detail tag"
            Case EntityKind.Transfer
                DirectCast(item, Transfer).Tag = "stale second editor"
            Case EntityKind.Balance
                DirectCast(item, Balance).Tag = "stale second editor"
                DirectCast(item, Balance).Groups(0).Tag = "stale group tag"
        End Select
    End Sub

    Private Sub AssertSaved(fixture As DatabaseFixture, kind As EntityKind, item As Object)
        Dim seq = Sequence(kind, item)
        Dim condition = " WHERE seq=" & seq.ToString(CultureInfo.InvariantCulture)
        Select Case kind
            Case EntityKind.Receipt
                Dim receipt = DirectCast(item, Receipt)
                AssertRows(fixture, "SELECT seq,strftime('%Y-%m-%d %H:%M:%S',date),shop_name,shop_location,tax_type,tax_rounding_mode,payment_classification_code,tag,total FROM t_receipts" & condition,
                    New Object() {seq, DateText(receipt.Date), receipt.ShopName, receipt.ShopLocation, receipt.TaxType, receipt.TaxRoundingMode, receipt.PaymentClassification.Code, receipt.Tag, receipt.Total})
                Dim groups As New List(Of Object())(), details As New List(Of Object())(), users As New List(Of Object())()
                For groupIndex As Integer = 0 To receipt.Groups.Count - 1
                    Dim group = receipt.Groups(groupIndex)
                    groups.Add(New Object() {seq, groupIndex + 1, group.GroupClassification.Code, group.TaxRate.Code, group.Tag})
                    For detailIndex As Integer = 0 To group.Details.Count - 1
                        Dim detail = group.Details(detailIndex)
                        details.Add(New Object() {seq, groupIndex + 1, detailIndex + 1, detail.Name, detail.Fee, detail.Quantity, detail.DiscountFee, detail.DiscountRate, If(detail.TaxTarget, 1, 0), detail.Tag})
                        For Each user In detail.Users.OrderBy(Function(value) value.Code)
                            users.Add(New Object() {seq, groupIndex + 1, detailIndex + 1, user.Code})
                        Next
                    Next
                Next
                Dim childCondition = " WHERE receipt_seq=" & seq.ToString(CultureInfo.InvariantCulture)
                AssertRows(fixture, "SELECT receipt_seq,seq,group_classification_code,tax,tag FROM t_receipt_groups" & childCondition & " ORDER BY seq", groups.ToArray())
                AssertRows(fixture, "SELECT receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_target,tag FROM t_receipt_details" & childCondition & " ORDER BY group_seq,seq", details.ToArray())
                AssertRows(fixture, "SELECT receipt_seq,group_seq,detail_seq,user_code FROM t_receipt_detail_users" & childCondition & " ORDER BY group_seq,detail_seq,user_code", users.ToArray())
            Case EntityKind.Transfer
                Dim transfer = DirectCast(item, Transfer)
                AssertRows(fixture, "SELECT seq,strftime('%Y-%m-%d %H:%M:%S',date),name,fee,from_payment_classification_code,to_payment_classification_code,tag FROM t_transfers" & condition,
                    New Object() {seq, DateText(transfer.Date), transfer.Name, transfer.Fee, transfer.FromPaymentClassification.Code, transfer.ToPaymentClassification.Code, transfer.Tag})
            Case EntityKind.Balance
                Dim balance = DirectCast(item, Balance)
                AssertRows(fixture, "SELECT seq,strftime('%Y-%m-%d %H:%M:%S',date),tag FROM t_balances" & condition, New Object() {seq, DateText(balance.Date), balance.Tag})
                Dim balanceGroups = balance.Groups.OrderBy(Function(group) group.PaymentClassification.Code).Select(Function(group) New Object() {seq, group.PaymentClassification.Code, group.Balance, group.ValuationAmount, group.Tag}).ToArray()
                AssertRows(fixture, "SELECT balance_seq,payment_classification_code,balance,valuation_amount,tag FROM t_balance_groups WHERE balance_seq=" & seq.ToString(CultureInfo.InvariantCulture) & " ORDER BY payment_classification_code", balanceGroups)
        End Select
    End Sub

    Private Function DateText(value As Date) As String
        Return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
    End Function

    Private Sub AssertRows(fixture As DatabaseFixture, sql As String, ParamArray expected As Object()())
        Dim expectedText As New StringBuilder()
        For Each row In expected
            AppendRow(expectedText, row)
        Next
        Equal(expectedText.ToString(), QueryRows(fixture.ActualDataConnection, sql), sql)
    End Sub

    Private Function Snapshot(fixture As DatabaseFixture) As String
        Return Snapshot(fixture, Nothing)
    End Function

    Private Function Snapshot(fixture As DatabaseFixture, kind As EntityKind?, ParamArray excludedSequences As Integer()) As String
        Dim connection = fixture.ActualDataConnection
        Dim result As New StringBuilder(QueryRows(connection, "SELECT type,name,tbl_name,sql FROM sqlite_master ORDER BY type,name"))
        Dim tables As New List(Of String)()
        Using command = connection.CreateCommand()
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
            Using reader = command.ExecuteReader()
                While reader.Read()
                    tables.Add(reader.GetString(0))
                End While
            End Using
        End Using
        For Each table In tables
            Dim query = "SELECT * FROM " & table
            Dim keyColumn As String = Nothing
            If kind.HasValue Then
                If table = ParentTable(kind.Value) Then keyColumn = "seq"
                If kind.Value = EntityKind.Receipt AndAlso New String() {"t_receipt_groups", "t_receipt_details", "t_receipt_detail_users"}.Contains(table) Then keyColumn = "receipt_seq"
                If kind.Value = EntityKind.Balance AndAlso table = "t_balance_groups" Then keyColumn = "balance_seq"
            End If
            If keyColumn IsNot Nothing AndAlso excludedSequences.Length > 0 Then query &= " WHERE " & keyColumn & " NOT IN (" & String.Join(",", excludedSequences) & ")"
            result.AppendLine(table).Append(QueryRows(connection, query & " ORDER BY rowid"))
        Next
        Return result.ToString()
    End Function

    Private Function QueryRows(connection As SQLiteConnection, sql As String) As String
        Dim result As New StringBuilder()
        Using command = connection.CreateCommand()
            command.CommandText = sql
            Using reader = command.ExecuteReader()
                While reader.Read()
                    Dim values(reader.FieldCount - 1) As Object
                    reader.GetValues(values)
                    AppendRow(result, values)
                End While
            End Using
        End Using
        Return result.ToString()
    End Function

    Private Sub AppendRow(result As StringBuilder, values As Object())
        For Each value In values
            If value Is Nothing OrElse Convert.IsDBNull(value) Then
                result.Append("null;")
            Else
                Dim stored = Convert.ToString(value, CultureInfo.InvariantCulture)
                result.Append(stored.Length).Append(":"c).Append(stored).Append(";"c)
            End If
        Next
        result.AppendLine()
    End Sub

    Private Function Scalar(connection As SQLiteConnection, sql As String) As Object
        Using command = connection.CreateCommand()
            command.CommandText = sql
            Return command.ExecuteScalar()
        End Using
    End Function

    Private Sub AssertForeignKeys(connection As SQLiteConnection)
        Equal(String.Empty, QueryRows(connection, "PRAGMA foreign_key_check"), "All related rows must have valid foreign keys")
    End Sub
End Module
