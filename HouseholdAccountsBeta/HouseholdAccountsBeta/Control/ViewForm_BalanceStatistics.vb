' 残高集計
Imports System.Windows.Forms.DataVisualization.Charting

Partial Public Class ViewForm

    ''' <summary>
    ''' 残高集計を行う
    ''' </summary>
    Private Sub UpdateBalanceStatistics()

        Dim FirstRowIndex As Integer = BalanceStatisticsDataGridView.FirstDisplayedScrollingRowIndex
        Dim SelectedRowIndex As Integer
        If BalanceStatisticsDataGridView.CurrentRow IsNot Nothing Then
            SelectedRowIndex = BalanceStatisticsDataGridView.CurrentRow.Index
        Else
            SelectedRowIndex = -1
        End If

        BalanceStatisticsChart.Series.Item(0).Points.Clear()
        UpdateStatistics(BalanceStatisticsDataGridView,
            Sub() PopulateBalanceStatistics(SelectedRowIndex, FirstRowIndex),
            Sub() BalanceStatisticsChart.Series.Item(0).Points.Clear())
    End Sub

    Private Sub PopulateBalanceStatistics(SelectedRowIndex As Integer, FirstRowIndex As Integer)

        ' 残高データ、支払い方法マスタ取得
        Dim BalanceStatisticsList As List(Of BalanceStatistics) = DataAccess.GetBalanceStatistics(YearMonth.Year)
        Dim PaymentClassificationList As List(Of PaymentClassification) = DataAccess.PaymentClassificationList()
        PaymentClassificationList = PaymentClassificationList.Where(Function(p) UserComboBox.SelectedIndex = 0 OrElse p.User.Code = DirectCast(UserComboBox.SelectedItem, User).Code).ToList()

        Dim RateDict As Dictionary(Of Integer, Rate) = Nothing
        If USDCheckBox.Checked Then
            RateDict = DataAccess.GetUsdJpyRatesForYear(YearMonth.Year)
            BalanceStatisticsDataGridView.Columns(1).HeaderText = "支払分類 (USD換算)"
            For Month As Integer = 1 To 12
                BalanceStatisticsDataGridView.Columns(Month + 1).HeaderText = Month & "月" & Environment.NewLine
                If RateDict.ContainsKey(Month) Then
                    BalanceStatisticsDataGridView.Columns(Month + 1).HeaderText &= RateDict.Item(Month).MiddleRate & "円"
                End If
            Next
        Else
            BalanceStatisticsDataGridView.Columns(1).HeaderText = "支払分類"
            For Month As Integer = 1 To 12
                BalanceStatisticsDataGridView.Columns(Month + 1).HeaderText = Month & "月"
            Next
        End If

        Dim totalDict As New Dictionary(Of Integer, Integer)()
        For Month As Integer = 1 To 12
            totalDict.Add(Month, 0)
        Next

        Dim LastMonth As Integer = 0
        Dim LastDate As DateTime = DateTime.MinValue
        Try
            ' 残高データの最新日付を取得し、最新月を特定
            LastDate = BalanceStatisticsList.OrderByDescending(Function(b) b.Date.Ticks).First().Date
            LastMonth = LastDate.AddDays(1).Month
            If LastMonth < LastDate.Month Then
                LastMonth = 0
            End If
        Catch
            '今年データなしのため昨年の残高データを取得
            Dim LastYearBalance = DataAccess.GetBalanceStatistics(YearMonth.Year - 1)
            Try
                LastDate = LastYearBalance.OrderByDescending(Function(b) b.Date.Ticks).First().Date
                If LastDate.Month = 12 Then
                    LastMonth = 1
                End If
            Catch
                '昨年データなし
                LastMonth = 0
            End Try
        End Try

        'If LastMonth > 0 Then
        '    LastDate = New DateTime(YearMonth.Year, LastMonth, DateTime.DaysInMonth(YearMonth.Year, LastMonth))
        'End If

        Dim ProjectedBalanceDict As New Dictionary(Of Integer, List(Of Integer))()
        Dim ProjectedBalanceMonthList As New List(Of Integer)()

        For Each Payment As PaymentClassification In PaymentClassificationList
            Dim values As New List(Of Object)()
            values.Add(Payment.Code)
            values.Add(Payment.Name)

            Dim value As Object

            For Month As Integer = 1 To 12
                value = Nothing

                Dim tergetMonth As Integer = Month

                ' 対象月の残高データを取得
                Dim BalanceStatisticsSubList As List(Of BalanceStatistics) = BalanceStatisticsList.Where(Function(b) b.PaymentClassificationCode = Payment.Code AndAlso b.Date.Month = tergetMonth).OrderByDescending(Function(b) b.Date.Ticks).ToList()

                If BalanceStatisticsSubList.Count > 0 Then
                    ' 対象月の残高データが存在する場合
                    Dim Balance As Integer
                    Dim ValuationAmount As Nullable(Of Integer) = Nothing

                    If ValuationAmountCheckBox.Checked Then
                        '最新の評価額を取得、評価額がない場合は残高を取得
                        Dim LatestBalanceStatistics As BalanceStatistics = BalanceStatisticsSubList.Where(Function(b) b.ValuationAmount IsNot Nothing).FirstOrDefault()
                        If LatestBalanceStatistics IsNot Nothing Then
                            ValuationAmount = LatestBalanceStatistics.ValuationAmount
                        Else
                            Balance = BalanceStatisticsSubList(0).Balance
                        End If
                    Else
                        ' 残高を取得
                        Balance = BalanceStatisticsSubList(0).Balance
                    End If

                    ' 値を設定
                    value = If(ValuationAmount.HasValue, ValuationAmount, Balance)
                    totalDict.Item(Month) += If(ValuationAmount.HasValue, ValuationAmount.GetValueOrDefault(), Balance)

                    ' 月末データもしくは本日かチェック
                    If BalanceStatisticsSubList(0).Date.Day <> DateTime.DaysInMonth(BalanceStatisticsSubList(0).Date.Year, BalanceStatisticsSubList(0).Date.Month) _
                       AndAlso BalanceStatisticsSubList(0).Date <> DateTime.Today Then
                        ' 月末データの場合、ProjectedBalanceDictに追加
                        If ProjectedBalanceDict.ContainsKey(Payment.Code) Then
                            ProjectedBalanceDict.Item(Payment.Code).Add(Month)
                        Else
                            ProjectedBalanceDict.Add(Payment.Code, New List(Of Integer) From {Month})
                        End If
                        If Not ProjectedBalanceMonthList.Contains(Month) Then
                            ProjectedBalanceMonthList.Add(Month)
                        End If
                    End If

                ElseIf Month <= LastMonth Then
                    Dim MonthLastDay As DateTime = New DateTime(YearMonth.Year, Month, DateTime.DaysInMonth(YearMonth.Year, Month))

                    ' 現在月までのデータが存在しない場合、前月の残高から推算して表示
                    If ValuationAmountCheckBox.Checked Then
                        Dim LastValuationAmount As Balance = DataAccess.GetLastValuationAmount(MonthLastDay, Payment)
                        If LastValuationAmount Is Nothing Then
                            Dim AutoBalance As Nullable(Of Integer) = DataAccess.GetAutoBalance(MonthLastDay, Payment)
                            value = AutoBalance
                            totalDict.Item(Month) += AutoBalance.GetValueOrDefault()
                        Else
                            value = LastValuationAmount.Groups.Where(Function(g) g.PaymentClassification.Code = Payment.Code).Select(Function(g) g.ValuationAmount).FirstOrDefault()
                            totalDict.Item(Month) += LastValuationAmount.Groups.Where(Function(g) g.PaymentClassification.Code = Payment.Code).Select(Function(g) g.ValuationAmount).FirstOrDefault().GetValueOrDefault()
                        End If
                    Else
                        Dim AutoBalance As Nullable(Of Integer) = DataAccess.GetAutoBalance(MonthLastDay, Payment)
                        value = AutoBalance
                        totalDict.Item(Month) += AutoBalance.GetValueOrDefault()
                    End If

                    If value IsNot Nothing Then
                        If ProjectedBalanceDict.ContainsKey(Payment.Code) Then
                            ProjectedBalanceDict.Item(Payment.Code).Add(Month)
                        Else
                            ProjectedBalanceDict.Add(Payment.Code, New List(Of Integer) From {Month})
                        End If
                        If Not ProjectedBalanceMonthList.Contains(Month) Then
                            ProjectedBalanceMonthList.Add(Month)
                        End If
                    End If
                End If

                If USDCheckBox.Checked AndAlso value IsNot Nothing Then
                    If RateDict.ContainsKey(Month) Then
                        Dim rate As Rate = RateDict.Item(Month)
                        value = Math.Round(CInt(value) / rate.MiddleRate, 2, MidpointRounding.AwayFromZero)
                    Else
                        value = Nothing
                    End If
                End If
                values.Add(value)
            Next
            BalanceStatisticsDataGridView.Rows.Add(values.ToArray())

            If ProjectedBalanceDict.ContainsKey(Payment.Code) Then
                Dim rowIndex As Integer = BalanceStatisticsDataGridView.Rows.Count - 1
                For Each Month As Integer In ProjectedBalanceDict.Item(Payment.Code)
                    BalanceStatisticsDataGridView.Rows(rowIndex).Cells(Month + 1).Style.BackColor = Color.MistyRose
                    BalanceStatisticsDataGridView.Rows(rowIndex).Cells(Month + 1).ToolTipText = "自動計算による予測残高です。"
                Next
            End If
        Next

        Dim totalValues As New List(Of Object)()
        totalValues.Add(-1)
        totalValues.Add("計")

        Dim Series As Series = BalanceStatisticsChart.Series.Item(0)
        Series.Points.Clear()

        For Month As Integer = 1 To 12
            Dim value As Object = totalDict.Item(Month)

            If USDCheckBox.Checked AndAlso value IsNot Nothing Then
                If RateDict.ContainsKey(Month) Then
                    Dim rate As Rate = RateDict.Item(Month)
                    value = Math.Round(CInt(value) / rate.MiddleRate, 2, MidpointRounding.AwayFromZero)
                Else
                    value = Nothing
                End If
            End If

            Series.Points.AddXY(New DateTime(YearMonth.Year, Month, 1), If(value IsNot Nothing, value, 0))
            totalValues.Add(value)
        Next
        BalanceStatisticsDataGridView.Rows.Add(totalValues.ToArray())
        For Each ProjectedMonth As Integer In ProjectedBalanceMonthList
            BalanceStatisticsDataGridView.Rows(BalanceStatisticsDataGridView.Rows.Count - 1).Cells(ProjectedMonth + 1).Style.BackColor = Color.MistyRose
            BalanceStatisticsDataGridView.Rows(BalanceStatisticsDataGridView.Rows.Count - 1).Cells(ProjectedMonth + 1).ToolTipText = "自動計算による予測残高です。"
        Next

        'For Month As Integer = 1 To 12
        '    Dim cellStyle As DataGridViewCellStyle = BalanceStatisticsDataGridView.Columns.Item(Month).DefaultCellStyle
        '    If Month = LastMonth Then
        '        cellStyle.BackColor = Color.MistyRose
        '    Else
        '        cellStyle.BackColor = Color.White
        '    End If
        '    BalanceStatisticsDataGridView.Columns.Item(Month).DefaultCellStyle = cellStyle
        'Next

        If SelectedRowIndex >= 0 AndAlso SelectedRowIndex < BalanceStatisticsDataGridView.Rows.Count Then
            BalanceStatisticsDataGridView.CurrentCell = BalanceStatisticsDataGridView.Rows(SelectedRowIndex).Cells(1)
        End If
        If FirstRowIndex >= 0 AndAlso FirstRowIndex < BalanceStatisticsDataGridView.Rows.Count Then
            BalanceStatisticsDataGridView.FirstDisplayedScrollingRowIndex = FirstRowIndex
        End If

    End Sub

    Private Sub ValuationAmountCheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles ValuationAmountCheckBox.CheckedChanged
        UpdateBalanceStatistics()
    End Sub

    Private Sub USDCheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles USDCheckBox.CheckedChanged
        For Each Column As DataGridViewColumn In BalanceStatisticsDataGridView.Columns
            If Column.Index = 0 Then
                Continue For
            End If
            If USDCheckBox.Checked Then
                Column.DefaultCellStyle.Format = "N2"
            Else
                Column.DefaultCellStyle.Format = "N0"
            End If
        Next
        UpdateBalanceStatistics()
    End Sub

    Private Sub ChartViewCheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles ChartViewCheckBox.CheckedChanged
        BalanceStatisticsChart.Visible = ChartViewCheckBox.Checked
    End Sub


    Private Sub BalanceStatisticsDataGridView_CellMouseDoubleClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles BalanceStatisticsDataGridView.CellMouseDoubleClick
        If e.RowIndex < 0 OrElse e.ColumnIndex < 2 Then
            Return
        End If
        If BalanceStatisticsDataGridView.Rows(e.RowIndex).Cells(0).Value Is Nothing Then
            Return
        End If
        Dim PaymentCode As Integer = CInt(BalanceStatisticsDataGridView.Rows(e.RowIndex).Cells("BalancePaymentCodeColumn").Value)
        Dim Month As Integer = e.ColumnIndex - 1
        If Month = 13 Then
            Month = 0
        End If

        Dim DetailViewForm As New DetailViewForm()
        DetailViewForm.DataAccess = DataAccess
        DetailViewForm.Year = YearMonth.Year
        DetailViewForm.Month = Month
        DetailViewForm.Mode = DetailViewFormMode.Payment
        DetailViewForm.Code = PaymentCode
        DetailViewForm.ShowDialog(Me)
    End Sub

End Class
