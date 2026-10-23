Imports System.Globalization
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.ExceptionServices

Friend Module DetailViewOverflowTests
    Public Sub GroupOverflowAndRecovery()
        EachScenario(0, True)
    End Sub

    Public Sub DetailOverflowAndRecovery()
        EachScenario(1, False)
    End Sub

    Private Sub EachScenario(kind As Integer, normalDetails As Boolean)
        For Each foreignKeys In New Boolean() {False, True}
            For Each mode In New String() {"floor", "half_up"}
                For Each sign As Integer In New Integer() {-1, 1}
                    For Each viewMode As DetailViewFormMode In New DetailViewFormMode() {DetailViewFormMode.Group, DetailViewFormMode.Payment}
                        Using fixture = Seed(foreignKeys, mode, kind, sign)
                            BalanceBoundaryTests.ReadOnlyCase(fixture,
                                Sub()
                                    Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                                    If kind = 0 Then
                                        Throws(Of OverflowException)(Sub() Calculation.GroupCalculatedFee(receipt, receipt.Groups(1)))
                                    Else
                                        Throws(Of OverflowException)(Sub() Calculation.DetailCalculatedFee(receipt, receipt.Groups(1), receipt.Groups(1).Details(0)))
                                    End If
                                    Using form = Editor(fixture, viewMode)
                                        WithoutDialogs(Sub() form.DetailCheckBox.Checked = normalDetails)
                                        WithoutDialogs(Sub() Program.OpenForm(form))
                                        AssertScenarioRows(form, kind, sign)
                                        For repeat As Integer = 0 To 1
                                            form.FeeColumn.HeaderCell.SortGlyphDirection = SortOrder.Ascending
                                            ' 実際のCheckedChangedで、正常な前回表示と途中行を消す。
                                            WithoutDialogs(Sub() form.DetailCheckBox.Checked = Not normalDetails)
                                            AssertOverflow(form)
                                            form.Code = -1
                                            Refresh(form)
                                            AssertOverflow(form)
                                            form.Code = 2
                                            Refresh(form)
                                            AssertRows(form)
                                            form.Code = If(viewMode = DetailViewFormMode.Group, -1, 1)
                                            form.Month = 11
                                            Refresh(form)
                                            AssertRows(form)
                                            form.Month = 0
                                            Refresh(form)
                                            AssertOverflow(form)
                                            form.Month = 10
                                            WithoutDialogs(Sub() form.DetailCheckBox.Checked = normalDetails)
                                            AssertScenarioRows(form, kind, sign)
                                        Next
                                    End Using
                                End Sub)
                        End Using
                    Next
                Next
            Next
        Next
    End Sub

    Public Sub TaxRangesAndNormalValues()
        For Each foreignKeys In New Boolean() {False, True}
            For Each mode In New String() {"floor", "half_up"}
                For Each sign As Integer In New Integer() {-1, 1}
                    Using fixture = Seed(foreignKeys, mode, 2, sign)
                        BalanceBoundaryTests.ReadOnlyCase(fixture,
                            Sub()
                                Using form = Editor(fixture, DetailViewFormMode.Group)
                                    ' Shownから始まる表示でも、税による超過を通知ダイアログなしで扱う。
                                    WithoutDialogs(Sub() Program.OpenForm(form))
                                    AssertOverflow(form)
                                    WithoutDialogs(Sub() form.DetailCheckBox.Checked = True)
                                    AssertOverflow(form)
                                    form.Year = 2027
                                    WithoutDialogs(Sub() form.DetailCheckBox.Checked = False)
                                    AssertRows(form)
                                    form.Year = 2026
                                    WithoutDialogs(Sub() form.DetailCheckBox.Checked = True)
                                    AssertOverflow(form)
                                End Using
                            End Sub)
                    End Using
                Next
                For Each kind As Integer In New Integer() {3, 4}
                    Using fixture = Seed(foreignKeys, mode, kind, 1)
                        BalanceBoundaryTests.ReadOnlyCase(fixture,
                            Sub()
                                Using form = Editor(fixture, DetailViewFormMode.Payment)
                                    WithoutDialogs(Sub() Program.OpenForm(form))
                                    Dim expected = If(kind = 3, New Integer() {Integer.MaxValue, Integer.MinValue},
                                        New Integer() {21, If(mode = "floor", 20, 21)})
                                    AssertRows(form, expected)
                                    WithoutDialogs(Sub() form.DetailCheckBox.Checked = True)
                                    AssertRows(form, expected)
                                    Equal("2026/10/16 12:00:00", CStr(form.GroupDetailGridView.Rows(0).Cells("DateTimeColumn").Value))
                                    Equal("history shop", CStr(form.GroupDetailGridView.Rows(0).Cells("ShopColumn").Value))
                                    Equal("original location", CStr(form.GroupDetailGridView.Rows(0).Cells("LocationColumn").Value))
                                    Equal("original tag", CStr(form.GroupDetailGridView.Rows(0).Cells("TagColumn").Value))
                                    Equal("cash", CStr(form.GroupDetailGridView.Rows(0).Cells("PaymentColumn").Value))
                                    Equal(1, CInt(form.GroupDetailGridView.Rows(0).Cells("QuantityColumn").Value))
                                    If kind = 3 Then
                                        Equal(New String("x"c, 600), CStr(form.GroupDetailGridView.Rows(0).Cells("ItemColumn").Value))
                                        Equal(500, form.ItemColumn.Width)
                                    End If
                                    form.Code = -1
                                    Refresh(form)
                                    AssertRows(form, expected)
                                    form.Code = 2
                                    Refresh(form)
                                    AssertRows(form)
                                End Using
                            End Sub)
                    End Using
                Next
            Next
        Next
    End Sub

    Public Sub OtherErrorsPropagate()
        For Each foreignKeys In New Boolean() {False, True}
            Using fixture = Seed(foreignKeys, "floor", 4, 1)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form = Editor(fixture, DetailViewFormMode.Group)
                            WithoutDialogs(Sub() Program.OpenForm(form))
                            AssertRows(form, 21, 20)
                            form.Month = 13
                            Throws(Of ArgumentOutOfRangeException)(Sub() Refresh(form))
                            Equal(0, form.GroupDetailGridView.Rows.Count)
                            AssertSizing(form)
                            form.Month = 10
                            Refresh(form)
                            AssertRows(form, 21, 20)
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Private Function Seed(foreignKeys As Boolean, mode As String, kind As Integer, sign As Integer) As DatabaseFixture
        Dim fixture As New DatabaseFixture(foreignKeys)
        Try
            fixture.Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'other cash',2,1);" &
                "INSERT INTO m_group_classification(code,name,seq,default_tax_category_code) VALUES(2,'other group',2,1);" &
                "UPDATE t_receipts SET tax_type=0,total=19,shop_location='original location',tag='original tag',tax_rounding_mode='" & mode & "';" &
                "UPDATE t_receipt_details SET tax_code=0;")
            ' 現在の入力防御を迂回し、既存DBにあり得る範囲外の明細を読ませる。
            If kind < 2 Then
                Dim amount = (sign * 1500000000).ToString(CultureInfo.InvariantCulture)
                Dim opposite = If(kind = 0, amount, (-sign * 1500000000).ToString(CultureInfo.InvariantCulture))
                Dim quantity = If(kind = 0, "1", "2")
                fixture.Execute("UPDATE t_receipt_details SET fee=" & amount & ",quantity=" & quantity & " WHERE group_seq=2;" &
                    "INSERT INTO t_receipt_details(receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_code,tag) " &
                    "VALUES(1,2,2,'second item'," & opposite & "," & quantity & ",0,0,0,'');")
            ElseIf kind = 2 Then
                fixture.Execute("UPDATE t_receipts SET tax_type=1;UPDATE t_receipt_details SET tax_code=5;" &
                    "UPDATE t_receipt_details SET fee=" & (sign * 2000000000).ToString(CultureInfo.InvariantCulture) & " WHERE group_seq=2;")
            ElseIf kind = 3 Then
                fixture.Execute("UPDATE t_receipt_details SET fee=2147483647,name='" & New String("x"c, 600) & "' WHERE group_seq=1;" &
                    "UPDATE t_receipt_details SET fee=-2147483648 WHERE group_seq=2;")
            Else
                fixture.Execute("UPDATE t_receipts SET tax_type=1;UPDATE t_receipt_details SET tax_code=5;")
            End If
            Return fixture
        Catch
            fixture.Dispose()
            Throw
        End Try
    End Function

    ' 分類別では全分類を表示し、支払方法別では口座1の明細を表示する。
    Private Function Editor(fixture As DatabaseFixture, mode As DetailViewFormMode) As DetailViewForm
        Return New DetailViewForm With {.DataAccess = fixture.Data, .Year = 2026, .Month = 10, .Code = If(mode = DetailViewFormMode.Group, -1, 1), .Mode = mode}
    End Function

    Private Sub Refresh(form As DetailViewForm)
        Try
            GetType(DetailViewForm).GetMethod("ShowGroupStatistics", BindingFlags.Instance Or BindingFlags.NonPublic).
                Invoke(form, New Object() {form.Year, form.Month, form.Code})
        Catch ex As TargetInvocationException When ex.InnerException IsNot Nothing
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw()
        End Try
    End Sub

    Private Sub AssertScenarioRows(form As DetailViewForm, kind As Integer, sign As Integer)
        If kind = 0 Then
            AssertRows(form, 19, sign * 1500000000, sign * 1500000000)
        Else
            AssertRows(form, 19, 0)
        End If
    End Sub

    Private Sub AssertRows(form As DetailViewForm, ParamArray expected As Integer())
        Dim grid = form.GroupDetailGridView
        Equal(expected.Length, grid.Rows.Count)
        For index As Integer = 0 To expected.Length - 1
            Equal(1, CInt(grid.Rows(index).Cells("SeqColumn").Value))
            Equal(True, TypeOf grid.Rows(index).Cells("FeeColumn").Value Is Integer)
            Equal(expected(index), CInt(grid.Rows(index).Cells("FeeColumn").Value))
        Next
        AssertSizing(form)
    End Sub

    Private Sub AssertOverflow(form As DetailViewForm)
        Dim grid = form.GroupDetailGridView
        Equal(1, grid.Rows.Count, "Previous results and partially rendered detail rows must be cleared")
        For Each cell As DataGridViewCell In grid.Rows(0).Cells
            Equal(True, cell.ToolTipText.Length > 0)
            If cell.OwningColumn.Name = "DateTimeColumn" Then
                Equal("集計不可", CStr(cell.Value))
            ElseIf cell.OwningColumn.Name = "FeeColumn" Then
                Equal("範囲超過", CStr(cell.Value))
            Else
                Equal(Nothing, cell.Value)
            End If
        Next
        AssertSizing(form)
    End Sub

    Private Sub AssertSizing(form As DetailViewForm)
        Dim grid = form.GroupDetailGridView
        Equal(DataGridViewAutoSizeRowsMode.DisplayedCells, grid.AutoSizeRowsMode)
        Equal(DataGridViewAutoSizeColumnsMode.AllCells, grid.AutoSizeColumnsMode)
        Equal(DataGridViewColumnHeadersHeightSizeMode.AutoSize, grid.ColumnHeadersHeightSizeMode)
        Equal(form.Code < 0, form.GroupColumn.Visible)
        Equal(form.DetailCheckBox.Checked, form.ItemColumn.Visible)
        Equal(form.DetailCheckBox.Checked, form.QuantityColumn.Visible)
        For Each column As DataGridViewColumn In grid.Columns
            Equal(SortOrder.None, column.HeaderCell.SortGlyphDirection)
        Next
    End Sub

    Private Sub WithoutDialogs(action As Action)
        Using guard As New System.Windows.Forms.Timer With {.Interval = 15000}
            AddHandler guard.Tick, Sub(sender, args) Environment.FailFast("Unexpected dialog blocked detail rendering")
            guard.Start()
            Try
                action()
            Finally
                guard.Stop()
            End Try
        End Using
    End Sub
End Module
