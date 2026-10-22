Imports System.Globalization
Imports System.Linq
Imports System.Reflection

' 一覧の製品partial・Designerを、一時DBで初期化する。起動・通知機能は含めない。
Partial Public Class ViewForm
    Private EventEnabled As Boolean
    Friend Property ReceiptListNotifications As Integer

    Private Sub UpdateNotify(payment As PaymentClassification)
        ReceiptListNotifications += 1
    End Sub

    Friend Sub InitReceiptList()
        EventEnabled = False
        InitList()
        Dim payments = DataAccess.PaymentClassificationList().ToList()
        payments.Insert(0, New PaymentClassification With {.Code = 0, .Name = ""})
        PaymentComboBox.DataSource = payments
        PaymentComboBox.SelectedIndex = 0
        RefreshReceiptList(New Date(2026, 10, 1))
        EventEnabled = True
    End Sub

    Friend Sub RefreshReceiptList(month As Date)
        YearMonth = month
        UpdateList()
    End Sub

    Friend Sub FilterReceiptOwner(code As Integer)
        UserComboBox.SelectedValue = code
        PaymentComboBox.SelectedIndex = 0
        UpdateListFilter()
    End Sub

    Friend Sub SelectReceiptForTest(seq As Integer)
        SelectReceiptRowBySeq(seq)
    End Sub

    Friend Function ReceiptModelForTest(seq As Integer) As Receipt
        Return AccountList.Single(Function(r) r.Seq = seq)
    End Function

    Friend Function ReceiptAmountForTest(row As DataGridViewRow) As Object
        Return DirectCast(row.DataBoundItem, ReceiptListRow).Total
    End Function
End Class

