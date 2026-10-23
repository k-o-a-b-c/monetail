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

    ' 単価値引・数量・率値引を適用し、税処理前の整数金額をDecimalで求める。
    Public Function CalculateDiscountedFee() As Decimal
        Dim fee As Decimal = Me.Fee
        fee -= DiscountFee
        fee *= Quantity
        fee -= Math.Ceiling(fee * DiscountRate / 100D)
        Return fee
    End Function

    Public Sub New()
        Users = New List(Of User)()
    End Sub
End Class
