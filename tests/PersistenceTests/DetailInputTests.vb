Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Xml.Serialization

Friend Module DetailInputTests
    Public Sub RejectedInputs()
        EachForeignKeyMode(
            Sub(fixture)
                For Each editing In New Boolean() {False, True}
                    Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                    Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                        Program.OpenForm(form)
                        SelectDetail(form, editing)
                        For Each quantity In New String() {"", " ", "abc", "1.5", "0", "-1", "2147483648"}
                            SetInput(form, quantity, "0")
                            Reject(form, fixture, form.QuantityTextBox)
                        Next
                        For Each rate In New String() {"-1", "101", "100.1", "abc", "NaN", "Infinity", "-Infinity", "1e309"}
                            SetInput(form, "1", rate)
                            Reject(form, fixture, form.DiscountRateTextBox)
                        Next
                        form.Close()
                    End Using
                Next
            End Sub)
    End Sub

    Public Sub AcceptedInputs()
        EachForeignKeyMode(
            Sub(fixture)
                For Each editing In New Boolean() {False, True}
                    For Each mode In New String() {"floor", "half_up"}
                        Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                            Program.OpenForm(form)
                            form.TaxInclusiveRadioButton.Checked = True
                            form.TaxRoundingFloorRadioButton.Checked = (mode = "floor")
                            form.TaxRoundingHalfUpRadioButton.Checked = (mode = "half_up")
                            Dim before = ConcurrencyTests.Snapshot(fixture)
                            ' 数量上限は単価0で検証し、金額のオーバーフローと切り離す。
                            Dim quantities = {Integer.MaxValue, 1, 1, 2, 1, 2}
                            Dim rates = {"100", "", " ", "0", "100", "2.5"}
                            Dim expectedRates = {100R, 0R, 0R, 0R, 100R, 2.5R}
                            Dim added As New List(Of ReceiptDetail)
                            For index As Integer = 0 To quantities.Length - 1
                                SelectDetail(form, editing)
                                SetInput(form, quantities(index).ToString(CultureInfo.InvariantCulture), rates(index))
                                form.FeeTextBox.Text = If(index = 0, "0", "-25")
                                form.DiscountFeeTextBox.Text = "0"
                                Dim oldCount = receipt.Groups(0).Details.Count
                                form.AddDetailButton.PerformClick()
                                Application.DoEvents()
                                Equal(oldCount + If(editing, 0, 1), receipt.Groups(0).Details.Count)
                                Dim detail = If(editing, receipt.Groups(0).Details(0), receipt.Groups(0).Details.Last())
                                Equal(quantities(index), detail.Quantity)
                                Equal(expectedRates(index), detail.DiscountRate)
                                Equal(If(index = 0, 0, -25), detail.Fee)
                                Equal("1", form.QuantityTextBox.Text)
                                If Not editing Then added.Add(detail)
                                Equal(before, ConcurrencyTests.Snapshot(fixture), "Adding/updating a detail must not write before registration")
                            Next
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save valid detail boundaries")
                            Equal(DialogResult.OK, form.DialogResult)
                            Dim saved = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                            Equal(mode, saved.TaxRoundingMode)
                            Equal(0, saved.TaxType)
                            Dim expected = If(editing, New List(Of ReceiptDetail) From {receipt.Groups(0).Details(0)}, added)
                            For Each detail In expected
                                Dim actual = saved.Groups(0).Details.Single(Function(d) d.Seq = detail.Seq)
                                Equal(detail.Quantity, actual.Quantity)
                                Equal(detail.DiscountRate, actual.DiscountRate)
                                Equal(detail.Fee, actual.Fee)
                            Next
                        End Using
                    Next
                Next
            End Sub)
    End Sub

    Public Sub LegacyReceipts()
        EachForeignKeyMode(
            Sub(fixture)
                fixture.Execute("UPDATE t_receipt_details SET quantity=0,discount_rate=125 WHERE group_seq=1;" &
                    "UPDATE t_receipt_details SET quantity=-2,discount_rate=150 WHERE group_seq=2;")
                Dim before = ConcurrencyTests.Snapshot(fixture)
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                Dim original = ReceiptState(receipt)
                Equal(20, receipt.Total)
                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                    Program.OpenForm(form)
                    SelectDetail(form, True)
                    Equal("0", form.QuantityTextBox.Text)
                    Equal("125", form.DiscountRateTextBox.Text)
                    form.GroupListView.SelectedItems.Clear()
                    form.GroupListView.Items(1).Selected = True
                    Application.DoEvents()
                    form.DetailListView.Items(0).Selected = True
                    Application.DoEvents()
                    Equal("-2", form.QuantityTextBox.Text)
                    Equal("150", form.DiscountRateTextBox.Text)
                    form.Close()
                End Using
                Equal(original, ReceiptState(receipt))
                Equal(before, ConcurrencyTests.Snapshot(fixture))
            End Sub)
    End Sub

    Private Sub Reject(form As RegisterForm, fixture As DatabaseFixture, invalidField As TextBox)
        Dim before = ReceiptState(form.Receipt)
        Dim saved = ConcurrencyTests.Snapshot(fixture)
        Dim inputs = InputState(form)
        Dim group = form.Receipt.Groups(0)
        Dim details = group.Details.ToArray()
        Dim users = details(0).Users.ToArray()
        Program.ClickAndAnswer(form.AddDetailButton, DialogResult.OK, "Reject invalid " & invalidField.Name,
            expectedWarningTitle:="入力エラー", expectedConfirmations:=0)
        Application.DoEvents()
        Equal(True, form.Visible)
        Equal(DialogResult.None, form.DialogResult)
        Equal(True, invalidField.Focused)
        Equal(invalidField.Text.Length, invalidField.SelectionLength)
        Equal(inputs, InputState(form), "All pending detail inputs and selection must remain unchanged")
        Equal(before, ReceiptState(form.Receipt), "Validation must precede any mutation of the receipt")
        Equal(True, details.SequenceEqual(group.Details))
        Equal(True, users.SequenceEqual(details(0).Users))
        Equal(saved, ConcurrencyTests.Snapshot(fixture))
    End Sub

    Private Sub SelectDetail(form As RegisterForm, editing As Boolean)
        form.GroupListView.SelectedItems.Clear()
        form.GroupListView.Items(0).Selected = True
        Application.DoEvents()
        form.DetailListView.SelectedItems.Clear()
        If editing Then form.DetailListView.Items(0).Selected = True
        Application.DoEvents()
        Equal(True, form.DetailPanel.Enabled)
    End Sub

    Private Sub SetInput(form As RegisterForm, quantity As String, rate As String)
        form.ItemNameComboBox.Text = "pending item"
        form.FeeTextBox.Text = "103"
        form.QuantityTextBox.Text = quantity
        form.DiscountFeeTextBox.Text = "3"
        form.DiscountRateTextBox.Text = rate
        form.DetailTagTextBox.Text = "pending tag"
        form.TaxTargetCheckBox.Checked = False
        form.UserListBox.ClearSelected()
        Equal(True, form.AddDetailButton.Enabled)
    End Sub

    Private Function InputState(form As RegisterForm) As String
        Return String.Join("|", form.ItemNameComboBox.Text, form.FeeTextBox.Text, form.QuantityTextBox.Text,
            form.DiscountFeeTextBox.Text, form.DiscountRateTextBox.Text, form.DetailTagTextBox.Text,
            form.TaxTargetCheckBox.Checked.ToString(), form.AddDetailButton.Text,
            String.Join(",", form.DetailListView.SelectedIndices.Cast(Of Integer)()),
            String.Join(",", form.UserListBox.SelectedItems.Cast(Of User)().Select(Function(u) u.Code)))
    End Function

    Private Function ReceiptState(receipt As Receipt) As String
        Using writer As New StringWriter(CultureInfo.InvariantCulture)
            Dim serializer As New XmlSerializer(GetType(Receipt))
            serializer.Serialize(writer, receipt)
            Return writer.ToString() & "|" & receipt.RevisionToken & "|" & receipt.Total & "|" & receipt.Incomes & "|" & receipt.Expenses
        End Using
    End Function

    Private Sub EachForeignKeyMode(action As Action(Of DatabaseFixture))
        For Each enabled In New Boolean() {False, True}
            Using fixture As New DatabaseFixture(enabled)
                action(fixture)
            End Using
        Next
    End Sub
End Module
