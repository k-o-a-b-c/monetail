Imports System.Linq
Imports System.Reflection
Imports System.Xml.Linq

Friend Module BalanceInputStateTests
    Private ReadOnly originalDate As New Date(2026, 10, 16)
    Private ReadOnly pendingDate As New Date(2026, 10, 17)

    Public Sub InvalidInputsAndReopen()
        BalanceBoundaryTests.EachForeignKeyMode(
            Sub(fixture)
                Seed(fixture)
                For Each editing As Boolean In New Boolean() {False, True}
                    Dim isEditing = editing
                    For invalid As Integer = 0 To 5
                        Dim invalidCase = invalid
                        Dim source = If(isEditing, fixture.Data.GetBalances(originalDate).Single(), Nothing)
                        BalanceBoundaryTests.ReadOnlyCase(fixture,
                            Sub()
                                Using form As New RegisterBalanceForm With {.DataAccess = fixture.Data, .Balance = source}
                                    Program.OpenForm(form)
                                    Dim model = form.Balance
                                    Dim before = State(model)
                                    Dim list = model.Groups
                                    Dim references = model.Groups.ToArray()
                                    SetPending(form)
                                    If invalidCase = 0 Then
                                        ClearRows(form)
                                    Else
                                        Dim row = form.BalanceDataGridView.Rows(1)
                                        Select Case invalidCase
                                            Case 1
                                                row.Cells(form.BalanceColumn.Index).Value = Nothing
                                                row.Cells(form.ValuationAmountColumn.Index).Value = 42
                                            Case 2
                                                row.Cells(form.BalanceColumn.Index).Value = "abc"
                                            Case 3
                                                row.Cells(form.BalanceColumn.Index).Value = "2147483648"
                                            Case 4
                                                row.Cells(form.ValuationAmountColumn.Index).Value = "abc"
                                            Case 5
                                                row.Cells(form.ValuationAmountColumn.Index).Value = "-2147483649"
                                        End Select
                                    End If
                                    Dim pending = InputState(form)
                                    ' Parseの防御はCellValidatedを経由しない値でも検証する。
                                    If invalidCase < 2 Then
                                        Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Reject incomplete balance input",
                                            expectedWarningTitle:="エラー", expectedConfirmations:=0)
                                    Else
                                        Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Reject unvalidated balance integers",
                                            expectedWarningTitle:="エラー", expectedConfirmations:=0, invoke:=Sub() InvokeRegister(form))
                                    End If
                                    Equal(pending, InputState(form))
                                    AssertOriginal(form, model, before, list, references)
                                    form.Close()
                                    If isEditing Then
                                        ' 親の一覧と同じ参照を再利用し、DBから読み直さずに開き直す。
                                        Using reopened As New RegisterBalanceForm With {.DataAccess = fixture.Data, .Balance = model}
                                            Program.OpenForm(reopened)
                                            Equal(before, State(reopened.Balance))
                                            Equal(originalDate, reopened.BalanceDateTextBox.Value.Date)
                                            Equal("original header", reopened.BalanceTagTextBox.Text)
                                            Equal(11, CInt(reopened.BalanceDataGridView.Rows(0).Cells(reopened.BalanceColumn.Index).Value))
                                            Equal(100, CInt(reopened.BalanceDataGridView.Rows(0).Cells(reopened.ValuationAmountColumn.Index).Value))
                                            Equal(22, CInt(reopened.BalanceDataGridView.Rows(1).Cells(reopened.BalanceColumn.Index).Value))
                                            Equal(True, reopened.BalanceDataGridView.Rows(1).Cells(reopened.ValuationAmountColumn.Index).Value Is Nothing)
                                            Equal(True, Object.ReferenceEquals(model, reopened.Balance))
                                            reopened.Close()
                                        End Using
                                    End If
                                End Using
                            End Sub)
                    Next
                Next
            End Sub)
    End Sub

    Public Sub CorrectAndSave()
        BalanceBoundaryTests.EachForeignKeyMode(
            Sub(fixture)
                Seed(fixture)
                For Each editing As Boolean In New Boolean() {True, False}
                    Dim source = If(editing, fixture.Data.GetBalances(originalDate).Single(Function(item) item.Seq = 1), Nothing)
                    Dim before = If(source Is Nothing, Nothing, source.RevisionToken)
                    Dim expectedSeq As Integer = If(editing, 1, 2)
                    Dim unaffected = ConcurrencyTests.Snapshot(fixture, EntityKind.Balance, expectedSeq)
                    Using form As New RegisterBalanceForm With {.DataAccess = fixture.Data, .Balance = source}
                        BalanceBoundaryTests.ReadOnlyCase(fixture,
                            Sub()
                                Program.OpenForm(form)
                                SetPending(form)
                                form.BalanceDataGridView.Rows(1).Cells(form.BalanceColumn.Index).Value = Nothing
                                form.BalanceDataGridView.Rows(1).Cells(form.ValuationAmountColumn.Index).Value = 42
                                Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Reject balance before correction",
                                    expectedWarningTitle:="エラー", expectedConfirmations:=0)
                            End Sub)
                        ' 正常な0円・負額、任意評価額と型の境界を、そのまま保存する。
                        SetPending(form)
                        form.BalanceDateTextBox.Value = If(editing, originalDate, pendingDate)
                        form.BalanceTagTextBox.Text = "corrected 'header'"
                        form.BalanceDataGridView.Rows(0).Cells(form.BalanceColumn.Index).Value = 0
                        form.BalanceDataGridView.Rows(0).Cells(form.ValuationAmountColumn.Index).Value = Integer.MaxValue
                        form.BalanceDataGridView.Rows(1).Cells(form.BalanceColumn.Index).Value = Integer.MinValue
                        Dim previous = Calculation.DataAccess
                        Try
                            Calculation.DataAccess = fixture.Data
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save corrected balance", expectedConfirmations:=If(editing, 1, 0))
                        Finally
                            Calculation.DataAccess = previous
                        End Try
                        Equal(DialogResult.OK, form.DialogResult)
                        Equal(False, form.Visible)
                        Equal(expectedSeq, form.Balance.Seq)
                        Dim loaded = fixture.Data.GetBalances(originalDate).Single(Function(item) item.Seq = expectedSeq)
                        Equal(If(editing, originalDate, pendingDate), loaded.Date)
                        Equal("corrected 'header'", loaded.Tag)
                        Equal(2, loaded.Groups.Count)
                        Equal(0, loaded.Groups(0).Balance)
                        Equal(Of Integer?)(Integer.MaxValue, loaded.Groups(0).ValuationAmount)
                        Equal(Integer.MinValue, loaded.Groups(1).Balance)
                        Equal(Of Integer?)(Nothing, loaded.Groups(1).ValuationAmount)
                        Equal("first pending", loaded.Groups(0).Tag)
                        Equal("second pending", loaded.Groups(1).Tag)
                        Equal(True, loaded.RevisionToken <> before)
                        Equal(unaffected, ConcurrencyTests.Snapshot(fixture, EntityKind.Balance, expectedSeq))
                    End Using
                Next
            End Sub)
    End Sub

    Public Sub DeclinedConfirmations()
        BalanceBoundaryTests.EachForeignKeyMode(
            Sub(fixture)
                Dim source = Seed(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterBalanceForm With {.DataAccess = fixture.Data, .Balance = source}
                            Program.OpenForm(form)
                            Dim before = State(source)
                            Dim list = source.Groups
                            Dim references = source.Groups.ToArray()
                            SetPending(form)
                            form.BalanceDateTextBox.Value = originalDate
                            Dim pending = InputState(form)
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.No, "Decline validated balance update")
                            Equal(pending, InputState(form))
                            AssertOriginal(form, source, before, list, references)
                            form.BalanceDateTextBox.Value = pendingDate
                            pending = InputState(form)
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Decline validated balance date change", expectedConfirmations:=2)
                            Equal(pending, InputState(form))
                            AssertOriginal(form, source, before, list, references)
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Private Function Seed(fixture As DatabaseFixture) As Balance
        Dim balance As New Balance With {.Date = originalDate, .Tag = "original header"}
        balance.Groups.Add(New BalanceGroup With {.PaymentClassification = fixture.Data.PaymentClassification(1), .Balance = 11, .ValuationAmount = 100, .Tag = "original first"})
        balance.Groups.Add(New BalanceGroup With {.PaymentClassification = fixture.Data.PaymentClassification(2), .Balance = 22, .Tag = "original second"})
        fixture.Data.RegisterBalance(balance)
        Return fixture.Data.GetBalances(originalDate).Single()
    End Function

    Private Sub SetPending(form As RegisterBalanceForm)
        ClearRows(form)
        form.BalanceDateTextBox.Value = pendingDate
        form.BalanceTagTextBox.Text = "pending header"
        form.BalanceDataGridView.Rows(0).Cells(form.BalanceColumn.Index).Value = 123
        form.BalanceDataGridView.Rows(0).Cells(form.TagColumn.Index).Value = "first pending"
        form.BalanceDataGridView.Rows(1).Cells(form.BalanceColumn.Index).Value = 456
        form.BalanceDataGridView.Rows(1).Cells(form.TagColumn.Index).Value = "second pending"
    End Sub

    Private Sub ClearRows(form As RegisterBalanceForm)
        For Each row As DataGridViewRow In form.BalanceDataGridView.Rows
            row.Cells(form.BalanceColumn.Index).Value = Nothing
            row.Cells(form.ValuationAmountColumn.Index).Value = Nothing
            row.Cells(form.TagColumn.Index).Value = Nothing
        Next
    End Sub

    Private Sub InvokeRegister(form As RegisterBalanceForm)
        GetType(RegisterBalanceForm).GetMethod("RegiserButton_Click", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(
            form, {form.RegiserButton, EventArgs.Empty})
    End Sub

    Private Sub AssertOriginal(form As RegisterBalanceForm, model As Balance, before As String, list As List(Of BalanceGroup), references As BalanceGroup())
        Equal(before, State(model))
        Equal(True, Object.ReferenceEquals(model, form.Balance))
        Equal(True, Object.ReferenceEquals(list, model.Groups))
        Equal(references.Length, model.Groups.Count)
        For index As Integer = 0 To references.Length - 1
            Equal(True, Object.ReferenceEquals(references(index), model.Groups(index)))
        Next
        Equal(True, form.Visible)
        Equal(DialogResult.None, form.DialogResult)
        Equal(False, form.ReloadRequired)
    End Sub

    Private Function State(balance As Balance) As String
        Return New XElement("balance", New XAttribute("seq", balance.Seq), New XAttribute("date", balance.Date.ToString("O")),
            New XElement("token", balance.RevisionToken), New XElement("tag", balance.Tag),
            balance.Groups.Select(Function(group) New XElement("group", New XAttribute("balanceSeq", group.BalanceSeq),
                New XAttribute("payment", group.PaymentClassification.Code), New XAttribute("balance", group.Balance),
                New XElement("valuation", group.ValuationAmount), New XElement("tag", group.Tag)))).ToString(SaveOptions.DisableFormatting)
    End Function

    Private Function InputState(form As RegisterBalanceForm) As String
        Dim values As New List(Of String) From {form.BalanceDateTextBox.Value.Date.ToString("O"), form.BalanceTagTextBox.Text}
        For Each row As DataGridViewRow In form.BalanceDataGridView.Rows
            For Each column As Integer In New Integer() {form.BalanceColumn.Index, form.ValuationAmountColumn.Index, form.TagColumn.Index}
                Dim value = row.Cells(column).Value
                values.Add(If(value Is Nothing, "<null>", value.GetType().FullName & ":" & value.ToString()))
            Next
        Next
        Return String.Join("|", values)
    End Function
End Module
