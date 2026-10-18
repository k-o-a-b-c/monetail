Public Class Calculation

    Public Shared DataAccess As DataAccess

    Public Shared Sub ValidateTaxRoundingMode(ByVal mode As String)
        If mode <> "floor" AndAlso mode <> "half_up" Then
            Throw New ArgumentException("税端数処理は floor または half_up を指定してください。", NameOf(mode))
        End If
    End Sub

    ''' <summary>
    ''' 同一税率で合算済みの課税額・非課税額から税込額を求める。
    ''' </summary>
    Public Shared Function TaxRateSubtotal(ByVal taxFee As Double, ByVal nonTaxFee As Double,
                                           ByVal taxRate As Integer, ByVal taxType As Integer,
                                           ByVal taxRoundingMode As String) As Double
        ValidateTaxRoundingMode(taxRoundingMode)
        If taxType <> 1 Then
            Return Math.Floor(taxFee + nonTaxFee)
        End If
        If taxRoundingMode = "floor" Then
            ' 負額や浮動小数点の境界を含め、既存レシートの計算式を維持する。
            Return Math.Floor(taxFee * (1 + taxRate / 100) + nonTaxFee)
        End If

        ' half_up は税額を Decimal で求め、負の中間値もゼロから遠い側へ丸める。
        Dim tax As Decimal = Math.Round(CDec(taxFee) * taxRate / 100D, 0, MidpointRounding.AwayFromZero)
        Return Decimal.ToDouble(CDec(taxFee) + tax + CDec(nonTaxFee))
    End Function

    Public Shared Function AutoBalance(ByVal BaseDate As DateTime, ByVal PaymentClassification As PaymentClassification) As Nullable(Of Integer)
        Dim balance As Integer = 0
        Dim dateFrom As DateTime = DateTime.MinValue
        Dim dateTo As DateTime = BaseDate.Date + New TimeSpan(23, 59, 59)
        Dim payment As PaymentClassification = PaymentClassification

        Dim LastBalance As Balance = DataAccess.GetLastBalance(BaseDate.Date, payment)
        If LastBalance IsNot Nothing Then
            dateFrom = LastBalance.Date + New TimeSpan(23, 59, 59)
            balance = LastBalance.Groups.FirstOrDefault(Function(g) g.PaymentClassification.Code = payment.Code).Balance
        End If

        Dim receipts As List(Of Receipt) = DataAccess.GetPaymentReceipts(dateFrom, dateTo, payment)
        For Each receipt As Receipt In receipts
            balance -= receipt.Total
        Next

        Dim transfers As List(Of Transfer) = DataAccess.GetPaymentTransfers(dateFrom, dateTo, payment)
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

    Public Shared Function GroupCalculatedFee(ByVal Receipt As Receipt, ByVal Group As ReceiptGroup) As Integer
        Dim taxFee As Double = 0
        Dim nonTaxFee As Double = 0
        For Each detail As ReceiptDetail In Group.Details
            Dim fee As Double = detail.Fee
            fee -= detail.DiscountFee
            fee *= detail.Quantity
            fee -= Math.Ceiling(fee * detail.DiscountRate / 100)
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

    Public Shared Function DetailCalculatedFee(ByVal Receipt As Receipt, ByVal Group As ReceiptGroup, ByVal Detail As ReceiptDetail) As Integer
        Dim fee As Double = Detail.Fee
        fee -= detail.DiscountFee
        fee *= Detail.Quantity
        fee -= Math.Ceiling(fee * Detail.DiscountRate / 100)
        If Detail.TaxTarget AndAlso Receipt.TaxType = 1 Then
            fee = TaxRateSubtotal(fee, 0, Group.TaxRate.Rate, Receipt.TaxType, Receipt.TaxRoundingMode)
        End If
        Return Convert.ToInt32(fee)
    End Function

End Class
