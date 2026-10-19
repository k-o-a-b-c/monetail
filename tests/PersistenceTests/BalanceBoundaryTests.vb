Imports System.Data.SQLite
Imports System.Globalization
Imports System.Linq

Friend Module BalanceBoundaryTests
    Public Sub NoBaselineAndAccountFiltering()
        EachForeignKeyMode(
            Sub(fixture)
                Dim target As New Date(2026, 10, 16)
                ReadOnlyCase(fixture,
                    Sub()
                        AssertBoth(fixture, target, 1, Nothing)
                        Equal(True, fixture.Data.GetLastBalance(target, fixture.Data.PaymentClassification(1)) Is Nothing)
                    End Sub)
                AddReceipt(fixture, target.AddHours(12), 999, 2)
                AddTransfer(fixture, target.AddHours(12), 6, 2, 3)
                AddReceipt(fixture, New Date(2026, 10, 17), 100, 1)
                ReadOnlyCase(fixture, Sub() AssertBoth(fixture, target, 1, Nothing))
                AddReceipt(fixture, target, 0, 1)
                ReadOnlyCase(fixture, Sub() AssertBoth(fixture, target, 1, 0))
                AddReceipt(fixture, New Date(2000, 1, 1), 7, 1)
                AddReceipt(fixture, target.AddHours(13), -25, 1)
                AddTransfer(fixture, target.AddHours(14), 10, 1, 2)
                AddTransfer(fixture, target.AddHours(15), 5, 2, 1)
                ReadOnlyCase(fixture,
                    Sub()
                        AssertBoth(fixture, target, 1, 13)
                        AssertBoth(fixture, target, 2, -1000)
                        AssertBoth(fixture, target, 3, 6)
                    End Sub)
            End Sub)
    End Sub

    Public Sub LatestPriorBalanceAndSameDay()
        EachForeignKeyMode(
            Sub(fixture)
                AddBalance(fixture, New Date(2026, 10, 14), 500, 1)
                Dim first = AddBalance(fixture, New Date(2026, 10, 15), 1000, 1)
                Dim second = AddBalance(fixture, New Date(2026, 10, 16), 2000, 2)
                AddBalance(fixture, New Date(2026, 10, 17), 9000, 1)
                AddBalance(fixture, New Date(2026, 10, 17, 18, 0, 0), 9500, 1)
                AddBalance(fixture, New Date(2026, 10, 18), 10000, 1)
                ReadOnlyCase(fixture,
                    Sub()
                        AssertBoth(fixture, New Date(2026, 10, 16), 2, Nothing)
                        AssertBoth(fixture, New Date(2026, 10, 17), 1, 1000)
                        AssertBoth(fixture, New Date(2026, 10, 17), 2, 2000)
                    End Sub)
                AddReceipt(fixture, New Date(2026, 10, 15, 12, 0, 0), 100, 1)
                AddReceipt(fixture, New Date(2026, 10, 16, 12, 0, 0), 30, 1)
                AddReceipt(fixture, New Date(2026, 10, 17, 12, 0, 0), 20, 1)
                AddTransfer(fixture, New Date(2026, 10, 16, 18, 0, 0), 5, 2, 1)
                AddTransfer(fixture, New Date(2026, 10, 17, 23, 59, 59), 7, 1, 2)
                ReadOnlyCase(fixture,
                    Sub()
                        Equal(first, fixture.Data.GetLastBalance(New Date(2026, 10, 17), fixture.Data.PaymentClassification(1)).Seq)
                        Equal(second, fixture.Data.GetLastBalance(New Date(2026, 10, 17), fixture.Data.PaymentClassification(2)).Seq)
                        AssertBoth(fixture, New Date(2026, 10, 16), 1, 975)
                        AssertBoth(fixture, New Date(2026, 10, 16), 2, -5)
                        For Each target In New Date() {New Date(2026, 10, 17), New Date(2026, 10, 17, 8, 0, 0), New Date(2026, 10, 17, 23, 59, 59)}
                            AssertBoth(fixture, target, 1, 948)
                            AssertBoth(fixture, target, 2, 2007)
                        Next
                    End Sub)
            End Sub)
    End Sub

    Public Sub EndOfDayHalfOpenInterval()
        EachForeignKeyMode(
            Sub(fixture)
                AddBalance(fixture, New Date(2026, 10, 15), 1000, 1)
                ' 各行の金額を変え、端点の片方だけを落としても同じ合計にならないようにする。
                Dim times As Date() = {New Date(2026, 10, 15, 23, 59, 58).AddMilliseconds(999),
                    New Date(2026, 10, 15, 23, 59, 59), New Date(2026, 10, 16),
                    New Date(2026, 10, 16, 23, 59, 59), New Date(2026, 10, 16, 23, 59, 59).AddTicks(1),
                    New Date(2026, 10, 17)}
                Dim receipts As Integer() = {101, 11, 13, 17, 103, 107}
                Dim transfers As Integer() = {109, 3, 5, 7, 113, 127}
                For index As Integer = 0 To times.Length - 1
                    AddReceipt(fixture, times(index), receipts(index), 1)
                    AddTransfer(fixture, times(index), transfers(index), If(index = 2, 2, 1), If(index = 2, 1, 2))
                Next
                ReadOnlyCase(fixture,
                    Sub()
                        AssertWindow(fixture, New Date(2026, 10, 15, 23, 59, 59), New Date(2026, 10, 16, 23, 59, 59), "2,3,4", "2,3,4")
                        AssertWindow(fixture, New Date(2026, 10, 16), New Date(2026, 10, 17), "3,4,5", "3,4,5", endExclusive:=True)
                        ' 残高日末を再計上せず、対象日最後の小数秒まで含める。
                        ' 1000 - (13+17+103) + 5 - 7 - 113 = 752。
                        AssertBoth(fixture, New Date(2026, 10, 16), 1, 752)
                    End Sub)
            End Sub)
    End Sub

    Public Sub FractionalSecondsAndStoredText()
        EachForeignKeyMode(
            Sub(fixture)
                AddBalance(fixture, New Date(2026, 10, 15), 1000, 1)
                Dim targetEnd As New Date(2026, 10, 16, 23, 59, 59)
                Dim lowerEnd As New Date(2026, 10, 15, 23, 59, 59)
                AddReceipt(fixture, New Date(2026, 10, 16, 12, 0, 0), 11, 1)
                AddReceipt(fixture, targetEnd, 13, 1)
                AddReceipt(fixture, targetEnd.AddTicks(1), 17, 1)
                AddReceipt(fixture, targetEnd.AddMilliseconds(500), 19, 1)
                AddReceipt(fixture, New Date(2026, 10, 17).AddTicks(-1), 23, 1)
                Dim lowerReceipt = AddReceipt(fixture, lowerEnd, 29, 1)
                Dim upperReceipt = AddReceipt(fixture, targetEnd, 31, 1)
                SetRawDate(fixture, "t_receipts", lowerReceipt, "2026-10-15 23:59:59.0000000")
                SetRawDate(fixture, "t_receipts", upperReceipt, "2026-10-16 23:59:59.0000000")
                Dim firstReceipt = AddReceipt(fixture, New Date(2026, 10, 16), 37, 1)
                Dim nextReceipt = AddReceipt(fixture, New Date(2026, 10, 17), 41, 1)
                SetRawDate(fixture, "t_receipts", firstReceipt, "2026-10-16 00:00:00.0000000")
                SetRawDate(fixture, "t_receipts", nextReceipt, "2026-10-17 00:00:00.0000000")
                AddTransfer(fixture, New Date(2026, 10, 16, 12, 0, 0), 3, 2, 1)
                AddTransfer(fixture, targetEnd, 5, 1, 2)
                AddTransfer(fixture, targetEnd.AddTicks(1), 7, 2, 1)
                AddTransfer(fixture, targetEnd.AddMilliseconds(500), 11, 2, 1)
                Dim upperTransfer = AddTransfer(fixture, targetEnd, 13, 1, 2)
                Dim lowerTransfer = AddTransfer(fixture, lowerEnd, 17, 1, 2)
                SetRawDate(fixture, "t_transfers", upperTransfer, "2026-10-16 23:59:59.0000000")
                SetRawDate(fixture, "t_transfers", lowerTransfer, "2026-10-15 23:59:59.0000000")
                Dim firstTransfer = AddTransfer(fixture, New Date(2026, 10, 16), 19, 2, 1)
                Dim nextTransfer = AddTransfer(fixture, New Date(2026, 10, 17), 23, 1, 2)
                SetRawDate(fixture, "t_transfers", firstTransfer, "2026-10-16 00:00:00.0000000")
                SetRawDate(fixture, "t_transfers", nextTransfer, "2026-10-17 00:00:00.0000000")
                ReadOnlyCase(fixture,
                    Sub()
                        Dim payment = fixture.Data.PaymentClassification(1)
                        Dim allReceipts = fixture.Data.GetPaymentReceipts(lowerEnd, New Date(2026, 10, 17), payment).ToDictionary(Function(r) r.Seq)
                        Equal(targetEnd.AddTicks(1).Ticks, allReceipts(3).Date.Ticks)
                        Equal(targetEnd.AddMilliseconds(500).Ticks, allReceipts(4).Date.Ticks)
                        Equal(New Date(2026, 10, 17).AddTicks(-1).Ticks, allReceipts(5).Date.Ticks)
                        Equal(targetEnd.Ticks, allReceipts(upperReceipt).Date.Ticks)
                        Dim allTransfers = fixture.Data.GetPaymentTransfers(lowerEnd, New Date(2026, 10, 17), payment).ToDictionary(Function(t) t.Seq)
                        Equal(targetEnd.AddTicks(1).Ticks, allTransfers(3).Date.Ticks)
                        Equal(targetEnd.Ticks, allTransfers(upperTransfer).Date.Ticks)
                        AssertWindow(fixture, lowerEnd, targetEnd, "1,2,6,8", "1,2,6,7")
                        AssertWindow(fixture, New Date(2026, 10, 16), New Date(2026, 10, 17), "1,2,3,4,5,7,8", "1,2,3,4,5,7", endExclusive:=True)
                        ' 支出151円、入金40円、出金18円。翌日0時の明示的な小数秒ゼロも除外。
                        AssertBoth(fixture, New Date(2026, 10, 16), 1, 871)
                    End Sub)
            End Sub)
    End Sub

    Public Sub CalendarTransitions()
        For Each calendarDate In New Date() {New Date(2026, 10, 1), New Date(2027, 1, 1), New Date(2024, 3, 1), New Date(2025, 3, 1)}
            Dim target As Date = calendarDate
            EachForeignKeyMode(
                Sub(fixture)
                    AddBalance(fixture, target.AddDays(-1), 1000, 1)
                    AddReceipt(fixture, target.AddDays(-1).AddHours(12), 101, 1)
                    AddReceipt(fixture, target.AddHours(12), 11, 1)
                    AddReceipt(fixture, target.AddDays(1), 103, 1)
                    AddTransfer(fixture, target, 7, 2, 1)
                    AddTransfer(fixture, target.AddHours(23).AddMinutes(59).AddSeconds(59), 5, 1, 2)
                    ReadOnlyCase(fixture,
                        Sub()
                            AssertBoth(fixture, target, 1, 991)
                            AssertBoth(fixture, target.AddHours(19), 1, 991)
                        End Sub)
                End Sub)
        Next
    End Sub

    Public Sub NonMidnightBaseline()
        EachForeignKeyMode(
            Sub(fixture)
                Dim baseline = AddBalance(fixture, New Date(2026, 10, 15, 9, 30, 0), 1000, 1)
                AddReceipt(fixture, New Date(2026, 10, 15, 23, 59, 59), 101, 1)
                AddReceipt(fixture, New Date(2026, 10, 16), 103, 1)
                AddReceipt(fixture, New Date(2026, 10, 16, 9, 29, 58).AddMilliseconds(999), 107, 1)
                AddReceipt(fixture, New Date(2026, 10, 16, 9, 29, 59), 20, 1)
                AddTransfer(fixture, New Date(2026, 10, 16, 9, 30, 0), 7, 2, 1)
                AddTransfer(fixture, New Date(2026, 10, 16, 23, 59, 59), 3, 1, 2)
                ReadOnlyCase(fixture,
                    Sub()
                        Dim selected = fixture.Data.GetLastBalance(New Date(2026, 10, 16), fixture.Data.PaymentClassification(1))
                        Equal(baseline, selected.Seq)
                        Equal(New Date(2026, 10, 15, 9, 30, 0), selected.Date)
                        AssertWindow(fixture, New Date(2026, 10, 16, 9, 29, 59), New Date(2026, 10, 16, 23, 59, 59), "4", "1,2")
                        AssertWindow(fixture, New Date(2026, 10, 16), New Date(2026, 10, 17), "2,3,4", "1,2", endExclusive:=True)
                        AssertBoth(fixture, New Date(2026, 10, 16), 1, 774)
                    End Sub)
            End Sub)
    End Sub

    Public Sub SupportedDateExtremes()
        EachForeignKeyMode(
            Sub(fixture)
                AddReceipt(fixture, DateTime.MinValue, 11, 1)
                AddReceipt(fixture, New Date(1, 1, 2).AddTicks(-1), 13, 1)
                AddReceipt(fixture, New Date(1, 1, 2), 101, 1)
                AddTransfer(fixture, DateTime.MinValue, 7, 2, 1)
                AddTransfer(fixture, New Date(1, 1, 2).AddTicks(-1), 5, 1, 2)
                AddTransfer(fixture, New Date(1, 1, 2), 103, 1, 2)
                ReadOnlyCase(fixture, Sub() AssertBoth(fixture, DateTime.MinValue, 1, -22))
            End Sub)
        EachForeignKeyMode(
            Sub(fixture)
                AddBalance(fixture, New Date(9999, 12, 30, 18, 0, 0), 1000, 1)
                AddReceipt(fixture, New Date(9999, 12, 30, 23, 59, 59), 101, 1)
                AddReceipt(fixture, New Date(9999, 12, 31), 11, 1)
                AddReceipt(fixture, DateTime.MaxValue, 13, 1)
                AddTransfer(fixture, New Date(9999, 12, 31), 7, 2, 1)
                AddTransfer(fixture, DateTime.MaxValue, 5, 1, 2)
                ReadOnlyCase(fixture, Sub() AssertBoth(fixture, DateTime.MaxValue, 1, 978))
            End Sub)
    End Sub

    Public Sub InstanceDataSource()
        EachForeignKeyMode(
            Sub(fixture)
                Dim target As New Date(2026, 10, 16)
                AddBalance(fixture, target.AddDays(-1), 1000, 1)
                AddReceipt(fixture, target, 73, 1)
                AddTransfer(fixture, target, 5, 1, 2)
                AddTransfer(fixture, target, 7, 2, 1)
                Using other As New DatabaseFixture(Convert.ToInt64(Scalar(fixture, "PRAGMA foreign_keys")) = 1)
                    ' 別DBには初期レシート41円があり、同じ口座コードでも残高が異なる。
                    AddBalance(other, target.AddDays(-1), 2000, 1)
                    ReadOnlyCase(fixture,
                        Sub()
                            ReadOnlyCase(other,
                                Sub()
                                    Dim previousData = Calculation.DataAccess
                                    Try
                                        Dim payment = fixture.Data.PaymentClassification(1)
                                        Dim otherPayment = other.Data.PaymentClassification(1)
                                        Calculation.DataAccess = Nothing
                                        Equal(Of Integer?)(929, fixture.Data.GetAutoBalance(target, payment))
                                        Equal(Of Integer?)(1959, other.Data.GetAutoBalance(target, otherPayment))
                                        Equal(True, Calculation.DataAccess Is Nothing)

                                        Calculation.DataAccess = other.Data
                                        Equal(Of Integer?)(1959, Calculation.AutoBalance(target, otherPayment))
                                        Equal(Of Integer?)(929, fixture.Data.GetAutoBalance(target, payment))
                                        Equal(True, Calculation.DataAccess Is other.Data)

                                        Calculation.DataAccess = fixture.Data
                                        Equal(Of Integer?)(929, Calculation.AutoBalance(target, payment))
                                        Equal(Of Integer?)(1959, other.Data.GetAutoBalance(target, otherPayment))
                                        Equal(True, Calculation.DataAccess Is fixture.Data)
                                    Finally
                                        Calculation.DataAccess = previousData
                                    End Try
                                End Sub)
                        End Sub)
                End Using
            End Sub)
    End Sub

    Private Sub EachForeignKeyMode(action As Action(Of DatabaseFixture))
        For Each foreignKeys In New Boolean() {False, True}
            Try
                Using fixture As New DatabaseFixture(foreignKeys)
                    fixture.Execute("DELETE FROM t_receipt_detail_users; DELETE FROM t_receipt_details; DELETE FROM t_receipt_groups; DELETE FROM t_receipts;" &
                        "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'bank',2,1),(3,'other',3,1);")
                    Equal(If(foreignKeys, 1L, 0L), Convert.ToInt64(Scalar(fixture, "PRAGMA foreign_keys")))
                    action(fixture)
                End Using
            Catch ex As Exception
                Throw New Exception("foreign_keys=" & foreignKeys.ToString(), ex)
            End Try
        Next
    End Sub

    Private Sub ReadOnlyCase(fixture As DatabaseFixture, action As Action)
        Dim before = ConcurrencyTests.Snapshot(fixture)
        Dim foreignKeys = Scalar(fixture, "PRAGMA foreign_keys")
        Dim userVersion = Scalar(fixture, "PRAGMA user_version")
        Dim previousData = Calculation.DataAccess
        Dim writes As Integer = 0
        Dim observe As SQLiteUpdateEventHandler = Sub(sender, args) writes += 1
        AddHandler fixture.ActualDataConnection.Update, observe
        Try
            Calculation.DataAccess = fixture.Data
            action()
        Finally
            Calculation.DataAccess = previousData
            RemoveHandler fixture.ActualDataConnection.Update, observe
            Equal(0, writes, "Balance prediction must not issue writes")
            Equal(before, ConcurrencyTests.Snapshot(fixture), "All stored values, types, tokens and schema must remain unchanged")
            Equal(foreignKeys, Scalar(fixture, "PRAGMA foreign_keys"))
            Equal(userVersion, Scalar(fixture, "PRAGMA user_version"))
        End Try
    End Sub

    Private Sub AssertBoth(fixture As DatabaseFixture, target As Date, paymentCode As Integer, expected As Integer?)
        Dim payment = fixture.Data.PaymentClassification(paymentCode)
        Dim context = target.ToString("O", CultureInfo.InvariantCulture) & " payment=" & paymentCode.ToString(CultureInfo.InvariantCulture)
        Equal(Of Integer?)(expected, fixture.Data.GetAutoBalance(target, payment), "DataAccess " & context)
        Equal(Of Integer?)(expected, Calculation.AutoBalance(target, payment), "Calculation " & context)
    End Sub

    Private Sub AssertWindow(fixture As DatabaseFixture, dateFrom As Date, dateTo As Date, receiptIds As String, transferIds As String,
                             Optional endExclusive As Boolean = False)
        Dim payment = fixture.Data.PaymentClassification(1)
        Dim receipts = If(endExclusive, fixture.Data.GetPaymentReceipts(dateFrom, dateTo, payment, endExclusive:=True),
            fixture.Data.GetPaymentReceipts(dateFrom, dateTo, payment))
        Dim transfers = If(endExclusive, fixture.Data.GetPaymentTransfers(dateFrom, dateTo, payment, endExclusive:=True),
            fixture.Data.GetPaymentTransfers(dateFrom, dateTo, payment))
        Equal(receiptIds, String.Join(",", receipts.Select(Function(r) r.Seq).OrderBy(Function(seq) seq)))
        Equal(transferIds, String.Join(",", transfers.Select(Function(t) t.Seq).OrderBy(Function(seq) seq)))
    End Sub

    Private Function AddReceipt(fixture As DatabaseFixture, value As Date, amount As Integer, paymentCode As Integer) As Integer
        Dim receipt = MakeReceipt("floor", Group(10, Detail(amount, taxable:=False)))
        receipt.Date = value
        receipt.TaxType = 0
        receipt.PaymentClassification = fixture.Data.PaymentClassification(paymentCode)
        fixture.Data.RegisterReceipt(receipt)
        Return receipt.Seq
    End Function

    Private Function AddTransfer(fixture As DatabaseFixture, value As Date, amount As Integer, fromCode As Integer, toCode As Integer) As Integer
        Dim transfer As New Transfer With {.Date = value, .Name = "boundary", .Fee = amount, .Tag = "",
            .FromPaymentClassification = fixture.Data.PaymentClassification(fromCode), .ToPaymentClassification = fixture.Data.PaymentClassification(toCode)}
        fixture.Data.RegisterTransfer(transfer)
        Return transfer.Seq
    End Function

    Private Function AddBalance(fixture As DatabaseFixture, value As Date, amount As Integer, paymentCode As Integer) As Integer
        Dim balance As New Balance With {.Date = value, .Tag = "boundary"}
        balance.Groups.Add(New BalanceGroup With {.PaymentClassification = fixture.Data.PaymentClassification(paymentCode),
            .Balance = amount, .ValuationAmount = 9999, .Tag = ""})
        fixture.Data.RegisterBalance(balance)
        Return balance.Seq
    End Function

    Private Sub SetRawDate(fixture As DatabaseFixture, tableName As String, seq As Integer, value As String)
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = "UPDATE " & tableName & " SET date=@date WHERE seq=@seq"
            command.Parameters.AddWithValue("date", value)
            command.Parameters.AddWithValue("seq", seq)
            command.ExecuteNonQuery()
        End Using
    End Sub

    Private Function Scalar(fixture As DatabaseFixture, sql As String) As Object
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = sql
            Return command.ExecuteScalar()
        End Using
    End Function
End Module
