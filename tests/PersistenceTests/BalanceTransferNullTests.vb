Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text

Friend Module BalanceTransferNullTests
    Private ReadOnly TextValues As String() = {Nothing, String.Empty, "plain tag", "残高・振替 '引用'", " " & vbTab & "　 "}
    Private ReadOnly BalanceReaders As String() = {"GetBalances", "GetLastBalance", "GetLastValuationAmount"}

    Public Sub HeaderTags()
        CheckBalanceTags(False)
    End Sub

    Public Sub GroupTags()
        CheckBalanceTags(True)
    End Sub

    Private Sub CheckBalanceTags(groupTag As Boolean)
        Dim failures As New List(Of String)
        For Each textValue In TextValues
            Using fixture As New DatabaseFixture
                SeedRows(fixture)
                SetTag(fixture, If(groupTag, "t_balance_groups", "t_balances"), textValue)
                For Each readerName In BalanceReaders
                    ReadCase(fixture, readerName & If(groupTag, " group tag ", " header tag ") & ValueLabel(textValue), failures,
                        Sub() AssertBalanceReader(fixture, readerName,
                            If(groupTag, "balance tag", If(textValue, String.Empty)),
                            If(groupTag, If(textValue, String.Empty), "account tag"), "neighbor tag"))
                Next
            End Using
        Next
        If failures.Count > 0 Then Throw New Exception(String.Join(Environment.NewLine, failures))
    End Sub

    Public Sub TransferTags()
        Dim failures As New List(Of String)
        For Each textValue In TextValues
            Using fixture As New DatabaseFixture
                SeedRows(fixture)
                SetTag(fixture, "t_transfers", textValue)
                For Each readerName In New String() {"GetTransfers", "GetPaymentTransfers"}
                    ReadCase(fixture, readerName & " tag " & ValueLabel(textValue), failures,
                        Sub() AssertTransferReader(fixture, readerName, If(textValue, String.Empty), "neighbor transfer"))
                Next
            End Using
        Next

        Using fixture As New DatabaseFixture
            SeedRows(fixture)
            fixture.Execute("UPDATE t_balances SET tag=NULL; UPDATE t_balance_groups SET tag=NULL; UPDATE t_transfers SET tag=NULL;" &
                "UPDATE t_receipts SET shop_location=NULL,tag=NULL; UPDATE t_receipt_groups SET tag=NULL; UPDATE t_receipt_details SET tag=NULL;")
            ReadCase(fixture, "all optional text SQL NULL and both auto-balance paths", failures,
                Sub()
                    For Each readerName In BalanceReaders
                        AssertBalanceReader(fixture, readerName, String.Empty, String.Empty, String.Empty)
                    Next
                    AssertTransferReader(fixture, "GetTransfers", String.Empty, String.Empty)
                    AssertTransferReader(fixture, "GetPaymentTransfers", String.Empty, String.Empty)
                    Dim previousData = Calculation.DataAccess
                    Try
                        Calculation.DataAccess = fixture.Data
                        Dim expectedAmounts As Integer() = {1114, -214, 9}
                        For paymentCode As Integer = 1 To 3
                            Dim payment = fixture.Data.PaymentClassificationList.Single(Function(item) item.Code = paymentCode)
                            Equal(Of Integer?)(expectedAmounts(paymentCode - 1), fixture.Data.GetAutoBalance(New Date(2026, 10, 18), payment))
                            Equal(Of Integer?)(expectedAmounts(paymentCode - 1), Calculation.AutoBalance(New Date(2026, 10, 18), payment))
                        Next
                    Finally
                        Calculation.DataAccess = previousData
                    End Try
                End Sub)
        End Using
        If failures.Count > 0 Then Throw New Exception(String.Join(Environment.NewLine, failures))
    End Sub

    Private Sub SeedRows(fixture As DatabaseFixture)
        ' 最新残高と最新評価額を別の履歴にし、NULL評価額・負額・隣の口座も保持する。
        fixture.Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'bank',2,1),(3,'other',3,1);" &
            "INSERT INTO t_balances(seq,date,tag,update_user,update_datetime) VALUES" &
            "(10,'2026-10-10 09:30:00','balance tag','test','2026-10-18')," &
            "(11,'2026-10-12 16:45:30','balance tag','test','2026-10-18');" &
            "INSERT INTO t_balance_groups(balance_seq,payment_classification_code,balance,valuation_amount,tag) VALUES" &
            "(10,1,1000,1100,'account tag'),(10,2,-200,NULL,'neighbor tag')," &
            "(11,1,1200,NULL,'account tag'),(11,2,-250,-225,'neighbor tag');" &
            "INSERT INTO t_transfers(seq,date,name,fee,from_payment_classification_code,to_payment_classification_code,tag,update_user,update_datetime) VALUES" &
            "(21,'2026-10-14 08:15:00','振替 ''出金''',75,1,2,'transfer tag','test','2026-10-18')," &
            "(22,'2026-10-15 21:05:00','振替 ''入金''',30,2,1,'transfer tag','test','2026-10-18')," &
            "(23,'2026-10-17 10:20:00','別口座',9,2,3,'neighbor transfer','test','2026-10-18')," &
            "(24,'2026-11-01 00:00:00','翌月',8,1,3,'next month','test','2026-10-18');")
    End Sub

    Private Sub SetTag(fixture As DatabaseFixture, tableName As String, textValue As String)
        Dim condition As String
        Select Case tableName
            Case "t_balances" : condition = String.Empty
            Case "t_balance_groups" : condition = " WHERE payment_classification_code=1"
            Case "t_transfers" : condition = " WHERE seq IN (21,22)"
            Case Else : Throw New ArgumentException("Unknown tag table: " & tableName)
        End Select
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = "UPDATE " & tableName & " SET tag=@value" & condition
            command.Parameters.AddWithValue("value", If(textValue Is Nothing, DirectCast(DBNull.Value, Object), textValue))
            command.ExecuteNonQuery()
        End Using
    End Sub

    Private Sub AssertBalanceReader(fixture As DatabaseFixture, readerName As String, headerTag As String, firstTag As String, secondTag As String)
        If readerName = "GetBalances" Then
            Dim balances = fixture.Data.GetBalances(New Date(2026, 10, 1))
            Equal(2, balances.Count)
            AssertBalance(balances(0), 11, headerTag, firstTag, secondTag)
            AssertBalance(balances(1), 10, headerTag, firstTag, secondTag)
            Return
        End If
        For Each payment In fixture.Data.PaymentClassificationList
            Dim balance As Balance
            If readerName = "GetLastBalance" Then
                balance = fixture.Data.GetLastBalance(New Date(2026, 10, 18), payment)
            ElseIf readerName = "GetLastValuationAmount" Then
                balance = fixture.Data.GetLastValuationAmount(New Date(2026, 10, 18), payment)
            Else
                Throw New ArgumentException("Unknown balance reader: " & readerName)
            End If
            If payment.Code = 3 Then
                Equal(True, balance Is Nothing)
            Else
                AssertBalance(balance, If(readerName = "GetLastValuationAmount" AndAlso payment.Code = 1, 10, 11), headerTag, firstTag, secondTag)
            End If
        Next
    End Sub

    Private Sub AssertBalance(balance As Balance, expectedSeq As Integer, headerTag As String, firstTag As String, secondTag As String)
        Equal(expectedSeq, balance.Seq)
        Equal(If(expectedSeq = 10, New Date(2026, 10, 10, 9, 30, 0), New Date(2026, 10, 12, 16, 45, 30)), balance.Date)
        Equal(headerTag, balance.Tag, "Balance.Tag")
        Equal(2, balance.Groups.Count)
        For index As Integer = 0 To 1
            Dim group = balance.Groups(index)
            Equal(expectedSeq, group.BalanceSeq)
            Equal(index + 1, group.PaymentClassification.Code)
            Equal(1, group.PaymentClassification.User.Code)
            Equal(If(index = 0, firstTag, secondTag), group.Tag, "BalanceGroup.Tag")
        Next
        Equal(If(expectedSeq = 10, 1000, 1200), balance.Groups(0).Balance)
        Equal(If(expectedSeq = 10, -200, -250), balance.Groups(1).Balance)
        Equal(Of Integer?)(If(expectedSeq = 10, CType(1100, Integer?), Nothing), balance.Groups(0).ValuationAmount)
        Equal(Of Integer?)(If(expectedSeq = 10, Nothing, CType(-225, Integer?)), balance.Groups(1).ValuationAmount)
    End Sub

    Private Sub AssertTransferReader(fixture As DatabaseFixture, readerName As String, targetTag As String, neighborTag As String)
        If readerName = "GetTransfers" Then
            Dim transfers = fixture.Data.GetTransfers(New Date(2026, 10, 1))
            Equal("23,22,21", String.Join(",", transfers.Select(Function(item) item.Seq)))
            For Each transfer In transfers
                AssertTransfer(transfer, targetTag, neighborTag)
            Next
            Return
        End If
        Dim expectedSequences As String() = {"21,22", "21,22,23", "23"}
        For Each payment In fixture.Data.PaymentClassificationList
            Dim transfers = fixture.Data.GetPaymentTransfers(New Date(2026, 10, 1), New Date(2026, 10, 31, 23, 59, 59), payment)
            Equal(expectedSequences(payment.Code - 1), String.Join(",", transfers.OrderBy(Function(item) item.Seq).Select(Function(item) item.Seq)))
            For Each transfer In transfers
                AssertTransfer(transfer, targetTag, neighborTag)
            Next
        Next
    End Sub

    Private Sub AssertTransfer(transfer As Transfer, targetTag As String, neighborTag As String)
        Select Case transfer.Seq
            Case 21
                Equal(New Date(2026, 10, 14, 8, 15, 0), transfer.Date)
                Equal("振替 '出金'", transfer.Name)
                Equal(75, transfer.Fee)
                Equal(1, transfer.FromPaymentClassification.Code)
                Equal(2, transfer.ToPaymentClassification.Code)
            Case 22
                Equal(New Date(2026, 10, 15, 21, 5, 0), transfer.Date)
                Equal("振替 '入金'", transfer.Name)
                Equal(30, transfer.Fee)
                Equal(2, transfer.FromPaymentClassification.Code)
                Equal(1, transfer.ToPaymentClassification.Code)
            Case 23
                Equal(New Date(2026, 10, 17, 10, 20, 0), transfer.Date)
                Equal("別口座", transfer.Name)
                Equal(9, transfer.Fee)
                Equal(2, transfer.FromPaymentClassification.Code)
                Equal(3, transfer.ToPaymentClassification.Code)
            Case Else
                Throw New Exception("Unexpected transfer: " & transfer.Seq.ToString(CultureInfo.InvariantCulture))
        End Select
        Equal(If(transfer.Seq = 23, neighborTag, targetTag), transfer.Tag, "Transfer.Tag")
    End Sub

    Private Sub ReadCase(fixture As DatabaseFixture, context As String, failures As List(Of String), action As Action)
        Try
            Dim before = SavedRows(fixture)
            Try
                action()
            Finally
                Equal(before, SavedRows(fixture), "Reading must preserve SQL NULLs and every related saved row")
            End Try
        Catch ex As Exception
            failures.Add(context & ": " & ex.GetType().Name & ": " & ex.Message)
        End Try
    End Sub

    ' NULLと空文字を区別し、日時・金額・評価額を含む関連表の全列・全行を比較する。
    Private Function SavedRows(fixture As DatabaseFixture) As String
        Dim result As New StringBuilder()
        For Each tableName In New String() {"m_payment_classification", "t_balances", "t_balance_groups", "t_transfers", "t_receipts", "t_receipt_groups", "t_receipt_details", "t_receipt_detail_users"}
            result.AppendLine(tableName)
            Using command = fixture.ActualDataConnection.CreateCommand()
                command.CommandText = "SELECT * FROM " & tableName & " ORDER BY rowid"
                Using reader = command.ExecuteReader()
                    While reader.Read()
                        For column As Integer = 0 To reader.FieldCount - 1
                            If reader.IsDBNull(column) Then
                                result.Append("null;")
                            Else
                                Dim storedValue = Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture)
                                result.Append(storedValue.Length).Append(":"c).Append(storedValue).Append(";"c)
                            End If
                        Next
                        result.AppendLine()
                    End While
                End Using
            End Using
        Next
        Return result.ToString()
    End Function

    Private Function ValueLabel(textValue As String) As String
        Return If(textValue Is Nothing, "SQL NULL", "[" & textValue & "]")
    End Function
End Module
