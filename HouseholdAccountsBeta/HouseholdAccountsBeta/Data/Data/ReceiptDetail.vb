Imports System.Configuration

Public Class ReceiptDetail
    Public Property ReceiptSeq As Integer
    Public Property GroupSeq As Integer
    Public Property Seq As Integer
    Public Property Name As String
    Public Property Fee As Integer
    Public Property Quantity As Integer
    Public Property TaxTarget As Boolean
    Public Property DiscountFee As Integer
    ' 割引率を十進数で保持し、率値引の整数境界で生じる誤差を防ぐ。
    Public Property DiscountRate As Decimal
    Public Property Tag As String
    Public Property Users As List(Of User)

    Public Sub New()
        Users = New List(Of User)()
    End Sub
End Class
