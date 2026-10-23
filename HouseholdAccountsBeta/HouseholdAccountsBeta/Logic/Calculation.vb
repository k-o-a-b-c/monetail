Imports System.Numerics

Public Class Calculation
    Public Shared DataAccess As DataAccess

    ' 統合するグループのメモを順序と重複を保ってまとめる。
    Public Shared Function MergeGroupNotes(notes As IList(Of String)) As String
        If notes.Count = 1 Then Return notes(0)
        Dim nonempty = notes.Where(Function(note) Not String.IsNullOrEmpty(note)).ToList()
        If nonempty.Count = 0 Then Return If(notes.All(Function(note) note Is Nothing), Nothing, String.Empty)
        Return String.Join(vbLf, nonempty.Select(Function(note, index) "【統合元 " & (index + 1).ToString() & "】" & vbLf & note))
    End Function

    ' 税端数方式が切り捨て・四捨五入のいずれかを検証する。
    Public Shared Sub ValidateTaxRoundingMode(mode As String)
        If mode <> "floor" AndAlso mode <> "half_up" Then Throw New ArgumentException("税端数処理は floor または half_up を指定してください。", NameOf(mode))
    End Sub

    ' レシート全体で確定した税込額を各明細に対応付ける。
    Public Shared Function GetReceiptDetailAmounts(details As IEnumerable(Of ReceiptDetail), taxType As Integer, mode As String) As Dictionary(Of ReceiptDetail, BigInteger)
        Dim lines = details.ToList()
        Dim rates As New List(Of Integer?)
        For Each detail As ReceiptDetail In lines
            If detail.TaxRate Is Nothing Then Throw New InvalidOperationException("明細の税設定がありません。")
            rates.Add(If(detail.TaxRate.IsTaxable, New Integer?(detail.TaxRate.Rate), Nothing))
        Next
        Dim net = lines.Select(Function(detail) ExactMoney.Net(detail.Fee, detail.DiscountFee, detail.Quantity, detail.DiscountRate)).ToArray()
        Dim amounts = ExactMoney.ReceiptAmounts(net, rates, taxType, mode)
        Return Enumerable.Range(0, lines.Count).ToDictionary(Function(i) lines(i), Function(i) amounts(i))
    End Function

    ' レシートで確定した税込明細額を抽出し、必要なら人数按分して合算する。
    Public Shared Function SumDetails(details As IEnumerable(Of ReceiptDetail), taxType As Integer, mode As String,
                                      Optional signFilter As Integer = 0, Optional allocate As Boolean = False,
                                      Optional receiptAmounts As IDictionary(Of ReceiptDetail, BigInteger) = Nothing) As MoneyFraction
        ValidateTaxRoundingMode(mode)
        If taxType <> 0 AndAlso taxType <> 1 Then Throw New ArgumentException("不正な内税／外税区分です。")
        Dim lines = details.ToList()
        Dim amounts = If(receiptAmounts, GetReceiptDetailAmounts(lines, taxType, mode))
        Dim result As New MoneyFraction(BigInteger.Zero)
        For Each detail As ReceiptDetail In lines
            If signFilter <> 0 AndAlso ExactMoney.Net(detail.Fee, detail.DiscountFee, detail.Quantity, detail.DiscountRate).Sign <> signFilter Then Continue For
            Dim divisor = If(allocate, detail.Users.Count, 1)
            If divisor > 0 Then result += New MoneyFraction(amounts(detail), divisor)
        Next
        Return result
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

    ' レシートで確定した税込明細額からグループの参考金額を返す。
    Public Shared Function GroupCalculatedFee(receipt As Receipt, group As ReceiptGroup, Optional receiptAmounts As IDictionary(Of ReceiptDetail, BigInteger) = Nothing) As Integer
        Dim amounts = If(receiptAmounts, GetReceiptDetailAmounts(receipt.Groups.SelectMany(Function(g) g.Details), receipt.TaxType, receipt.TaxRoundingMode))
        Return CInt(SumDetails(group.Details, receipt.TaxType, receipt.TaxRoundingMode, receiptAmounts:=amounts).Rounded(receipt.TaxRoundingMode))
    End Function

    ' レシートの確定税額を配分した明細の参考金額を返す。
    Public Shared Function DetailCalculatedFee(receipt As Receipt, group As ReceiptGroup, detail As ReceiptDetail, Optional receiptAmounts As IDictionary(Of ReceiptDetail, BigInteger) = Nothing) As Integer
        Dim amounts = If(receiptAmounts, GetReceiptDetailAmounts(receipt.Groups.SelectMany(Function(g) g.Details), receipt.TaxType, receipt.TaxRoundingMode))
        Return CInt(amounts(detail))
    End Function
End Class
