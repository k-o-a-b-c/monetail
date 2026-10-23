Public NotInheritable Class ReceiptAmountValidation
    Private Sub New()
    End Sub

    ' レシートの確定税額を使い、会計・収支・参考額の整数範囲を検証する。
    Public Shared Function IsInRange(receipt As Receipt, taxType As Integer, roundingMode As String) As Boolean
        ' 計算用の税設定だけを持ち、元レシートや明細は変更しない。
        Dim preview As New Receipt With {.Groups = receipt.Groups, .TaxType = taxType, .TaxRoundingMode = roundingMode}
        Try
            ' 相殺後の合計だけでなく、収入・支出を別々に整数変換できることも確認する。
            Dim total As Integer = preview.Total
            Dim income As Integer = preview.Incomes
            Dim expense As Integer = preview.Expenses
            Dim amounts = Calculation.GetReceiptDetailAmounts(preview.Groups.SelectMany(Function(g) g.Details), taxType, roundingMode)
            For Each group As ReceiptGroup In preview.Groups
                Calculation.GroupCalculatedFee(preview, group, amounts)
                For Each detail As ReceiptDetail In group.Details
                    Calculation.DetailCalculatedFee(preview, group, detail, amounts)
                Next
            Next
            Return True
        Catch ex As OverflowException
            Return False
        End Try
    End Function

    ' 元の明細を変更せずに追加・更新後の金額範囲を検証する。
    Public Shared Function CanApplyDetail(receipt As Receipt, target As ReceiptGroup, previous As ReceiptDetail,
                                          pending As ReceiptDetail, taxType As Integer, roundingMode As String) As Boolean
        If Not receipt.Groups.Contains(target) Then Throw New ArgumentException("対象グループがレシートにありません。", NameOf(target))
        ' 更新対象のリストだけを複製し、検証前に元の明細を差し替えない。
        Dim replacement As New ReceiptGroup With {
            .GroupClassification = target.GroupClassification,
            .Details = New List(Of ReceiptDetail)(target.Details)
        }
        If previous Is Nothing Then
            replacement.Details.Add(pending)
        Else
            Dim index As Integer = replacement.Details.IndexOf(previous)
            If index < 0 Then Throw New ArgumentException("更新対象の明細がありません。", NameOf(previous))
            replacement.Details(index) = pending
        End If
        Dim groups As New List(Of ReceiptGroup)(receipt.Groups)
        groups(groups.IndexOf(target)) = replacement
        Return IsInRange(New Receipt With {.Groups = groups}, taxType, roundingMode)
    End Function
End Class
