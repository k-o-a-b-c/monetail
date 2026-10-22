Public Class ReceiptGroup
    Public Property ReceiptSeq As Integer
    Public Property Seq As Integer
    Public Property GroupClassification As GroupClassification
    Public Property Tag As String

    Public Property Details As List(Of ReceiptDetail)

    Public Sub New()
        Details = New List(Of ReceiptDetail)()
    End Sub

End Class
