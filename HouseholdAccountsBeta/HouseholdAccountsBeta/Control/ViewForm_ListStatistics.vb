' 分類集計
Partial Public Class ViewForm

    ''' <summary>
    ''' 分類集計を更新する
    ''' </summary>
    Private Sub UpdateListStatistics()

        ' 処理高速化（開始）
        GroupStatisticsDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
        GroupStatisticsDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing

        ' 表示内容クリア
        GroupStatisticsDataGridView.Rows.Clear()

        ' 1年分のデータを取得する
        Dim ReceiptList As List(Of Receipt) = DataAccess.GetReceipts(YearMonth.Year)

        ' 支払い者で絞り込む
        ReceiptList = ReceiptList.Where(Function(r)
                                            Return UserComboBox.SelectedIndex = 0 OrElse r.PaymentClassification.User.Code = DirectCast(UserComboBox.SelectedValue, Integer)
                                        End Function).ToList()

        ' 使用者で絞り込む
        Dim GroupStatisticsList As New List(Of GroupStatistics)
        For Each Receipt As Receipt In ReceiptList
            GroupStatisticsList.AddRange(Receipt.GetGroupStatisticsList(UserListBox.SelectedItems.Cast(Of User).ToList(), UserExactRadioButton.Checked, OnlyPersonCheckBox.Enabled AndAlso OnlyPersonCheckBox.Checked))
        Next

        ' 分類マスタを取得する
        Dim GroupClassificationList As List(Of GroupClassification) = DataAccess.GroupClassificationList()

        ' 分類毎に初期化する
        Dim GroupStatisticsDict As New Dictionary(Of Integer, Dictionary(Of Integer, Integer))()
        For Each Group As GroupClassification In GroupClassificationList
            GroupStatisticsDict.Add(Group.Code, New Dictionary(Of Integer, Integer)())
            For Month As Integer = 1 To 13
                GroupStatisticsDict(Group.Code).Add(Month, 0)
            Next
        Next

        ' 合計を初期化する
        Dim TotalDict As New Dictionary(Of Integer, Integer)()
        For Month As Integer = 1 To 13
            TotalDict.Add(Month, 0)
        Next

        ' 分類と合計に金額を加える
        For Each GroupStatistics As GroupStatistics In GroupStatisticsList
            GroupStatisticsDict.Item(GroupStatistics.GroupCode).Item(GroupStatistics.YearMonth.Month) += GroupStatistics.Fee
            GroupStatisticsDict.Item(GroupStatistics.GroupCode).Item(13) += GroupStatistics.Fee
            TotalDict.Item(GroupStatistics.YearMonth.Month) += GroupStatistics.Fee
            TotalDict.Item(13) += GroupStatistics.Fee
        Next

        ' 分類の行を生成して追加する
        For Each Group As GroupClassification In GroupClassificationList
            Dim values As New List(Of Object)()
            values.Add(Group.Code)
            values.Add(Group.Name)
            For Month As Integer = 1 To 13
                values.Add(GroupStatisticsDict.Item(Group.Code).Item(Month))
            Next
            GroupStatisticsDataGridView.Rows.Add(values.ToArray())
        Next

        ' 合計の行を生成して追加する
        Dim totalValues As New List(Of Object)()
        totalValues.Add(-1)
        totalValues.Add("計")
        For Month As Integer = 1 To 13
            totalValues.Add(TotalDict.Item(Month))
        Next
        GroupStatisticsDataGridView.Rows.Add(totalValues.ToArray())

        ' 処理高速化（終了）
        GroupStatisticsDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells
        GroupStatisticsDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize

    End Sub

    Private Sub GroupStatisticsDataGridView_CellMouseDoubleClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles GroupStatisticsDataGridView.CellMouseDoubleClick
        If e.RowIndex < 0 OrElse e.ColumnIndex < 2 Then
            Return
        End If
        Dim GroupCode As Integer = CInt(GroupStatisticsDataGridView.Rows(e.RowIndex).Cells("GroupCodeColumn").Value)
        Dim Month As Integer = e.ColumnIndex - 1
        If Month = 13 Then
            Month = 0
        End If

        Dim DetailViewForm As New DetailViewForm()
        DetailViewForm.DataAccess = DataAccess
        DetailViewForm.Year = YearMonth.Year
        DetailViewForm.Month = Month
        DetailViewForm.Mode = DetailViewFormMode.Group
        DetailViewForm.Code = GroupCode
        DetailViewForm.ShowDialog(Me)
    End Sub

End Class
