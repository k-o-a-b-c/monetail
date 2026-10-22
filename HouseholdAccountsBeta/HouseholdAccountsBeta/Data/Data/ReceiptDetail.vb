Imports System.Configuration

Public Class ReceiptDetail
    Public Property ReceiptSeq As Integer
    Public Property GroupSeq As Integer
    Public Property Seq As Integer
    Public Property Name As String
    Public Property Fee As Integer
    Public Property Quantity As Integer
    ' 明細に適用する税率・課税種別を保持する。
    Public Property TaxRate As TaxRate
    Public Property DiscountFee As Integer
    ' 明細の割引率を十進数で保持する。
    Public Property DiscountRate As Decimal
    Public Property Tag As String
    Public Property Users As List(Of User)

    Public Sub New()
        Users = New List(Of User)()
    End Sub
End Class
