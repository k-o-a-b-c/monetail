Public Class Transfer
    Public Property Seq As Integer
    Friend Property RevisionToken As String
    Public Property [Date] As DateTime
    Public Property Name As String
    Public Property Fee As Integer
    Public Property FromPaymentClassification As PaymentClassification
    Public Property ToPaymentClassification As PaymentClassification
    Public Property Tag As String
End Class
