Imports System.Linq
Imports System.Reflection

' 集計の製品partialとDesignerを使う。起動設定・他タブは含めず、一時DBで初期化する。
Partial Public Class ViewForm
    Private DataAccess As DataAccess
    Private Property YearMonth As Date

    Friend Sub New(data As DataAccess)
        InitializeComponent()
        DataAccess = data
        Dim users = data.UserList().ToList()
        users.Insert(0, New User With {.Code = 0, .Name = ""})
        UserComboBox.DataSource = users
        UserComboBox.SelectedIndex = 0
        UserListBox.DataSource = data.UserList()
        UserListBox.ClearSelected()
        UserPartialRadioButton.Checked = True
        YearMonth = New Date(2026, 1, 1)
    End Sub

    Friend Sub RefreshStatistics(categories As Boolean, Optional year As Integer = 2026)
        YearMonth = New Date(year, 1, 1)
        If categories Then
            UpdateListStatistics()
        Else
            UpdateExpenses()
        End If
    End Sub

    Friend Sub PopulateForTest(populate As Action)
        UpdateStatistics(GroupStatisticsDataGridView, populate)
    End Sub

    Friend Sub RefreshBalanceStatistics(Optional year As Integer = 2026)
        YearMonth = New Date(year, 1, 1)
        UpdateBalanceStatistics()
    End Sub
End Class

