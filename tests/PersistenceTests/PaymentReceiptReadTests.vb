Imports System.Data.SQLite
Imports System.Linq

Friend Module PaymentReceiptReadTests
    Public Sub HierarchyAndSingleSelect()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                ReceiptReadTests.Seed(fixture)
                fixture.Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(-1,'negative code',3,1);")
                fixture.AddReceipt(10, "negative payment", "floor", New Date(2026, 10, 19), paymentCode:=-1)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        For Each code In {1, 2, -1, -999}
                            For Each exclusive In {False, True}
                                Dim payment As New PaymentClassification With {.Code = code}
                                Dim reads = 0
                                Dim observe As SQLiteTraceEventHandler =
                                    Sub(sender, args)
                                        Dim sql = args.Statement.Trim().ToLowerInvariant()
                                        If sql.StartsWith("select ", StringComparison.Ordinal) AndAlso sql.Contains("from t_receipt") Then reads += 1
                                    End Sub
                                Dim actual As List(Of Receipt)
                                AddHandler fixture.ActualDataConnection.Trace, observe
                                Try
                                    actual = fixture.Data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1), payment, exclusive)
                                Finally
                                    RemoveHandler fixture.ActualDataConnection.Trace, observe
                                End Try
                                Equal(1, reads, "One hierarchy SELECT, including empty results and cold master caches")
                                Dim expected = ReadLegacy(fixture, New Date(2026, 10, 1), New Date(2026, 11, 1), payment, exclusive)
                                Equal(Ids(expected), Ids(actual), "Preserve legacy sequence order")
                                Equal(If(code = 1, If(exclusive, "0,1,3,5,6", "0,1,3,5,6,7"),
                                    If(code = 2, "2", If(code = -1, "10", ""))), Ids(actual))
                                For index = 0 To expected.Count - 1
                                    ReceiptReadTests.AssertSame(expected(index), actual(index))
                                Next
                            Next
                        Next
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub RangeBoundaries()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                ReceiptReadTests.Seed(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Dim payment = fixture.Data.PaymentClassification(1)
                        Dim endpoint As New Date(2026, 10, 31, 23, 59, 59)
                        Equal("5,6,7", Ids(fixture.Data.GetPaymentReceipts(endpoint, New Date(2026, 11, 1), payment)))
                        Equal("5,6", Ids(fixture.Data.GetPaymentReceipts(endpoint, New Date(2026, 11, 1), payment, endExclusive:=True)))
                        Equal("5", Ids(fixture.Data.GetPaymentReceipts(endpoint, endpoint, payment)))
                        Equal("", Ids(fixture.Data.GetPaymentReceipts(endpoint, endpoint, payment, endExclusive:=True)))
                        For Each exclusive In {False, True}
                            Equal("", Ids(fixture.Data.GetPaymentReceipts(New Date(2026, 11, 1), endpoint, payment, exclusive)))
                        Next
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub PredictionOrder()
        For Each sign In {1, -1}
            Dim amount = sign * 1500000000
            BalanceBoundaryTests.EachForeignKeyMode(
                Sub(fixture)
                    BalanceBoundaryTests.AddBalance(fixture, New Date(2026, 9, 30), 0, 1)
                    BalanceBoundaryTests.AddReceipt(fixture, New Date(2026, 10, 20), amount, 1)
                    Dim second = BalanceBoundaryTests.AddReceipt(fixture, New Date(2026, 10, 16), amount, 1)
                    BalanceBoundaryTests.AddReceipt(fixture, New Date(2026, 10, 18), -amount, 1)
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub()
                            Dim payment = fixture.Data.PaymentClassification(1)
                            Equal("1,2,3", Ids(ReadLegacy(fixture, New Date(2026, 10, 1), New Date(2026, 11, 1), payment)))
                            Equal("1,2,3", Ids(fixture.Data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1), payment)))
                            ' 最終値は範囲内でも、従来のSeq順では2件目で超過する。
                            ' 日付順へ変えると相殺されるため、読込順の回帰として確認する。
                            Throws(Of OverflowException)(Sub() fixture.Data.GetAutoBalance(New Date(2026, 10, 31), payment))
                            Throws(Of OverflowException)(Sub() Calculation.AutoBalance(New Date(2026, 10, 31), payment))
                        End Sub)
                    Using command = fixture.ActualDataConnection.CreateCommand()
                        command.CommandText = "UPDATE t_receipt_details SET fee=@fee WHERE receipt_seq=@seq"
                        command.Parameters.AddWithValue("fee", -amount)
                        command.Parameters.AddWithValue("seq", second)
                        command.ExecuteNonQuery()
                    End Using
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub() BalanceBoundaryTests.AssertBoth(fixture, New Date(2026, 10, 31), 1, amount))
                End Sub)
        Next
    End Sub

    Private Function Ids(receipts As List(Of Receipt)) As String
        Return String.Join(",", receipts.Select(Function(r) r.Seq))
    End Function

    ' 改修前の親SELECTと、未変更の製品子読込を参照実装として保持する。
    ' 新しい共通JOINから独立させ、比較と同一CI内の性能基準に使う。
    Friend Function ReadLegacy(fixture As DatabaseFixture, dateFrom As Date, dateTo As Date,
                               payment As PaymentClassification, Optional endExclusive As Boolean = False) As List(Of Receipt)
        Dim receipts As New List(Of Receipt)
        Using command = fixture.ActualDataConnection.CreateCommand()
            Dim condition = If(endExclusive, "date >= :date_from and date < :date_to", "date between :date_from and :date_to")
            command.CommandText = "select seq, date, shop_name, shop_location, tax_type, payment_classification_code, tag, tax_rounding_mode, revision_token from t_receipts where " &
                condition & " and payment_classification_code = :payment_classification_code"
            command.Parameters.AddWithValue("date_from", dateFrom)
            command.Parameters.AddWithValue("date_to", dateTo)
            command.Parameters.AddWithValue("payment_classification_code", payment.Code)
            Using reader = command.ExecuteReader()
                While reader.Read()
                    Dim receipt As New Receipt With {
                        .Seq = reader.GetInt32(0), .Date = reader.GetDateTime(1), .ShopName = reader.GetString(2),
                        .ShopLocation = If(reader.IsDBNull(3), String.Empty, reader.GetString(3)),
                        .TaxType = reader.GetInt32(4), .PaymentClassification = fixture.Data.PaymentClassification(reader.GetInt32(5)),
                        .Tag = If(reader.IsDBNull(6), String.Empty, reader.GetString(6)),
                        .TaxRoundingMode = reader.GetString(7), .RevisionToken = reader.GetString(8)}
                    fixture.Data.LoadReceiptChildData(receipt)
                    receipts.Add(receipt)
                End While
            End Using
        End Using
        Return receipts
    End Function
End Module
