Imports System.Globalization
Imports System.Linq

Friend Module ReceiptOverflowTests
    Public Sub DetailRejectionAndRetry()
        For Each foreignKeys In New Boolean() {False, True}
            For Each editing In New Boolean() {False, True}
                For Each mode In New String() {"floor", "half_up"}
                    Using fixture As New DatabaseFixture(foreignKeys)
                        Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                            Program.OpenForm(form)
                            DetailInputTests.SelectDetail(form, editing)
                            form.TaxRoundingFloorRadioButton.Checked = (mode = "floor")
                            form.TaxRoundingHalfUpRadioButton.Checked = (mode = "half_up")
                            Dim prices = {2000000000, 2000000000, 2147483630, Integer.MaxValue, -2000000000}
                            Dim quantities = {2, 1, 1, 1, 2}
                            Dim discounts = {0, 0, 0, -1, 0}
                            For index As Integer = 0 To prices.Length - 1
                                SetInput(form, prices(index), quantities(index), discounts(index), index = 1)
                                Dim inputs = InputState(form)
                                Dim before = DetailInputTests.ReceiptState(receipt)
                                Dim saved = ConcurrencyTests.Snapshot(fixture)
                                Dim references = receipt.Groups(0).Details.ToArray()
                                Warn(form.AddDetailButton, "Reject overflowing detail")
                                Equal(True, form.Visible)
                                Equal(DialogResult.None, form.DialogResult)
                                Equal(inputs, InputState(form))
                                Equal(before, DetailInputTests.ReceiptState(receipt))
                                Equal(True, references.SequenceEqual(receipt.Groups(0).Details))
                                Equal(saved, ConcurrencyTests.Snapshot(fixture))
                            Next
                            form.TaxInclusiveRadioButton.Checked = True
                            SetInput(form, 20, 2, 0, False)
                            form.AddDetailButton.PerformClick()
                            Dim expected As Integer = If(editing, 59, 78)
                            Equal(expected.ToString(), form.TotalTextBox.Text)
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save corrected detail")
                            Equal(DialogResult.OK, form.DialogResult)
                            Dim savedReceipt = fixture.Data.GetReceipts(2026, 10).Single()
                            Equal(expected, savedReceipt.Total)
                            Equal(mode, savedReceipt.TaxRoundingMode)
                            Equal(CLng(expected), Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                        End Using
                    End Using
                Next
            Next
        Next
    End Sub

    Public Sub TaxPreviewAndRegistration()
        For Each foreignKeys In New Boolean() {False, True}
            For Each mode In New String() {"floor", "half_up"}
                Using fixture As New DatabaseFixture(foreignKeys)
                    fixture.Execute("UPDATE t_receipts SET tax_type=0,total=2000000019,tax_rounding_mode='" & mode & "';" &
                        "UPDATE t_receipt_details SET fee=2000000000 WHERE group_seq=1;")
                    Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                    Dim before = DetailInputTests.ReceiptState(receipt)
                    Dim saved = ConcurrencyTests.Snapshot(fixture)
                    Dim token = fixture.Scalar("SELECT revision_token FROM t_receipts WHERE seq=1")
                    Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                        Program.OpenForm(form)
                        Equal("2000000019", form.TotalTextBox.Text)
                        form.TaxExclusiveRadioButton.Checked = True
                        Equal("範囲超過", form.TotalTextBox.Text)
                        Equal("範囲超過", form.TotalTextBox2.Text)
                        Dim inputs = InputState(form)
                        Warn(form.RegiserButton, "Reject overflowing registration")
                        Equal(inputs, InputState(form))
                        Equal(before, DetailInputTests.ReceiptState(receipt))
                        Equal(saved, ConcurrencyTests.Snapshot(fixture))
                        Warn(form.CopyButton, "Reject overflowing copy before confirmation or sequence changes")
                        Equal(True, form.Visible)
                        Equal(DialogResult.None, form.DialogResult)
                        Equal(inputs, InputState(form))
                        Equal(before, DetailInputTests.ReceiptState(receipt))
                        Equal(saved, ConcurrencyTests.Snapshot(fixture))
                        form.TaxInclusiveRadioButton.Checked = True
                        Equal("2000000019", form.TotalTextBox.Text)
                        Program.ClickAndAnswer(form.CopyButton, DialogResult.Yes, "Copy corrected tax settings")
                        Equal(DialogResult.OK, form.DialogResult)
                        Equal(2, receipt.Seq)
                        Equal(2L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipts")))
                        Equal(token, fixture.Scalar("SELECT revision_token FROM t_receipts WHERE seq=1"))
                        Equal(2000000019L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                        Equal(2000000019L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=2")))
                    End Using
                End Using
            Next
        Next
    End Sub

    Private Sub SetInput(form As RegisterForm, price As Integer, quantity As Integer, discount As Integer, taxable As Boolean)
        form.ItemNameComboBox.Text = "overflow input"
        form.FeeTextBox.Text = price.ToString(CultureInfo.InvariantCulture)
        form.QuantityTextBox.Text = quantity.ToString(CultureInfo.InvariantCulture)
        form.DiscountFeeTextBox.Text = discount.ToString(CultureInfo.InvariantCulture)
        form.DiscountRateTextBox.Text = "0"
        form.DetailTagTextBox.Text = "pending tag"
        form.DetailTaxRateComboBox.SelectedValue = If(taxable, 5, 0)
        form.UserListBox.ClearSelected()
    End Sub

    Private Sub Warn(button As Button, operation As String)
        Program.ClickAndAnswer(button, DialogResult.OK, operation,
            expectedWarningTitle:="金額の範囲エラー", expectedConfirmations:=0)
    End Sub

    Private Function InputState(form As RegisterForm) As String
        Return String.Join("|", form.ItemNameComboBox.Text, form.FeeTextBox.Text, form.QuantityTextBox.Text,
            form.DiscountFeeTextBox.Text, form.DiscountRateTextBox.Text, form.DetailTagTextBox.Text,
            Convert.ToString(form.DetailTaxRateComboBox.SelectedValue), form.AddDetailButton.Text,
            form.TaxExclusiveRadioButton.Checked.ToString(), form.TaxRoundingHalfUpRadioButton.Checked.ToString(),
            form.TotalTextBox.Text, form.TotalTextBox2.Text,
            String.Join(",", form.DetailListView.SelectedIndices.Cast(Of Integer)()),
            String.Join(",", form.UserListBox.SelectedItems.Cast(Of User)().Select(Function(u) u.Code)))
    End Function
End Module
