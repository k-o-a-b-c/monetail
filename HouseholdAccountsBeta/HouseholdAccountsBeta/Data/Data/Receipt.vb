Public Class Receipt
    Public Property Seq As Integer
    Public Property [Date] As DateTime
    Public Property ShopName As String
    Public Property ShopLocation As String
    Public Property TaxType As Integer

    Private _taxRoundingMode As String = "floor"

    Public Property TaxRoundingMode As String
        Get
            Return _taxRoundingMode
        End Get
        Set(ByVal value As String)
            Calculation.ValidateTaxRoundingMode(value)
            _taxRoundingMode = value
        End Set
    End Property
    Public Property PaymentClassification As PaymentClassification
    Public Property Tag As String

    Public Property Groups As List(Of ReceiptGroup)

    Public Property Points As List(Of ReceiptPoint)

    Public Sub New()
        Groups = New List(Of ReceiptGroup)()
        Points = New List(Of ReceiptPoint)()
    End Sub

    'Public ReadOnly Property TotalWithoutTax As Integer
    '    Get
    '        Dim groupTaxFee As New Dictionary(Of Integer, Double)()
    '        Dim groupNoTaxFee As New Dictionary(Of Integer, Double)()
    '        For Each group As ReceiptGroup In Groups
    '            Dim taxFee As Double = 0
    '            Dim nonTaxFee As Double = 0
    '            For Each detail As ReceiptDetail In group.Details
    '                Dim fee As Double = detail.Fee
    '                fee -= detail.DiscountFee
    '                fee *= detail.Quantity
    '                fee -= Math.Ceiling(fee * detail.DiscountRate / 100)
    '                If detail.TaxTarget Then
    '                    taxFee += fee
    '                Else
    '                    nonTaxFee += fee
    '                End If
    '            Next
    '            If groupTaxFee.ContainsKey(group.TaxRate.Rate) Then
    '                groupTaxFee(group.TaxRate.Rate) += taxFee
    '                groupNoTaxFee(group.TaxRate.Rate) += nonTaxFee
    '            Else
    '                groupTaxFee.Add(group.TaxRate.Rate, taxFee)
    '                groupNoTaxFee.Add(group.TaxRate.Rate, nonTaxFee)
    '            End If
    '        Next

    '        Dim subTotals As Double = 0
    '        For Each taxRate As Integer In groupTaxFee.Keys
    '            If TaxType = 0 Then
    '                subTotals += groupTaxFee(taxRate) / (1 + taxRate / 100) + groupNoTaxFee(taxRate)
    '            Else
    '                subTotals += groupTaxFee(taxRate) + groupNoTaxFee(taxRate)
    '            End If
    '        Next

    '        Return Convert.ToInt32(Math.Floor(subTotals))
    '    End Get
    'End Property

    Public ReadOnly Property Total As Integer
        Get
            Return CalculateTotal(0)
        End Get
    End Property

    ''' <summary>
    ''' 収入合計。値引き後の明細が負の場合を収入とみなす。
    ''' </summary>
    Public ReadOnly Property Incomes As Integer
        Get
            Return CalculateTotal(-1)
        End Get
    End Property

    ''' <summary>
    ''' 支出合計。値引き後の明細が正の場合を支出とみなす。
    ''' </summary>
    Public ReadOnly Property Expenses As Integer
        Get
            Return CalculateTotal(1)
        End Get
    End Property

    ' signFilter: 0 = 全明細、-1 = 収入、1 = 支出。
    ' 同じ税率の全グループを合算してから、一度だけ税端数を処理する。
    Private Function CalculateTotal(ByVal signFilter As Integer) As Integer
        Dim groupTaxFee As New Dictionary(Of Integer, Double)()
        Dim groupNoTaxFee As New Dictionary(Of Integer, Double)()
        For Each group As ReceiptGroup In Groups
            Dim taxFee As Double = 0
            Dim nonTaxFee As Double = 0
            For Each detail As ReceiptDetail In group.Details
                Dim fee As Double = detail.Fee
                fee -= detail.DiscountFee
                fee *= detail.Quantity
                fee -= Math.Ceiling(fee * detail.DiscountRate / 100)
                If (signFilter < 0 AndAlso fee >= 0) OrElse
                   (signFilter > 0 AndAlso fee <= 0) Then
                    Continue For
                End If
                If detail.TaxTarget Then
                    taxFee += fee
                Else
                    nonTaxFee += fee
                End If
            Next
            If groupTaxFee.ContainsKey(group.TaxRate.Rate) Then
                groupTaxFee(group.TaxRate.Rate) += taxFee
                groupNoTaxFee(group.TaxRate.Rate) += nonTaxFee
            Else
                groupTaxFee.Add(group.TaxRate.Rate, taxFee)
                groupNoTaxFee.Add(group.TaxRate.Rate, nonTaxFee)
            End If
        Next

        Dim subTotals As Double = 0
        For Each taxRate As Integer In groupTaxFee.Keys
            subTotals += Calculation.TaxRateSubtotal(groupTaxFee(taxRate), groupNoTaxFee(taxRate), taxRate, TaxType, TaxRoundingMode)
        Next
        Return Convert.ToInt32(subTotals)
    End Function

    Public Function GetGroupStatisticsList(ByVal users As List(Of User), ByVal IsAnd As Boolean, ByVal IsOnlyPerson As Boolean) As List(Of GroupStatistics)

        Dim GroupStatisticsList As New List(Of GroupStatistics)
        Dim GroupStatistics As GroupStatistics

        For Each group As ReceiptGroup In Groups
            Dim taxFee As Double = 0
            Dim nonTaxFee As Double = 0
            For Each detail As ReceiptDetail In group.Details
                If users.Count > 0 AndAlso
                   ((IsAnd AndAlso (detail.Users.Intersect(users).ToArray().Count <> users.Count OrElse detail.Users.Count <> users.Count)) _
                    OrElse (Not IsAnd AndAlso detail.Users.Intersect(users).ToArray().Count = 0)) Then
                    Continue For
                End If
                If IsAnd AndAlso users.Count = 0 AndAlso detail.Users.Count > 0 Then
                    Continue For
                End If
                Dim fee As Double = detail.Fee
                fee -= detail.DiscountFee
                fee *= detail.Quantity
                fee -= Math.Ceiling(fee * detail.DiscountRate / 100)
                Dim personNumber As Double
                If IsOnlyPerson Then
                    personNumber = detail.Users.Count
                Else
                    personNumber = 1
                End If
                If personNumber = 0 Then
                Else
                    If detail.TaxTarget Then
                        taxFee += fee / personNumber
                    Else
                        nonTaxFee += fee / personNumber
                    End If
                End If
            Next

            GroupStatistics = New GroupStatistics() With {
                .GroupCode = group.GroupClassification.Code,
                .YearMonth = Me.Date
            }
            ' 分類・個人別の参考額。レシートの会計合計には使用しない。
            Dim calculatedFee As Double = Calculation.TaxRateSubtotal(taxFee, nonTaxFee, group.TaxRate.Rate, TaxType, TaxRoundingMode)
            If IsOnlyPerson AndAlso TaxRoundingMode = "half_up" Then
                If TaxType <> 1 Then
                    calculatedFee = taxFee + nonTaxFee
                End If
                GroupStatistics.Fee = Convert.ToInt32(Math.Round(calculatedFee, 0, MidpointRounding.AwayFromZero))
            Else
                ' 既存 floor レシートの分類・個人別参考額も従来どおり維持する。
                GroupStatistics.Fee = Convert.ToInt32(calculatedFee)
            End If

            GroupStatisticsList.Add(GroupStatistics)
        Next

        Return GroupStatisticsList

    End Function

End Class
