Public Class Calculation

    Public Shared DataAccess As DataAccess

    Public Shared Sub ValidateTaxRoundingMode(ByVal mode As String)
        If mode <> "floor" AndAlso mode <> "half_up" Then
            Throw New ArgumentException("税端数処理は floor または half_up を指定してください。", NameOf(mode))
        End If
    End Sub

    ''' <summary>
    ''' 同一税率の合算額から税込額をDecimalで求め、税率乗算の誤差を防ぐ。
    ''' </summary>
    Public Shared Function TaxRateSubtotal(ByVal taxFee As Decimal, ByVal nonTaxFee As Decimal,
                                           ByVal taxRate As Integer, ByVal taxType As Integer,
                                           ByVal taxRoundingMode As String) As Decimal
        ValidateTaxRoundingMode(taxRoundingMode)
        If taxType <> 1 Then
            Return Math.Floor(taxFee + nonTaxFee)
        End If
        If taxRoundingMode = "floor" Then
            Return Math.Floor(taxFee * (1D + taxRate / 100D) + nonTaxFee)
        End If

        Dim tax As Decimal = Math.Round(taxFee * taxRate / 100D, 0, MidpointRounding.AwayFromZero)
        Return taxFee + tax + nonTaxFee
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

    ' 分類内で合算した金額に税と端数処理を適用し、分類の参考額を求める。
    Public Shared Function GroupCalculatedFee(ByVal Receipt As Receipt, ByVal Group As ReceiptGroup) As Integer
        Dim taxFee As Decimal = 0D
        Dim nonTaxFee As Decimal = 0D
        For Each detail As ReceiptDetail In Group.Details
            Dim fee As Decimal = detail.Fee
            fee -= detail.DiscountFee
            fee *= detail.Quantity
            fee -= Math.Ceiling(fee * detail.DiscountRate / 100D)
            If detail.TaxTarget Then
                taxFee += fee
            Else
                nonTaxFee += fee
            End If
        Next
        If Receipt.TaxType = 1 Then
            taxFee = TaxRateSubtotal(taxFee, 0, Group.TaxRate.Rate, Receipt.TaxType, Receipt.TaxRoundingMode)
        End If
        Return Convert.ToInt32(taxFee + nonTaxFee)
    End Function

    ' 明細の値引後金額に税と端数処理を適用し、明細の参考額を求める。
    Public Shared Function DetailCalculatedFee(ByVal Receipt As Receipt, ByVal Group As ReceiptGroup, ByVal Detail As ReceiptDetail) As Integer
        Dim fee As Decimal = Detail.Fee
        fee -= detail.DiscountFee
        fee *= Detail.Quantity
        fee -= Math.Ceiling(fee * Detail.DiscountRate / 100D)
        If Detail.TaxTarget AndAlso Receipt.TaxType = 1 Then
            Return Convert.ToInt32(TaxRateSubtotal(fee, 0D, Group.TaxRate.Rate, Receipt.TaxType, Receipt.TaxRoundingMode))
        End If
        Return Convert.ToInt32(fee)
    End Function

End Class
