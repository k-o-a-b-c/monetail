Imports System.Configuration

Public Class ReceiptDetail
    Public Property ReceiptSeq As Integer
    Public Property GroupSeq As Integer
    Public Property Seq As Integer
    Public Property Name As String
    Public Property Fee As Integer
    Public Property Quantity As Integer
    Public Property TaxRate As TaxRate
    Public Property DiscountFee As Integer
    Public Property DiscountRate As Decimal
    Public Property Tag As String
    Public Property Users As List(Of User)

    Public Sub New()
        Users = New List(Of User)()
    End Sub
End Class
