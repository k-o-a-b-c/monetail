Imports System.Linq
Imports System.Reflection

Friend Module BalanceOverflowTests
    Private ReadOnly target As New Date(2026, 2, 28)

    Public Sub PredictionRanges()
        For kind As Integer = 0 To 5
            Dim scenario = kind
            BalanceBoundaryTests.EachForeignKeyMode(
                Sub(fixture)
                    Dim amount As Integer = If(scenario Mod 2 = 0, 1500000000, -1500000000)
                    BalanceBoundaryTests.AddBalance(fixture, New Date(2026, 1, 31), amount, 1)
                    If scenario < 2 OrElse scenario >= 4 Then
                        BalanceBoundaryTests.AddReceipt(fixture, New Date(2026, 2, 1), -amount, 1)
                    End If
                    If scenario >= 2 Then
                        ' 4/5は最終値だけなら元の残高に戻るが、途中の推算で範囲を超える。
                        Dim outgoing As Boolean = scenario >= 4 OrElse amount < 0
                        BalanceBoundaryTests.AddTransfer(fixture, New Date(2026, 2, 2),
                            If(scenario >= 4, amount, Math.Abs(amount)), If(outgoing, 1, 2), If(outgoing, 2, 1))
                    End If
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub()
                            Dim payment = fixture.Data.PaymentClassification(1)
                            Throws(Of OverflowException)(Sub() fixture.Data.GetAutoBalance(target, payment))
                            Throws(Of OverflowException)(Sub() Calculation.AutoBalance(target, payment))
                            Equal("範囲超過", CStr(BalanceDisplay.AutoBalance(target, payment)))
                        End Sub)
                End Sub)
        Next
        For Each amount In New Integer() {Integer.MinValue, 0, Integer.MaxValue}
            Dim expected = amount
            BalanceBoundaryTests.EachForeignKeyMode(
                Sub(fixture)
                    BalanceBoundaryTests.AddBalance(fixture, New Date(2026, 1, 31), expected, 1)
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub()
                            BalanceBoundaryTests.AssertBoth(fixture, target, 1, expected)
                            Equal(expected, CInt(BalanceDisplay.AutoBalance(target, fixture.Data.PaymentClassification(1))))
                            Equal(True, BalanceDisplay.AutoBalance(target, fixture.Data.PaymentClassification(3)) Is Nothing)
                        End Sub)
                End Sub)
        Next
        Dim previous = Calculation.DataAccess
        Try
            Calculation.DataAccess = Nothing
            Throws(Of NullReferenceException)(Sub() BalanceDisplay.AutoBalance(target, New PaymentClassification))
        Finally
            Calculation.DataAccess = previous
        End Try
    End Sub

    Public Sub RecordedStatistics()
        For Each sign As Integer In New Integer() {1, -1}
            Dim direction = sign
            BalanceBoundaryTests.EachForeignKeyMode(
                Sub(fixture)
                    OtherOwner(fixture)
                    Dim balance As New Balance With {.Date = New Date(2026, 1, 31), .Tag = "recorded"}
                    For code As Integer = 1 To 2
                        balance.Groups.Add(New BalanceGroup With {.PaymentClassification = fixture.Data.PaymentClassification(code),
                            .Balance = direction * 1500000000, .ValuationAmount = 100, .Tag = ""})
                    Next
                    fixture.Data.RegisterBalance(balance)
                    fixture.Execute("INSERT INTO t_exchange_rates VALUES('2026-01-31','USD','JPY',100),('2026-02-28','USD','JPY',100)")
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub()
                            Using form As New ViewForm(fixture.Data)
                                form.MainTabControl.SelectedTab = form.BalanceStatisticsTabPage
                                form.Show()
                                form.ValuationAmountCheckBox.Checked = True
                                AssertNormal(form, 1, 200)
                                ' 既存のグラフと、2番目の支払方法の途中加算を両方消す。
                                form.BalanceStatisticsDataGridView.CurrentCell = form.BalanceStatisticsDataGridView.Rows(3).Cells(1)
                                form.ValuationAmountCheckBox.Checked = False
                                AssertOverflow(form)
                                form.USDCheckBox.Checked = True
                                AssertOverflow(form)
                                form.ValuationAmountCheckBox.Checked = True
                                AssertNormal(form, 1, 2)
                                form.USDCheckBox.Checked = False
                                form.ValuationAmountCheckBox.Checked = False
                                AssertOverflow(form)
                                form.UserComboBox.SelectedValue = 2
                                form.RefreshBalanceStatistics()
                                AssertNormal(form, 1, direction * 1500000000)
                                form.UserComboBox.SelectedIndex = 0
                                form.RefreshBalanceStatistics()
                                AssertOverflow(form)
                                form.RefreshBalanceStatistics(2027)
                                AssertNormal(form, 1, 0)
                            End Using
                        End Sub)
                End Sub)
        Next
    End Sub

    Public Sub ProjectedStatistics()
        BalanceBoundaryTests.EachForeignKeyMode(
            Sub(fixture)
                OtherOwner(fixture)
                BalanceBoundaryTests.AddBalance(fixture, New Date(2026, 1, 31), 1500000000, 1)
                BalanceBoundaryTests.AddReceipt(fixture, New Date(2026, 2, 1), -1500000000, 1)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New ViewForm(fixture.Data)
                            form.MainTabControl.SelectedTab = form.BalanceStatisticsTabPage
                            form.Show()
                            form.ValuationAmountCheckBox.Checked = True
                            AssertNormal(form, 2, 9999)
                            form.ValuationAmountCheckBox.Checked = False
                            AssertOverflow(form)
                            form.ChartViewCheckBox.Checked = False
                            form.ChartViewCheckBox.Checked = True
                            AssertOverflow(form)
                            form.UserComboBox.SelectedValue = 2
                            form.RefreshBalanceStatistics()
                            AssertNormal(form, 2, 0)
                            form.UserComboBox.SelectedIndex = 0
                            form.RefreshBalanceStatistics()
                            AssertOverflow(form)
                            form.RefreshBalanceStatistics(2027)
                            AssertNormal(form, 2, 0)
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Public Sub EditorCopyAndRecovery()
        BalanceBoundaryTests.EachForeignKeyMode(
            Sub(fixture)
                BalanceBoundaryTests.AddBalance(fixture, New Date(2026, 1, 31), 1500000000, 1)
                BalanceBoundaryTests.AddReceipt(fixture, New Date(2026, 2, 2), -1500000000, 1)
                BalanceBoundaryTests.AddBalance(fixture, target, 77, 1)
                Dim balance = fixture.Data.GetBalances(New Date(2026, 2, 1)).Single()
                Dim previous = Calculation.DataAccess
                Try
                    Calculation.DataAccess = fixture.Data
                    Using form As New RegisterBalanceForm With {.DataAccess = fixture.Data, .Balance = balance}
                        BalanceBoundaryTests.ReadOnlyCase(fixture,
                            Sub()
                                Program.OpenForm(form)
                                Dim row = form.BalanceDataGridView.Rows(0)
                                Equal("範囲超過", CStr(row.Cells(form.AutoBalanceColumn.Index).Value))
                                CopyPrediction(form)
                                Equal(77, CInt(row.Cells(form.BalanceColumn.Index).Value))
                                Equal(77, balance.Groups.Single().Balance)
                                SetDateAndValidate(form, New Date(2026, 1, 30))
                                Equal(True, row.Cells(form.AutoBalanceColumn.Index).Value Is Nothing)
                                CopyPrediction(form)
                                Equal(True, row.Cells(form.BalanceColumn.Index).Value Is Nothing)
                                SetDateAndValidate(form, New Date(2026, 2, 1))
                                Equal(1500000000, CInt(row.Cells(form.AutoBalanceColumn.Index).Value))
                                CopyPrediction(form)
                                Equal(1500000000, CInt(row.Cells(form.BalanceColumn.Index).Value))
                                SetDateAndValidate(form, target)
                                Equal("範囲超過", CStr(row.Cells(form.AutoBalanceColumn.Index).Value))
                                CopyPrediction(form)
                                Equal(1500000000, CInt(row.Cells(form.BalanceColumn.Index).Value))
                            End Sub)
                        form.BalanceDataGridView.Rows(0).Cells(form.BalanceColumn.Index).Value = 88
                        Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save manual balance despite unavailable prediction")
                        Equal(DialogResult.OK, form.DialogResult)
                        Equal(88, fixture.Data.GetBalances(New Date(2026, 2, 1)).Single().Groups.Single().Balance)
                    End Using
                Finally
                    Calculation.DataAccess = previous
                End Try
            End Sub)
    End Sub

    Private Sub SetDateAndValidate(form As RegisterBalanceForm, value As Date)
        form.BalanceDateTextBox.Value = value
        ' 再計算は欄を離れたValidatedで行う。値の代入だけでは発生しない。
        GetType(RegisterBalanceForm).GetMethod("BalanceDateTextBox_Validated", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(
            form, {form.BalanceDateTextBox, EventArgs.Empty})
    End Sub

    Private Sub CopyPrediction(form As RegisterBalanceForm)
        GetType(RegisterBalanceForm).GetMethod("BalanceDataGridView_CellContentClick", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(
            form, {form.BalanceDataGridView, New DataGridViewCellEventArgs(form.CopyAutoBalanceColumn.Index, 0)})
    End Sub

    Private Sub OtherOwner(fixture As DatabaseFixture)
        ' マスタのキャッシュが作られる前に、絞り込み用の支払者を設定する。
        fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'other','other-account',2);" &
            "UPDATE m_payment_classification SET user_code=2 WHERE code=2")
    End Sub

    Private Sub AssertOverflow(form As ViewForm)
        Dim grid = form.BalanceStatisticsDataGridView
        Equal(1, grid.Rows.Count)
        Equal(Nothing, grid.Rows(0).Cells(0).Value)
        Equal("集計不可", CStr(grid.Rows(0).Cells(1).Value))
        For column As Integer = 2 To grid.Columns.Count - 1
            Equal("範囲超過", CStr(grid.Rows(0).Cells(column).Value))
        Next
        Equal(0, form.BalanceStatisticsChart.Series(0).Points.Count, "No old or partial graph may remain")
        Equal(DataGridViewAutoSizeRowsMode.DisplayedCells, grid.AutoSizeRowsMode)
        Equal(DataGridViewColumnHeadersHeightSizeMode.AutoSize, grid.ColumnHeadersHeightSizeMode)
        GetType(ViewForm).GetMethod("BalanceStatisticsDataGridView_CellMouseDoubleClick", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(
            form, {grid, New DataGridViewCellMouseEventArgs(2, 0, 0, 0, New MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0))})
    End Sub

    Private Sub AssertNormal(form As ViewForm, month As Integer, expected As Double)
        Dim grid = form.BalanceStatisticsDataGridView
        Equal(True, grid.Rows.Count > 1)
        Equal(expected, Convert.ToDouble(grid.Rows(grid.Rows.Count - 1).Cells(month + 1).Value))
        Equal(12, form.BalanceStatisticsChart.Series(0).Points.Count)
        Equal(expected, form.BalanceStatisticsChart.Series(0).Points(month - 1).YValues(0))
        Equal(DataGridViewAutoSizeRowsMode.DisplayedCells, grid.AutoSizeRowsMode)
        Equal(DataGridViewColumnHeadersHeightSizeMode.AutoSize, grid.ColumnHeadersHeightSizeMode)
    End Sub
End Module
