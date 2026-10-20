' 分類・収支集計に共通する表示の復帰処理
Partial Public Class ViewForm

    Private Sub UpdateStatistics(grid As DataGridView, populate As Action)
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        grid.Rows.Clear()
        Try
            populate()
        Catch ex As OverflowException
            ' 途中までの表や前の条件の合計を、正常な結果として残さない。
            grid.Rows.Clear()
            Dim values As New List(Of Object) From {Nothing, "集計不可"}
            For month As Integer = 1 To 13
                values.Add("範囲超過")
            Next
            Dim row = grid.Rows(grid.Rows.Add(values.ToArray()))
            For Each cell As DataGridViewCell In row.Cells
                cell.ToolTipText = "合算する金額が扱える範囲を超えたため、集計表を表示できません。年や絞り込み条件を変更してください。"
            Next
        Finally
            ' 別の例外が発生した場合も表示設定を元へ戻し、例外自体は呼出元へ返す。
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        End Try
    End Sub

End Class
