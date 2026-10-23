Imports System.Collections.Generic
Imports System.Linq

' 日付と分類の軽減税率対象フラグから税率の初期値を選ぶ。
Public NotInheritable Class TaxRateSelection
    Private Sub New()
    End Sub

    ' 従来の8%／10%ルールに一致するマスタを返し、該当なしならNothingを返す。
    Public Shared Function GetDefaultRate(receiptDate As Date, reducedTaxTarget As Boolean,
                                          taxRates As IEnumerable(Of TaxRate)) As TaxRate
        If taxRates Is Nothing Then Throw New ArgumentNullException(NameOf(taxRates))
        Dim rate As Integer = If(receiptDate.Date >= New Date(2019, 10, 1) AndAlso Not reducedTaxTarget, 10, 8)
        Return taxRates.FirstOrDefault(Function(candidate) candidate.Rate = rate)
    End Function
End Class
