Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq

Friend Module FractionalDiscountTests
    Private ReadOnly Rates As Double() = {0, 3, 2.5, 2.6}
    Private ReadOnly FloorTotals As Integer() = {216, 209, 210, 209}
    Private ReadOnly HalfUpTotals As Integer() = {216, 210, 211, 210}

    Public Sub InsertRoundTrips()
        RoundTrips(False)
    End Sub

    Public Sub UpdateRoundTrips()
        RoundTrips(True)
    End Sub

    Private Sub RoundTrips(updated As Boolean)
        Dim failures As New List(Of String)
        For Each mode In New String() {"floor", "half_up"}
            For index As Integer = 0 To Rates.Length - 1
                Dim rate = Rates(index)
                Dim expectedTotal = If(mode = "floor", FloorTotals(index), HalfUpTotals(index))
                Dim context = If(updated, "update", "insert") & " mode=" & mode &
                    " rate=" & rate.ToString(CultureInfo.InvariantCulture)
                RunCase(context & " path=save", failures,
                    Sub()
                        Using fixture As New DatabaseFixture
                            ' (103 - 3) * 2 = 200。率値引と税端数は固定した期待値で検証する。
                            Dim receipt = MakeReceipt(mode, Group(8, Detail(103, quantity:=2, discountFee:=3,
                                discountRate:=If(updated, If(rate = 0, 3, 0), rate))))
                            fixture.Data.RegisterReceipt(receipt)
                            If updated Then
                                receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = receipt.Seq)
                                receipt.Groups.Single().Details.Single().DiscountRate = rate
                                fixture.Data.UpdateReceipt(receipt)
                            End If
                            Dim detailFilter = " FROM t_receipt_details WHERE receipt_seq=" &
                                receipt.Seq.ToString(CultureInfo.InvariantCulture)
                            Equal(rate, Convert.ToDouble(fixture.Scalar("SELECT CAST(discount_rate AS REAL)" & detailFilter)), context & " saved rate")
                            Equal(If(rate = Math.Truncate(rate), "integer", "real"),
                                Convert.ToString(fixture.Scalar("SELECT typeof(discount_rate)" & detailFilter)), context & " SQLite storage class")
                            Equal(expectedTotal, Convert.ToInt32(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=" &
                                receipt.Seq.ToString(CultureInfo.InvariantCulture))), context & " saved total")
                            If Not updated AndAlso mode = "floor" AndAlso rate = 2.5 Then ReportProviderBehavior(fixture, detailFilter)

                            Dim before = SavedValues(fixture, receipt.Seq)
                            For Each path In New String() {"GetReceipts", "GetPaymentReceipts", "LoadReceiptChildData"}
                                Dim readContext = context & " path=" & path
                                RunCase(readContext, failures,
                                    Sub()
                                        Try
                                            Dim loaded As Receipt
                                            Select Case path
                                                Case "GetReceipts"
                                                    loaded = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = receipt.Seq)
                                                Case "GetPaymentReceipts"
                                                    loaded = fixture.Data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1),
                                                        receipt.PaymentClassification).Single(Function(r) r.Seq = receipt.Seq)
                                                Case Else
                                                    ' 未保存の明細が、DBの小数率を持つ明細へ置き換わることも確認する。
                                                    loaded = MakeReceipt(mode, Group(8, Detail(1, discountRate:=99)))
                                                    loaded.Seq = receipt.Seq
                                                    fixture.Data.LoadReceiptChildData(loaded)
                                            End Select
                                            AssertReceipt(loaded, mode, rate, expectedTotal, readContext)
                                        Finally
                                            Equal(before, SavedValues(fixture, receipt.Seq), readContext & " must not write saved values")
                                        End Try
                                    End Sub)
                            Next
                        End Using
                    End Sub)
            Next
        Next
        If failures.Count > 0 Then Throw New Exception(String.Join(Environment.NewLine, failures))
    End Sub

    Private Sub AssertReceipt(receipt As Receipt, mode As String, rate As Double, total As Integer, context As String)
        Dim detail = receipt.Groups.Single().Details.Single()
        Equal(mode, receipt.TaxRoundingMode, context & " rounding mode")
        Equal(1, receipt.TaxType, context & " tax type")
        Equal(rate, detail.DiscountRate, context & " rate")
        Equal(103, detail.Fee, context & " fee")
        Equal(3, detail.DiscountFee, context & " manual discount per unit")
        Equal(2, detail.Quantity, context & " quantity")
        Equal(True, detail.TaxTarget, context & " tax target")
        Equal(total, receipt.Total, context & " total")
    End Sub

    Private Function SavedValues(fixture As DatabaseFixture, receiptSeq As Integer) As String
        Return Convert.ToString(fixture.Scalar("SELECT quote(d.discount_rate)||'|'||typeof(d.discount_rate)||'|'||" &
            "r.total||'|'||d.fee||'|'||d.discount_fee||'|'||d.quantity||'|'||d.tax_target " &
            "FROM t_receipt_details d JOIN t_receipts r ON r.seq=d.receipt_seq WHERE r.seq=" &
            receiptSeq.ToString(CultureInfo.InvariantCulture)))
    End Function

    Private Sub ReportProviderBehavior(fixture As DatabaseFixture, detailFilter As String)
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = "SELECT discount_rate,typeof(discount_rate)" & detailFilter
            Using reader = command.ExecuteReader()
                reader.Read()
                Dim actual As String
                Try
                    actual = reader.GetInt32(0).ToString(CultureInfo.InvariantCulture)
                Catch ex As Exception
                    actual = ex.GetType().Name
                End Try
                Console.WriteLine("INFO SQLite " & fixture.ActualDataConnection.GetType().Assembly.GetName().Version.ToString() &
                    " stored rate=2.5 storage=" & reader.GetString(1) & " GetInt32=" & actual)
            End Using
        End Using
    End Sub

    Private Sub RunCase(context As String, failures As List(Of String), action As Action)
        Try
            action()
        Catch ex As Exception
            failures.Add(context & ": " & ex.ToString())
        End Try
    End Sub
End Module