Friend Module ReceiptListOverflowTests
    Public Sub LegacyBindingAndOtherErrors()
        For Each foreignKeys In New Boolean() {False, True}
            Using fixture = Seed(foreignKeys, "floor")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Dim source = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 2)
                        Throws(Of OverflowException)(Sub() source.GetTotal(source.TaxType, source.TaxRoundingMode))
                        Using host As New Form(), legacy As New DataGridView With {.AutoGenerateColumns = False, .Dock = DockStyle.Fill, .AllowUserToAddRows = False}
                            legacy.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "Seq"})
                            legacy.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "Total"})
                            Dim errors As New List(Of Exception)
                            WatchErrors(legacy, errors)
                            host.Controls.Add(legacy)
                            WithoutDialogs(
                                Sub()
                                    legacy.DataSource = New List(Of Receipt) From {source}
                                    Program.OpenForm(host)
                                    Try
                                        Dim value = legacy.Rows(0).Cells(1).FormattedValue
                                    Catch ex As Exception
                                        errors.Add(ex)
                                    End Try
                                End Sub)
                            Equal(True, errors.Any(Function(ex) ContainsOverflow(ex)), "Direct Total binding must expose the original overflow")
                            Console.WriteLine("INFO direct receipt Total binding reproduced: " & String.Join(", ", errors.Select(Function(ex) ex.GetType().Name)))
                        End Using
                        Using form As New ViewForm(fixture.Data)
                            WithoutDialogs(Sub() Program.OpenForm(form))
                            WithoutDialogs(Sub() form.InitReceiptList())
                            Dim row = RowFor(form, 1)
                            Dim model = form.ReceiptModelForTest(1)
                            Dim groups = model.Groups
                            Try
                                model.Groups = Nothing
                                Throws(Of ArgumentNullException)(Sub() form.ReceiptAmountForTest(row))
                            Finally
                                model.Groups = groups
                            End Try
                            Equal(41, CInt(row.Cells(form.ReceiptTotalColumn.Index).Value))
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub MixedRowsAndCurrentValues()
        EachMode(
            Sub(fixture, mode)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New ViewForm(fixture.Data)
                            Dim errors As New List(Of Exception)
                            WatchErrors(form.HouseholdAccountDataGridView, errors)
                            WithoutDialogs(Sub() Program.OpenForm(form))
                            WithoutDialogs(Sub() form.InitReceiptList())
                            Equal(8, form.HouseholdAccountDataGridView.Rows.Count)
                            For Each seq In New Integer() {2, 3, 4}
                                AssertAmount(form, seq, Nothing)
                                Equal(Color.MistyRose, RowFor(form, seq).DefaultCellStyle.BackColor)
                                Throws(Of OverflowException)(Sub() form.ReceiptModelForTest(seq).GetTotal(form.ReceiptModelForTest(seq).TaxType, mode))
                            Next
                            Dim previousCulture = CultureInfo.CurrentCulture
                            Try
                                For Each culture As String In New String() {"en-US", "de-DE"}
                                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture)
                                    AssertAmount(form, 1, If(mode = "floor", 41, 42))
                                    AssertAmount(form, 5, Integer.MaxValue)
                                    AssertAmount(form, 6, Integer.MinValue)
                                    AssertAmount(form, 7, 0)
                                    AssertAmount(form, 8, 1234)
                                Next
                            Finally
                                CultureInfo.CurrentCulture = previousCulture
                            End Try
                            Dim normal = RowFor(form, 1)
                            Equal("history shop", CStr(normal.Cells(form.ReceiptShopNameColumn.Index).Value))
                            Equal("", CStr(normal.Cells(form.ReceiptShopLocationColumn.Index).Value))
                            Equal("", CStr(normal.Cells(form.ReceiptTagColumn.Index).Value))
                            Equal(New Date(2026, 10, 16, 12, 0, 0), DirectCast(normal.Cells(form.ReceiptDateColumn.Index).Value, Date))
                            Equal(True, Object.ReferenceEquals(form.ReceiptModelForTest(1).PaymentClassification, normal.Cells(form.PaymentClassificationColumn.Index).Value))
                            Equal(False, RowFor(form, 8).DefaultCellStyle.BackColor = Color.MistyRose)
                            Equal(1, form.TransferDataGridView.Rows.Count)
                            Equal(True, TypeOf form.TransferDataGridView.Rows(0).DataBoundItem Is Transfer)
                            Equal(55, DirectCast(form.TransferDataGridView.Rows(0).DataBoundItem, Transfer).Fee)
                            ' 同じReceiptが変更された場合も、一覧の金額は古い値をキャッシュしない。
                            Dim changing = form.ReceiptModelForTest(2).Groups(0).Details(0)
                            changing.Quantity = 1
                            AssertAmount(form, 2, 2000000000)
                            changing.Quantity = 2
                            AssertAmount(form, 2, Nothing)
                            form.SelectReceiptForTest(8)
                            Dim clipboard = form.HouseholdAccountDataGridView.GetClipboardContent()
                            Equal(True, CStr(clipboard.GetData(DataFormats.UnicodeText)).Contains(CInt(1234).ToString("N0", CultureInfo.CurrentCulture)))
                            Equal(0, errors.Count, String.Join(";", errors.Select(Function(ex) ex.ToString())))
                            AssertSizing(form)
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Public Sub FiltersAndEditCancellation()
        EachMode(
            Sub(fixture, mode)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New ViewForm(fixture.Data)
                            Dim errors As New List(Of Exception)
                            WatchErrors(form.HouseholdAccountDataGridView, errors)
                            WithoutDialogs(Sub() Program.OpenForm(form))
                            WithoutDialogs(Sub() form.InitReceiptList())
                            form.SelectReceiptForTest(2)
                            form.RefreshReceiptList(New Date(2026, 10, 1))
                            Equal(2, CInt(form.HouseholdAccountDataGridView.SelectedRows(0).Cells(form.ReceiptSeqColumn.Index).Value))
                            ' 支払方法は製品のSelectedIndexChangedを通す。利用者は既存の絞り込み処理を呼ぶ。
                            WithoutDialogs(Sub() form.PaymentComboBox.SelectedValue = 2)
                            Equal(3, form.HouseholdAccountDataGridView.Rows.Count)
                            AssertAmount(form, 5, Integer.MaxValue)
                            WithoutDialogs(Sub() form.FilterReceiptOwner(2))
                            Equal(1, form.HouseholdAccountDataGridView.Rows.Count)
                            AssertAmount(form, 8, 1234)
                            Equal(0, form.TransferDataGridView.Rows.Count)
                            WithoutDialogs(Sub() form.FilterReceiptOwner(0))
                            form.RefreshReceiptList(New Date(2026, 11, 1))
                            Equal(0, form.HouseholdAccountDataGridView.Rows.Count)
                            form.RefreshReceiptList(New Date(2026, 10, 1))
                            Equal(8, form.HouseholdAccountDataGridView.Rows.Count)
                            Dim source = form.ReceiptModelForTest(2)
                            Dim token = source.RevisionToken
                            form.SelectReceiptForTest(2)
                            For Each doubleClick In New Boolean() {False, True}
                                Edit(form, doubleClick,
                                    Sub(editor)
                                        Equal(True, Object.ReferenceEquals(source, editor.Receipt))
                                        Equal("範囲超過", editor.TotalTextBox.Text)
                                        editor.ShopNameComboBox.Text = "pending editor shop"
                                        editor.TaxExclusiveRadioButton.Checked = True
                                        editor.TaxRoundingHalfUpRadioButton.Checked = True
                                        editor.Close()
                                    End Sub)
                                Equal(2, source.Seq)
                                Equal(token, source.RevisionToken)
                                Equal("shop 2", source.ShopName)
                                Equal(0, source.TaxType)
                                Equal(mode, source.TaxRoundingMode)
                                AssertAmount(form, 2, Nothing)
                            Next
                            Equal(0, form.ReceiptListNotifications)
                            Equal(0, errors.Count)
                            AssertSizing(form)
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Public Sub CorrectAndSave()
        EachMode(
            Sub(fixture, mode)
                Dim before = ConcurrencyTests.Snapshot(fixture, EntityKind.Receipt, 2)
                Dim originalToken = fixture.Scalar("SELECT revision_token FROM t_receipts WHERE seq=2")
                Using form As New ViewForm(fixture.Data)
                    Dim errors As New List(Of Exception)
                    WatchErrors(form.HouseholdAccountDataGridView, errors)
                    WithoutDialogs(Sub() Program.OpenForm(form))
                    WithoutDialogs(Sub() form.InitReceiptList())
                    Dim source = form.ReceiptModelForTest(2)
                    form.SelectReceiptForTest(2)
                    Edit(form, False,
                        Sub(editor)
                            Equal(True, Object.ReferenceEquals(source, editor.Receipt))
                            DetailInputTests.SelectDetail(editor, True)
                            editor.QuantityTextBox.Text = "1"
                            editor.AddDetailButton.PerformClick()
                            Equal("2000000000", editor.TotalTextBox.Text)
                            Program.ClickAndAnswer(editor.RegiserButton, DialogResult.Yes, "Save corrected receipt from list")
                        End Sub)
                    Equal(1, form.ReceiptListNotifications)
                    Equal(8, form.HouseholdAccountDataGridView.Rows.Count)
                    AssertAmount(form, 2, 2000000000)
                    AssertAmount(form, 3, Nothing)
                    Equal(2, CInt(form.HouseholdAccountDataGridView.SelectedRows(0).Cells(form.ReceiptSeqColumn.Index).Value))
                    Dim loaded = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 2)
                    Equal(1, loaded.Groups(0).Details(0).Quantity)
                    Equal(2000000000, loaded.Total)
                    Equal(2000000000L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=2")))
                    Equal(False, Object.Equals(originalToken, fixture.Scalar("SELECT revision_token FROM t_receipts WHERE seq=2")))
                    Equal(before, ConcurrencyTests.Snapshot(fixture, EntityKind.Receipt, 2))
                    Equal(0, errors.Count)
                    AssertSizing(form)
                End Using
            End Sub)
    End Sub

    Private Function Seed(foreignKeys As Boolean, mode As String) As DatabaseFixture
        Dim fixture As New DatabaseFixture(foreignKeys)
        Try
            fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'other','other-account',2);" &
                "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'bank',2,1),(3,'other cash',3,2);" &
                "UPDATE t_receipts SET shop_location=NULL,tag=NULL,tax_rounding_mode='" & mode & "' WHERE seq=1;")
            For seq As Integer = 2 To 8
                fixture.AddReceipt(seq, "shop " & seq.ToString(CultureInfo.InvariantCulture), mode, New Date(2026, 10, 16, 12, 0, seq), If(seq >= 5, If(seq = 8, 3, 2), 1))
            Next
            fixture.Execute("UPDATE t_receipts SET tax_type=0 WHERE seq BETWEEN 2 AND 8 AND seq<>4;" &
                "UPDATE t_receipt_details SET tax_code=0 WHERE receipt_seq BETWEEN 2 AND 8 AND receipt_seq<>4;" &
                "UPDATE t_receipt_details SET fee=2000000000,quantity=2 WHERE receipt_seq=2;" &
                "UPDATE t_receipt_details SET fee=-2000000000,quantity=2 WHERE receipt_seq=3;" &
                "UPDATE t_receipt_details SET fee=2000000000 WHERE receipt_seq=4;" &
                "UPDATE t_receipt_details SET fee=2147483647 WHERE receipt_seq=5;" &
                "UPDATE t_receipt_details SET fee=-2147483648 WHERE receipt_seq=6;" &
                "UPDATE t_receipt_details SET fee=1234 WHERE receipt_seq=8;" &
                "INSERT INTO t_receipt_detail_users(receipt_seq,group_seq,detail_seq,user_code) VALUES(8,1,1,2);")
            fixture.Data.RegisterTransfer(New Transfer With {.Date = New Date(2026, 10, 16, 12, 0, 0),
                .Name = "ordinary transfer", .Fee = 55, .Tag = "", .FromPaymentClassification = fixture.Data.PaymentClassification(1),
                .ToPaymentClassification = fixture.Data.PaymentClassification(2)})
            Return fixture
        Catch
            fixture.Dispose()
            Throw
        End Try
    End Function

    Private Sub EachMode(action As Action(Of DatabaseFixture, String))
        For Each foreignKeys In New Boolean() {False, True}
            For Each mode In New String() {"floor", "half_up"}
                Using fixture = Seed(foreignKeys, mode)
                    action(fixture, mode)
                End Using
            Next
        Next
    End Sub

    Private Function RowFor(form As ViewForm, seq As Integer) As DataGridViewRow
        Return form.HouseholdAccountDataGridView.Rows.Cast(Of DataGridViewRow)().Single(
            Function(row) CInt(row.Cells(form.ReceiptSeqColumn.Index).Value) = seq)
    End Function

    Private Sub AssertAmount(form As ViewForm, seq As Integer, expected As Integer?)
        Dim cell = RowFor(form, seq).Cells(form.ReceiptTotalColumn.Index)
        If expected.HasValue Then
            Equal(True, TypeOf cell.Value Is Integer)
            Equal(expected.Value, CInt(cell.Value))
            Equal(expected.Value.ToString("N0", CultureInfo.CurrentCulture), CStr(cell.FormattedValue))
            Equal("", cell.ToolTipText)
        Else
            Equal("範囲超過", CStr(cell.Value))
            Equal("範囲超過", CStr(cell.FormattedValue))
            Equal(True, cell.ToolTipText.Length > 0)
        End If
    End Sub

    Private Sub WatchErrors(grid As DataGridView, errors As List(Of Exception))
        AddHandler grid.DataError,
            Sub(sender, e)
                errors.Add(e.Exception)
                e.ThrowException = False
                e.Cancel = True
            End Sub
    End Sub

    Private Function ContainsOverflow(ex As Exception) As Boolean
        While ex IsNot Nothing
            If TypeOf ex Is OverflowException Then Return True
            ex = ex.InnerException
        End While
        Return False
    End Function

    Private Sub AssertSizing(form As ViewForm)
        Equal(DataGridViewAutoSizeRowsMode.DisplayedCells, form.HouseholdAccountDataGridView.AutoSizeRowsMode)
        Equal(DataGridViewColumnHeadersHeightSizeMode.AutoSize, form.HouseholdAccountDataGridView.ColumnHeadersHeightSizeMode)
        Equal(DataGridViewAutoSizeRowsMode.DisplayedCells, form.TransferDataGridView.AutoSizeRowsMode)
        Equal(DataGridViewColumnHeadersHeightSizeMode.AutoSize, form.TransferDataGridView.ColumnHeadersHeightSizeMode)
    End Sub

    Private Sub Edit(form As ViewForm, doubleClick As Boolean, action As Action(Of RegisterForm))
        Dim failure As Exception = Nothing
        Dim opened As Boolean
        Using responder As New System.Windows.Forms.Timer With {.Interval = 50}
            AddHandler responder.Tick,
                Sub(sender, e)
                    Dim editor = Application.OpenForms.OfType(Of RegisterForm)().FirstOrDefault()
                    If editor Is Nothing Then Return
                    responder.Stop()
                    opened = True
                    Try
                        action(editor)
                    Catch ex As Exception
                        failure = ex
                    Finally
                        If Not editor.IsDisposed Then editor.Close()
                    End Try
                End Sub
            responder.Start()
            WithoutDialogs(
                Sub()
                    If doubleClick Then
                        GetType(ViewForm).GetMethod("HouseholdAccountDataGridView_CellDoubleClick", BindingFlags.Instance Or BindingFlags.NonPublic).
                            Invoke(form, New Object() {form.HouseholdAccountDataGridView, New DataGridViewCellEventArgs(form.ReceiptTotalColumn.Index, form.HouseholdAccountDataGridView.SelectedRows(0).Index)})
                    Else
                        form.ModifyButton.PerformClick()
                    End If
                End Sub)
        End Using
        If failure IsNot Nothing Then Throw failure
        Equal(True, opened, "The original edit route must open the receipt")
        Equal(True, form.Visible)
    End Sub

    Private Sub WithoutDialogs(action As Action)
        Using guard As New System.Windows.Forms.Timer With {.Interval = 15000}
            AddHandler guard.Tick, Sub(sender, e) Environment.FailFast("Unexpected dialog blocked receipt list rendering")
            guard.Start()
            Try
                action()
            Finally
                guard.Stop()
            End Try
        End Using
    End Sub
End Module
