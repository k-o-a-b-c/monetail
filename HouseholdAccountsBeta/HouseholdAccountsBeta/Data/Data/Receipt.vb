Public Class Receipt
    Public Property Seq As Integer
    Friend Property RevisionToken As String
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

    ' 全明細を税率別に合算したレシート総額を返す。
    Public ReadOnly Property Total As Integer
        Get
            Return GetTotal(TaxType, TaxRoundingMode)
        End Get
    End Property

    ' レシートの確定税額を配分した収入明細の合計を返す。
    Public ReadOnly Property Incomes As Integer
        Get
            Return CalculateTotal(-1, TaxType, TaxRoundingMode)
        End Get
    End Property

    ' レシートの確定税額を配分した支出明細の合計を返す。
    Public ReadOnly Property Expenses As Integer
        Get
            Return CalculateTotal(1, TaxType, TaxRoundingMode)
        End Get
    End Property

    ' 画面の未確定設定でプレビューし、元レシートは変更しない。
    Public Function GetTotal(taxType As Integer, taxRoundingMode As String) As Integer
        Return CalculateTotal(0, taxType, taxRoundingMode)
    End Function

    ' レシートの税額を確定してから収支の抽出条件に従って合算する。
    Private Function CalculateTotal(signFilter As Integer, taxType As Integer, mode As String) As Integer
        Return CInt(Calculation.SumDetails(Groups.SelectMany(Function(g) g.Details), taxType, mode, signFilter).Rounded(mode))
    End Function

    ' レシートの確定税込額を対象者で絞り、分類別の統計金額を計算する。
    Public Function GetGroupStatisticsList(users As List(Of User), IsAnd As Boolean, IsOnlyPerson As Boolean) As List(Of GroupStatistics)
        Dim result As New List(Of GroupStatistics)
        Dim amounts = Calculation.GetReceiptDetailAmounts(Groups.SelectMany(Function(g) g.Details), TaxType, TaxRoundingMode)
        For Each group As ReceiptGroup In Groups
            Dim lines = group.Details.Where(Function(detail)
                                                If users.Count > 0 Then
                                                    If IsAnd Then Return detail.Users.Intersect(users).Count() = users.Count AndAlso detail.Users.Count = users.Count
                                                    Return detail.Users.Intersect(users).Any()
                                                End If
                                                Return Not IsAnd OrElse detail.Users.Count = 0
                                            End Function)
            ' 確定税込額の1/3等を近似せず合算し、最終参考円額だけ丸める。
            Dim amount = Calculation.SumDetails(lines, TaxType, TaxRoundingMode, allocate:=IsOnlyPerson, receiptAmounts:=amounts)
            result.Add(New GroupStatistics With {.GroupCode = group.GroupClassification.Code, .YearMonth = Me.Date,
                                                 .Fee = CInt(amount.Rounded(TaxRoundingMode))})
        Next
        Return result
    End Function
End Class
