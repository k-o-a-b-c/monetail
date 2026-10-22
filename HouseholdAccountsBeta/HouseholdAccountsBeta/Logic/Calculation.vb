Imports System.Numerics

Public Class Calculation
    Public Shared DataAccess As DataAccess

    Public Shared Function MergeGroupNotes(notes As IList(Of String)) As String
        If notes.Count = 1 Then Return notes(0)
        Dim nonempty = notes.Where(Function(note) Not String.IsNullOrEmpty(note)).ToList()
        If nonempty.Count = 0 Then Return If(notes.All(Function(note) note Is Nothing), Nothing, String.Empty)
        Return String.Join(vbLf, nonempty.Select(Function(note, index) "【統合元 " & (index + 1).ToString() & "】" & vbLf & note))
    End Function

    Public Shared Sub ValidateTaxRoundingMode(mode As String)
        If mode <> "floor" AndAlso mode <> "half_up" Then Throw New ArgumentException("税端数処理は floor または half_up を指定してください。", NameOf(mode))
    End Sub

    ' レシート・分類・明細・人数按分を同じ税率別合算へ集約する。
    Public Shared Function SumDetails(details As IEnumerable(Of ReceiptDetail), taxType As Integer, mode As String,
                                      Optional signFilter As Integer = 0, Optional allocate As Boolean = False) As MoneyFraction
        ValidateTaxRoundingMode(mode)
        If taxType <> 0 AndAlso taxType <> 1 Then Throw New ArgumentException("不正な内税／外税区分です。")
        Dim taxable As New Dictionary(Of Integer, MoneyFraction)
        Dim excluded As New MoneyFraction(BigInteger.Zero)
        For Each detail As ReceiptDetail In details
            If detail.TaxRate Is Nothing Then Throw New InvalidOperationException("明細の税設定がありません。")
            Dim net = ExactMoney.Net(detail.Fee, detail.DiscountFee, detail.Quantity, detail.DiscountRate)
            If signFilter <> 0 AndAlso net.Sign <> signFilter Then Continue For
            Dim divisor = If(allocate, detail.Users.Count, 1)
            If divisor = 0 Then Continue For
            Dim amount As New MoneyFraction(net, divisor)
            If detail.TaxRate.IsTaxable Then
                Dim rate = detail.TaxRate.Rate
                If Not taxable.ContainsKey(rate) Then taxable.Add(rate, New MoneyFraction(BigInteger.Zero))
                taxable(rate) += amount
            Else
                excluded += amount
            End If
        Next
        For Each pair In taxable
            excluded += pair.Value.WithTax(pair.Key, taxType, mode)
        Next
        Return excluded
    End Function

    Public Shared Function AutoBalance(ByVal BaseDate As DateTime, ByVal PaymentClassification As PaymentClassification) As Nullable(Of Integer)
        Return AutoBalance(BaseDate, PaymentClassification, DataAccess)
    End Function

    ' 呼出元の接続を明示し、期間判定と加減算を一か所で行う。
    Friend Shared Function AutoBalance(ByVal BaseDate As DateTime, ByVal PaymentClassification As PaymentClassification, ByVal source As DataAccess) As Nullable(Of Integer)
        Dim balance As Integer = 0
        Dim dateFrom As DateTime = DateTime.MinValue
        ' 日末残高の翌日から対象日の翌日直前までを集計する。
        Dim endExclusive As Boolean = BaseDate.Date <> DateTime.MaxValue.Date
        Dim dateTo As DateTime = If(endExclusive, BaseDate.Date.AddDays(1), DateTime.MaxValue)
        Dim payment As PaymentClassification = PaymentClassification

        Dim LastBalance As Balance = source.GetLastBalance(BaseDate.Date, payment)
        If LastBalance IsNot Nothing Then
            dateFrom = LastBalance.Date.Date.AddDays(1)
            balance = LastBalance.Groups.FirstOrDefault(Function(g) g.PaymentClassification.Code = payment.Code).Balance
        End If

        Dim receipts As List(Of Receipt) = source.GetPaymentReceipts(dateFrom, dateTo, payment, endExclusive:=endExclusive)
        For Each receipt As Receipt In receipts
            balance -= receipt.Total
        Next

        Dim transfers As List(Of Transfer) = source.GetPaymentTransfers(dateFrom, dateTo, payment, endExclusive:=endExclusive)
        For Each transfer As Transfer In transfers
            If transfer.FromPaymentClassification.Code = payment.Code Then
                balance -= transfer.Fee
            Else
                balance += transfer.Fee
            End If
        Next

        If LastBalance IsNot Nothing OrElse receipts.Count > 0 OrElse transfers.Count > 0 Then
            Return balance
        Else
            Return Nothing
        End If
    End Function

    Public Shared Function GroupCalculatedFee(receipt As Receipt, group As ReceiptGroup) As Integer
        Return CInt(SumDetails(group.Details, receipt.TaxType, receipt.TaxRoundingMode).Rounded(receipt.TaxRoundingMode))
    End Function

    Public Shared Function DetailCalculatedFee(receipt As Receipt, group As ReceiptGroup, detail As ReceiptDetail) As Integer
        Return CInt(SumDetails({detail}, receipt.TaxType, receipt.TaxRoundingMode).Rounded(receipt.TaxRoundingMode))
    End Function
End Class
