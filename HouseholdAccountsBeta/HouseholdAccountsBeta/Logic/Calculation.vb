Imports System.Windows.Forms.VisualStyles.VisualStyleElement.ListView

Public Class Calculation

    Public Shared DataAccess As DataAccess

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
            taxFee = Math.Floor(taxFee * (1 + Group.TaxRate.Rate / 100))
        End If
        Return Convert.ToInt32(taxFee + nonTaxFee)
    End Function

    Public Shared Function DetailCalculatedFee(ByVal Receipt As Receipt, ByVal Group As ReceiptGroup, ByVal Detail As ReceiptDetail) As Integer
        Dim fee As Double = Detail.Fee
        fee -= detail.DiscountFee
        fee *= Detail.Quantity
        fee -= Math.Ceiling(fee * Detail.DiscountRate / 100)
        If Detail.TaxTarget AndAlso Receipt.TaxType = 1 Then
            fee = Math.Floor(fee * (1 + Group.TaxRate.Rate / 100))
        End If
        Return Convert.ToInt32(fee)
    End Function

End Class
