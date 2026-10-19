Imports System.Globalization
Imports System.Windows.Forms
Imports ConcurrencyTests

Friend Module RevisionUiTests
    Public Sub CurrentOperationsClose()
        EachCase(
            Sub(fixture, second, kind)
                Dim previousData = Calculation.DataAccess
                Try
                    Calculation.DataAccess = fixture.Data
                    For Each deleting In New Boolean() {False, True}
                        Dim item = LoadItem(fixture.Data, kind)
                        Dim originalToken = RevisionTests.Token(kind, item)
                        Dim unrelated = Snapshot(fixture, kind, 1)
                        Using form = CreateEditor(fixture.Data, kind, item)
                            Dim closed As Boolean
                            AddHandler form.FormClosed, Sub(sender, args) closed = True
                            Program.OpenForm(form)
                            SetPendingInput(form, kind)
                            Equal(False, ReloadRequired(form, kind))
                            Program.ClickAndAnswer(ActionButton(form, kind, deleting), DialogResult.Yes,
                                                   kind.ToString() & If(deleting, " current delete", " current update"))
                            Equal(True, closed, "Successful confirmed operation must close the editor")
                            Equal(DialogResult.OK, form.DialogResult)
                            Equal(False, ReloadRequired(form, kind), "A normal save must not request a conflict refresh")
                        End Using
                        If deleting Then
                            Equal(0L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM " & ParentTable(kind) & " WHERE seq=1")))
                        Else
                            Equal(False, originalToken = RevisionTests.Token(kind, item), "The successful UI save must publish the committed token")
                            Equal("pending user 'quote'", Convert.ToString(fixture.Scalar("SELECT tag FROM " & ParentTable(kind) & " WHERE seq=1")))
                            AssertSaved(fixture, kind, item)
                        End If
                        Equal(unrelated, Snapshot(fixture, kind, 1), "A normal UI operation must preserve every unrelated row")
                        AssertForeignKeys(fixture.ActualDataConnection)
                    Next
                Finally
                    Calculation.DataAccess = previousData
                End Try
            End Sub)
    End Sub

    Public Sub ConflictsKeepInput()
        EachCase(
            Sub(fixture, second, kind)
                Dim previousData = Calculation.DataAccess
                Try
                    Calculation.DataAccess = fixture.Data
                    For Each deleting In New Boolean() {False, True}
                        Dim item = LoadItem(fixture.Data, kind)
                        Dim originalToken = RevisionTests.Token(kind, item)
                        Using form = CreateEditor(fixture.Data, kind, item)
                            Dim closed As Boolean
                            AddHandler form.FormClosed, Sub(sender, args) closed = True
                            Program.OpenForm(form)
                            SetPendingInput(form, kind)
                            Dim pending = InputSnapshot(form, kind)
                            Dim firstWriter = MakeItem(kind, "first UI writer", 2, LoadItem(second, kind))
                            Save(second, kind, firstWriter, registering:=False)
                            Dim before = Snapshot(fixture)
                            Equal(False, ReloadRequired(form, kind))
                            Program.ClickAndAnswer(ActionButton(form, kind, deleting), DialogResult.Yes,
                                                   kind.ToString() & If(deleting, " stale delete", " stale update"), expectedConflict:=True)
                            Equal(False, closed, "A conflict must keep the editor open")
                            Equal(True, form.Visible)
                            Equal(DialogResult.None, form.DialogResult, "A rejected operation must not report success to its parent")
                            Equal(True, ReloadRequired(form, kind), "The parent must refresh its list even if the user later cancels")
                            Equal(pending, InputSnapshot(form, kind), "Conflict warning must preserve every pending input field")
                            Equal(originalToken, RevisionTests.Token(kind, item), "Rejected form save must retain its stale token")
                            Equal(before, Snapshot(fixture), "A rejected UI operation must preserve the first writer and all saved data")
                            AssertSaved(fixture, kind, firstWriter)
                            form.Close()
                            Equal(True, closed)
                            Equal(True, ReloadRequired(form, kind), "Closing the failed form must retain the parent's refresh signal")
                            Equal(before, Snapshot(fixture), "Closing after conflict must not save pending input")
                        End Using
                    Next
                    AssertForeignKeys(fixture.ActualDataConnection)
                Finally
                    Calculation.DataAccess = previousData
                End Try
            End Sub)
    End Sub

    Private Function CreateEditor(data As DataAccess, kind As EntityKind, item As Object) As Form
        Select Case kind
            Case EntityKind.Receipt : Return New RegisterForm With {.DataAccess = data, .Receipt = DirectCast(item, Receipt)}
            Case EntityKind.Transfer : Return New RegisterTransferForm With {.DataAccess = data, .Transfer = DirectCast(item, Transfer)}
            Case EntityKind.Balance : Return New RegisterBalanceForm With {.DataAccess = data, .Balance = DirectCast(item, Balance)}
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Sub SetPendingInput(form As Form, kind As EntityKind)
        Select Case kind
            Case EntityKind.Receipt
                Dim editor = DirectCast(form, RegisterForm)
                editor.ReceiptTagTextBox.Text = "pending user 'quote'"
                editor.ShopLocationComboBox.Text = "pending location"
                editor.ReceiptTimeTextBox.Value = editor.ReceiptDateTextBox.Value.Date.AddHours(12)
                editor.TaxRoundingHalfUpRadioButton.PerformClick()
            Case EntityKind.Transfer
                Dim editor = DirectCast(form, RegisterTransferForm)
                editor.TransferTagTextBox.Text = "pending user 'quote'"
                editor.NameComboBox.Text = "pending transfer"
                editor.FeeTextBox.Text = "321"
                editor.TransferTimeTextBox.Value = editor.TransferDateTextBox.Value.Date.AddHours(12)
            Case EntityKind.Balance
                Dim editor = DirectCast(form, RegisterBalanceForm)
                editor.BalanceTagTextBox.Text = "pending user 'quote'"
                Dim row = editor.BalanceDataGridView.Rows(0)
                row.Cells(editor.BalanceColumn.Index).Value = 1234
                row.Cells(editor.ValuationAmountColumn.Index).Value = 9876
                row.Cells(editor.TagColumn.Index).Value = "pending group"
        End Select
    End Sub

    Private Function InputSnapshot(form As Form, kind As EntityKind) As String
        ' 日付欄の隠れた時刻と時刻欄の隠れた日付は、入力確定時に正規化される。
        ' 利用者が入力する日付・時刻をそれぞれ比較する。
        Select Case kind
            Case EntityKind.Receipt
                Dim editor = DirectCast(form, RegisterForm)
                Return String.Join("|", editor.ReceiptTagTextBox.Text, editor.ShopNameComboBox.Text,
                    editor.ShopLocationComboBox.Text, editor.ReceiptDateTextBox.Value.Date.ToString("O"),
                    editor.ReceiptTimeTextBox.Value.TimeOfDay.ToString(), editor.TaxExclusiveRadioButton.Checked.ToString(),
                    editor.TaxRoundingHalfUpRadioButton.Checked.ToString(), editor.TotalTextBox.Text)
            Case EntityKind.Transfer
                Dim editor = DirectCast(form, RegisterTransferForm)
                Return String.Join("|", editor.TransferTagTextBox.Text, editor.NameComboBox.Text,
                    editor.FeeTextBox.Text, editor.TransferDateTextBox.Value.Date.ToString("O"), editor.TransferTimeTextBox.Value.TimeOfDay.ToString(),
                    Convert.ToString(editor.FromPaymentClassificationComboBox.SelectedValue, CultureInfo.InvariantCulture),
                    Convert.ToString(editor.ToPaymentClassificationComboBox.SelectedValue, CultureInfo.InvariantCulture))
            Case EntityKind.Balance
                Dim editor = DirectCast(form, RegisterBalanceForm)
                Dim result = editor.BalanceTagTextBox.Text & "|" & editor.BalanceDateTextBox.Value.Date.ToString("O")
                For Each row As DataGridViewRow In editor.BalanceDataGridView.Rows
                    For Each index In New Integer() {editor.BalanceColumn.Index, editor.ValuationAmountColumn.Index, editor.TagColumn.Index}
                        result &= "|" & Convert.ToString(row.Cells(index).Value, CultureInfo.InvariantCulture)
                    Next
                Next
                Return result
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Function ActionButton(form As Form, kind As EntityKind, deleting As Boolean) As Button
        Select Case kind
            Case EntityKind.Receipt
                Dim editor = DirectCast(form, RegisterForm)
                Return If(deleting, editor.DeleteButton, editor.RegiserButton)
            Case EntityKind.Transfer
                Dim editor = DirectCast(form, RegisterTransferForm)
                Return If(deleting, editor.DeleteButton, editor.RegiserButton)
            Case EntityKind.Balance
                Dim editor = DirectCast(form, RegisterBalanceForm)
                Return If(deleting, editor.DeleteButton, editor.RegiserButton)
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    Private Function ReloadRequired(form As Form, kind As EntityKind) As Boolean
        Select Case kind
            Case EntityKind.Receipt : Return DirectCast(form, RegisterForm).ReloadRequired
            Case EntityKind.Transfer : Return DirectCast(form, RegisterTransferForm).ReloadRequired
            Case EntityKind.Balance : Return DirectCast(form, RegisterBalanceForm).ReloadRequired
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function
End Module