Friend Module StatisticsOverflowTests
    Public Sub Categories()
        For Each foreignKeys In New Boolean() {False, True}
            For Each mode In New String() {"floor", "half_up"}
                ' 同一月、年間、複数分類の月計・年計、負数側、途中だけ超える相殺。
                Scenario(foreignKeys, mode, True, {1500000000, 1500000000}, {1, 1}, {1, 1}, {1, 1}, True)
                Scenario(foreignKeys, mode, True, {1500000000, 1500000000}, {1, 2}, {1, 1}, {1, 1}, True)
                Scenario(foreignKeys, mode, True, {1500000000, 1500000000}, {1, 1}, {1, 2}, {1, 2}, True)
                Scenario(foreignKeys, mode, True, {1500000000, 1500000000}, {1, 2}, {1, 2}, {1, 2}, True)
                Scenario(foreignKeys, mode, True, {-1500000000, -1500000000}, {1, 1}, {1, 1}, {1, 1}, True)
                Scenario(foreignKeys, mode, True, {-1500000000, 1500000000, 1500000000}, {1, 1, 1}, {1, 1, 1}, {1, 1, 1}, True)
                Scenario(foreignKeys, mode, True, {Integer.MaxValue, Integer.MinValue}, {1, 2}, {1, 1}, {1, 1}, False)
                Scenario(foreignKeys, mode, True, {120, -20}, {1, 2}, {1, 2}, {1, 2}, False)
            Next
        Next
    End Sub

    Public Sub IncomeAndExpenses()
        For Each foreignKeys In New Boolean() {False, True}
            For Each mode In New String() {"floor", "half_up"}
                ' 支払方法内の月額・年額、支払方法をまたぐ月額・年額、収入側。
                Scenario(foreignKeys, mode, False, {1500000000, 1500000000}, {1, 1}, {1, 1}, {1, 1}, True)
                Scenario(foreignKeys, mode, False, {1500000000, 1500000000}, {1, 2}, {1, 1}, {1, 1}, True)
                Scenario(foreignKeys, mode, False, {1500000000, 1500000000}, {1, 1}, {1, 2}, {1, 1}, True)
                Scenario(foreignKeys, mode, False, {1500000000, 1500000000}, {1, 2}, {1, 2}, {1, 1}, True)
                Scenario(foreignKeys, mode, False, {-1500000000, -1500000000}, {1, 1}, {1, 1}, {1, 1}, True)
                Scenario(foreignKeys, mode, False, {-1500000000, -1500000000}, {1, 2}, {1, 2}, {1, 1}, True)
                Scenario(foreignKeys, mode, False, {1500000000, 1500000000, -1500000000}, {1, 1, 1}, {1, 1, 1}, {1, 1, 1}, True)
                Scenario(foreignKeys, mode, False, {Integer.MaxValue, Integer.MinValue}, {1, 2}, {1, 1}, {1, 1}, False)
                Scenario(foreignKeys, mode, False, {120, -20}, {1, 2}, {1, 2}, {1, 2}, False)
            Next
        Next
    End Sub

    Public Sub OtherErrorsPropagate()
        Using fixture As New DatabaseFixture
            Using form As New ViewForm(fixture.Data)
                Throws(Of InvalidOperationException)(
                    Sub() form.PopulateForTest(Sub() Throw New InvalidOperationException("unrelated failure")))
                Equal(0, form.GroupStatisticsDataGridView.Rows.Count)
                AssertSizing(form.GroupStatisticsDataGridView)
                form.RefreshStatistics(True)
                AssertNormal(form.GroupStatisticsDataGridView)
            End Using
        End Using
    End Sub

    Private Sub Scenario(foreignKeys As Boolean, mode As String, categories As Boolean,
                         amounts As Integer(), months As Integer(), payments As Integer(), groups As Integer(), overflow As Boolean)
        Using fixture As New DatabaseFixture(foreignKeys)
            fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'other','other-account',2);" &
                "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'other cash',2,2);" &
                "INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(2,'other group',2,1,0)")
            ' マスタの読込キャッシュが作られる前に、テスト用マスタを揃える。
            fixture.Data.DeleteReceipt(fixture.Data.GetReceipts(2026, 10).Single())
            For index As Integer = 0 To amounts.Length - 1
                Dim groupCode = groups(index)
                Dim paymentCode = payments(index)
                Dim detail = ReceiptFixtures.Detail(amounts(index), taxable:=False)
                detail.Users.Add(fixture.Data.UserList().Single(Function(u) u.Code = 1))
                Dim group = ReceiptFixtures.Group(10, detail)
                group.GroupClassification = fixture.Data.GroupClassificationList().Single(Function(g) g.Code = groupCode)
                Dim receipt = MakeReceipt(mode, group)
                receipt.Date = New Date(2026, months(index), index + 1)
                receipt.TaxType = 0
                receipt.PaymentClassification = fixture.Data.PaymentClassificationList().Single(Function(p) p.Code = paymentCode)
                Equal(amounts(index), receipt.Total)
                fixture.Data.RegisterReceipt(receipt)
            Next
            Dim before = ConcurrencyTests.Snapshot(fixture)
            Using form As New ViewForm(fixture.Data)
                form.Show()
                Application.DoEvents()
                Dim grid = If(categories, form.GroupStatisticsDataGridView, form.ExpensesDataGridView)
                form.RefreshStatistics(categories)
                If overflow Then
                    AssertOverflow(grid)
                    ' エラー行をダブルクリックしても詳細画面を開かない。
                    Dim handler = If(categories, "GroupStatisticsDataGridView_CellMouseDoubleClick", "ExpensesDataGridView_CellMouseDoubleClick")
                    GetType(ViewForm).GetMethod(handler, BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(
                        form, {grid, New DataGridViewCellMouseEventArgs(2, 0, 0, 0, New MouseEventArgs(MouseButtons.Left, 2, 0, 0, 0))})
                Else
                    AssertNormal(grid)
                    Dim expected As Long = amounts.Sum(Function(amount) CLng(amount))
                    Equal(CInt(expected), CInt(grid.Rows(grid.Rows.Count - 1).Cells(14).Value))
                    Equal(amounts(0), CInt(grid.Rows(0).Cells(months(0) + 1).Value))
                End If
                ' 同じ画面で別年へ移動し、0円の正常な表に戻る。
                form.RefreshStatistics(categories, 2027)
                AssertNormal(grid)
                Equal(0, CInt(grid.Rows(grid.Rows.Count - 1).Cells(14).Value))
                form.RefreshStatistics(categories)
                If overflow Then
                    AssertOverflow(grid)
                    If categories Then
                        ' 該当しない対象者への絞り込みで復帰する。
                        form.UserListBox.SelectedItem = fixture.Data.UserList().Single(Function(u) u.Code = 2)
                        form.RefreshStatistics(True)
                    Else
                        ' 収入だけ／支出だけへの変更は実際のCheckedChangedを使う。
                        If amounts(0) > 0 Then
                            form.IncomeRadioButton.Checked = True
                        Else
                            form.ExpenseRadioButton.Checked = True
                        End If
                    End If
                    AssertNormal(grid)
                    Dim recovered As Long = If(categories, 0L,
                        amounts.Where(Function(amount) If(amounts(0) > 0, amount < 0, amount > 0)).Sum(Function(amount) CLng(amount)))
                    Equal(CInt(recovered), CInt(grid.Rows(grid.Rows.Count - 1).Cells(14).Value))
                    If payments.Distinct().Count() > 1 Then
                        form.UserListBox.ClearSelected()
                        form.IncomeAndExpenseRadioButton.Checked = True
                        form.UserComboBox.SelectedValue = 1
                        form.RefreshStatistics(categories)
                        AssertNormal(grid)
                        Equal(amounts(0), CInt(grid.Rows(grid.Rows.Count - 1).Cells(14).Value))
                    End If
                End If
            End Using
            Equal(before, ConcurrencyTests.Snapshot(fixture), "Viewing and changing statistics must not write any saved data")
        End Using
    End Sub

    Private Sub AssertOverflow(grid As DataGridView)
        Equal(1, grid.Rows.Count, "No previous or partial totals may remain")
        Equal(Nothing, grid.Rows(0).Cells(0).Value)
        Equal("集計不可", CStr(grid.Rows(0).Cells(1).Value))
        For column As Integer = 2 To 14
            Equal("範囲超過", CStr(grid.Rows(0).Cells(column).Value))
            Equal(True, grid.Rows(0).Cells(column).ToolTipText.Length > 0)
        Next
        AssertSizing(grid)
    End Sub

    Private Sub AssertNormal(grid As DataGridView)
        Equal(True, grid.Rows.Count > 1)
        For Each row As DataGridViewRow In grid.Rows
            For column As Integer = 2 To 14
                Equal(True, TypeOf row.Cells(column).Value Is Integer)
            Next
        Next
        AssertSizing(grid)
    End Sub

    Private Sub AssertSizing(grid As DataGridView)
        Equal(DataGridViewAutoSizeRowsMode.DisplayedCells, grid.AutoSizeRowsMode)
        Equal(DataGridViewColumnHeadersHeightSizeMode.AutoSize, grid.ColumnHeadersHeightSizeMode)
    End Sub
End Module
