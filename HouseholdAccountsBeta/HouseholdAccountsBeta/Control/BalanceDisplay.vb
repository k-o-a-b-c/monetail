' 推算不能を「データなし」のNothingと区別し、画面の表示だけで扱う。
Public NotInheritable Class BalanceDisplay
    Private Sub New()
    End Sub

    Public Shared Function AutoBalance(target As DateTime, payment As PaymentClassification) As Object
        Try
            Return Calculation.AutoBalance(target, payment)
        Catch ex As OverflowException
            Return "範囲超過"
        End Try
    End Function
End Class
