Imports System.Data.SQLite
Imports System.Globalization
Imports System.Linq

Friend Module TransferInputTests
    Public Sub InvalidInputsAndRecovery()
        For Each foreignKeys In New Boolean() {False, True}
            For Each editing In New Boolean() {False, True}
                Using fixture = Seed(foreignKeys)
                    Dim source = fixture.Data.GetTransfers(New Date(2026, 10, 1)).Single()
                    Using form As New RegisterTransferForm With {.DataAccess = fixture.Data, .Transfer = If(editing, source, Nothing)}
                        Dim saved = ConcurrencyTests.Snapshot(fixture)
                        Dim writes As Integer
                        Dim observe As SQLiteUpdateEventHandler = Sub(sender, args) writes += 1
                        AddHandler fixture.ActualDataConnection.Update, observe
                        Try
                            Program.OpenForm(form)
                            Dim invalid As New List(Of Action(Of RegisterTransferForm))
                            For Each input In New String() {"", " ", "abc", "1.5", "1+2", "1*2", "2147483648", "-2147483649"}
                                Dim text = input
                                invalid.Add(Sub(editor) editor.FeeTextBox.Text = text)
                            Next
                            invalid.Add(Sub(editor) editor.NameComboBox.Text = "")
                            invalid.Add(Sub(editor) editor.FromPaymentClassificationComboBox.SelectedIndex = -1)
                            invalid.Add(Sub(editor) editor.ToPaymentClassificationComboBox.SelectedIndex = -1)
                            invalid.Add(Sub(editor)
                                            editor.FromPaymentClassificationComboBox.SelectedIndex = -1
                                            editor.ToPaymentClassificationComboBox.SelectedIndex = -1
                                        End Sub)
                            For Each setInvalid In invalid
                                SetInput(form, "123")
                                setInvalid(form)
                                Equal(False, form.RegiserButton.Enabled)
                                Equal(False, form.CopyButton.Enabled)
                                Dim pending = InputState(form)
                                Dim original = TransferState(form.Transfer)
                                Dim fromReference = form.Transfer.FromPaymentClassification
                                Dim toReference = form.Transfer.ToPaymentClassification
                                Program.ClickAndAnswer(form.RegiserButton, DialogResult.No, "Disabled invalid transfer register", expectedConfirmations:=0)
                                Program.ClickAndAnswer(form.CopyButton, DialogResult.No, "Disabled invalid transfer copy", expectedConfirmations:=0)
                                For Each copying In New Boolean() {False, True}
                                    Dim isCopy = copying
                                    ' ボタンを経由しない呼出でも、確認やモデルへの転記より前に止まる。
                                    form.RegiserButton.Enabled = True
                                    form.CopyButton.Enabled = True
                                    Program.ClickAndAnswer(form.RegiserButton, DialogResult.No, "Direct invalid transfer guard",
                                        expectedConfirmations:=0, invoke:=Sub() form.RegisterTransfer(isCopy))
                                    Equal(False, form.RegiserButton.Enabled)
                                    Equal(False, form.CopyButton.Enabled)
                                    Equal(True, form.Visible)
                                    Equal(DialogResult.None, form.DialogResult)
                                    Equal(False, form.ReloadRequired)
                                    Equal(pending, InputState(form))
                                    Equal(original, TransferState(form.Transfer))
                                    Equal(True, Object.ReferenceEquals(fromReference, form.Transfer.FromPaymentClassification))
                                    Equal(True, Object.ReferenceEquals(toReference, form.Transfer.ToPaymentClassification))
                                    Equal(saved, ConcurrencyTests.Snapshot(fixture))
                                Next
                            Next
                        Finally
                            RemoveHandler fixture.ActualDataConnection.Update, observe
                        End Try
                        Equal(0, writes, "Invalid input and previews must not issue any writes")
                        SetInput(form, "321")
                        Equal(True, form.RegiserButton.Enabled)
                        Equal(True, form.CopyButton.Enabled)
                        Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save corrected transfer", expectedConfirmations:=If(editing, 1, 0))
                        Equal(DialogResult.OK, form.DialogResult)
                        Dim loaded = fixture.Data.GetTransfers(New Date(2026, 10, 1)).Single(Function(item) item.Seq = form.Transfer.Seq)
                        Equal(321, loaded.Fee)
                        Equal("pending transfer", loaded.Name)
                        Equal("pending 'tag'", loaded.Tag)
                    End Using
                End Using
            Next
        Next
    End Sub

    Public Sub AcceptedValuesAndImmediateCopy()
        For Each foreignKeys In New Boolean() {False, True}
            ' 新規・更新・流用で、0・負額・型の両端を同じ条件で保存する。
            For operation As Integer = 0 To 2
                For Each amount In New Integer() {0, -17, Integer.MinValue, Integer.MaxValue}
                    Using fixture = Seed(foreignKeys)
                        Dim source = fixture.Data.GetTransfers(New Date(2026, 10, 1)).Single()
                        Dim sourceToken = source.RevisionToken
                        Dim sourceRow = ConcurrencyTests.Snapshot(fixture)
                        Using form As New RegisterTransferForm With {.DataAccess = fixture.Data, .Transfer = If(operation = 0, Nothing, source)}
                            Program.OpenForm(form)
                            Dim input = If(amount = Integer.MaxValue, " +2147483647 ", amount.ToString(CultureInfo.InvariantCulture))
                            SetInput(form, input)
                            Equal(True, form.RegiserButton.Enabled)
                            Equal(True, form.CopyButton.Enabled)
                            If operation = 2 Then
                                Program.ClickAndAnswer(form.CopyButton, DialogResult.Yes, "Immediate valid transfer copy")
                            Else
                                Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Valid transfer register", expectedConfirmations:=If(operation = 0, 0, 1))
                            End If
                            Equal(DialogResult.OK, form.DialogResult)
                            Equal(False, form.Visible)
                            Dim loaded = fixture.Data.GetTransfers(New Date(2026, 10, 1)).Single(Function(item) item.Seq = form.Transfer.Seq)
                            Equal(amount, loaded.Fee)
                            Equal("pending transfer", loaded.Name)
                            Equal("pending 'tag'", loaded.Tag)
                            Equal(New Date(2026, 10, 16, 12, 0, 0), loaded.Date)
                            Equal(1, loaded.FromPaymentClassification.Code)
                            Equal(2, loaded.ToPaymentClassification.Code)
                            Equal("integer", Convert.ToString(fixture.Scalar("SELECT typeof(fee) FROM t_transfers WHERE seq=" & loaded.Seq.ToString(CultureInfo.InvariantCulture))))
                            If operation <> 1 Then
                                Equal(2, loaded.Seq)
                                Dim original = fixture.Data.GetTransfers(New Date(2026, 10, 1)).Single(Function(item) item.Seq = 1)
                                Equal(55, original.Fee)
                                Equal(sourceToken, original.RevisionToken)
                                Equal(sourceRow, ConcurrencyTests.Snapshot(fixture, EntityKind.Transfer, loaded.Seq))
                            End If
                        End Using
                    End Using
                Next
            Next
        Next
    End Sub

    Public Sub ExistingConfirmations()
        For Each foreignKeys In New Boolean() {False, True}
            Using fixture = Seed(foreignKeys)
                Dim source = fixture.Data.GetTransfers(New Date(2026, 10, 1)).Single()
                Using form As New RegisterTransferForm With {.DataAccess = fixture.Data, .Transfer = source}
                    Program.OpenForm(form)
                    SetInput(form, "123")
                    Dim original = TransferState(source)
                    Dim saved = ConcurrencyTests.Snapshot(fixture)
                    Dim pending = InputState(form)
                    Program.ClickAndAnswer(form.CopyButton, DialogResult.No, "Decline transfer copy")
                    Equal(pending, InputState(form))
                    Program.ClickAndAnswer(form.RegiserButton, DialogResult.No, "Decline transfer update")
                    form.TransferTimeTextBox.Value = New Date(2026, 10, 16)
                    pending = InputState(form)
                    Program.ClickAndAnswer(form.CopyButton, DialogResult.No, "Decline unspecified transfer time")
                    Equal(pending, InputState(form))
                    form.TransferTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                    form.TransferDateTextBox.Value = New Date(2026, 10, 17)
                    pending = InputState(form)
                    Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Decline changed transfer date", expectedConfirmations:=2)
                    Equal(pending, InputState(form))
                    form.ToPaymentClassificationComboBox.SelectedValue = 1
                    Equal(True, form.RegiserButton.Enabled)
                    Equal(True, form.CopyButton.Enabled)
                    pending = InputState(form)
                    For Each button In New Button() {form.RegiserButton, form.CopyButton}
                        Program.ClickAndAnswer(button, DialogResult.OK, "Reject identical transfer accounts",
                            expectedWarningTitle:="エラー", expectedConfirmations:=0)
                        Equal(pending, InputState(form))
                        Equal(original, TransferState(source))
                        Equal(saved, ConcurrencyTests.Snapshot(fixture))
                        Equal(True, form.Visible)
                        Equal(DialogResult.None, form.DialogResult)
                    Next
                End Using
            End Using
        Next
    End Sub

    Private Function Seed(foreignKeys As Boolean) As DatabaseFixture
        Dim fixture As New DatabaseFixture(foreignKeys)
        Try
            fixture.Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'bank',2,1)")
            fixture.Data.RegisterTransfer(New Transfer With {.Date = New Date(2026, 10, 16, 12, 0, 0),
                .Name = "source transfer", .Fee = 55, .Tag = "source tag",
                .FromPaymentClassification = fixture.Data.PaymentClassification(1), .ToPaymentClassification = fixture.Data.PaymentClassification(2)})
            Return fixture
        Catch
            fixture.Dispose()
            Throw
        End Try
    End Function

    Private Sub SetInput(form As RegisterTransferForm, fee As String)
        form.NameComboBox.Text = "pending transfer"
        form.FromPaymentClassificationComboBox.SelectedValue = 1
        form.ToPaymentClassificationComboBox.SelectedValue = 2
        form.FeeTextBox.Text = fee
        form.TransferDateTextBox.Value = New Date(2026, 10, 16)
        form.TransferTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
        form.TransferTagTextBox.Text = "pending 'tag'"
    End Sub

    Private Function TransferState(transfer As Transfer) As String
        Return String.Join("|", transfer.Seq, transfer.RevisionToken, transfer.Date.ToString("O"), transfer.Name, transfer.Fee,
            If(transfer.FromPaymentClassification Is Nothing, 0, transfer.FromPaymentClassification.Code),
            If(transfer.ToPaymentClassification Is Nothing, 0, transfer.ToPaymentClassification.Code), transfer.Tag)
    End Function

    Private Function InputState(form As RegisterTransferForm) As String
        Return String.Join("|", form.NameComboBox.Text, form.FeeTextBox.Text,
            form.FromPaymentClassificationComboBox.SelectedIndex, form.ToPaymentClassificationComboBox.SelectedIndex,
            form.TransferDateTextBox.Value.Date.ToString("O"), form.TransferTimeTextBox.Value.TimeOfDay.ToString(), form.TransferTagTextBox.Text)
    End Function
End Module
