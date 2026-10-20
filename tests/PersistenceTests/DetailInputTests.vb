Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Xml.Serialization

Friend Module DetailInputTests
    Public Sub NormalizedInputs()
        EachForeignKeyMode(
            Sub(fixture)
                For Each editing In New Boolean() {False, True}
                    Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                    Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                        Program.OpenForm(form)
                        For Each quantity In New String() {"", " ", "abc", "1.5", "0", "-1", "2147483648"}
                            SelectDetail(form, editing)
                            CheckCorrection(form, fixture, quantity, "-1", 1, 0)
                        Next
                        Dim rates = {"", " ", "-1", "101", "100.1", "abc", "NaN", "Infinity", "-Infinity", "1e309", "2.5"}
                        Dim expected = {0R, 0R, 0R, 100R, 100R, 0R, 0R, 0R, 0R, 0R, 2.5R}
                        For index As Integer = 0 To rates.Length - 1
                            SelectDetail(form, editing)
                            CheckCorrection(form, fixture, "2", rates(index), 2, expected(index))
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
                            For Each detail As ReceiptDetail In expected
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

    Public Sub MoneyExpressions()
        EachForeignKeyMode(
            Sub(fixture)
                Dim original = fixture.Data.GetReceipts(2026, 10).Single()
                Dim originalState = ReceiptState(original)
                Dim savedState = ConcurrencyTests.Snapshot(fixture)
                Using cancelled As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = original}
                    Program.OpenForm(cancelled)
                    SelectDetail(cancelled, True)
                    cancelled.FeeTextBox.Text = "100+20*3"
                    cancelled.DiscountFeeTextBox.Text = "2+3*4"
                    ValidateMoneyFields(cancelled, 160, 14)
                    cancelled.Close()
                End Using
                Equal(originalState, ReceiptState(original), "Cancelling evaluated fields must preserve the original receipt")
                Equal(savedState, ConcurrencyTests.Snapshot(fixture))

                For Each editing In New Boolean() {False, True}
                    For Each mode In New String() {"floor", "half_up"}
                        Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                            Program.OpenForm(form)
                            form.TaxInclusiveRadioButton.Checked = True
                            form.TaxRoundingFloorRadioButton.Checked = (mode = "floor")
                            form.TaxRoundingHalfUpRadioButton.Checked = (mode = "half_up")
                            Dim saved = ConcurrencyTests.Snapshot(fixture)
                            Dim added As New List(Of ReceiptDetail)
                            For Each discountField In New Boolean() {False, True}
                                For Each item As KeyValuePair(Of String, Integer) In IntegerExpressionCases.MoneyUiCases()
                                    SelectDetail(form, editing)
                                    SetInput(form, "1", "0")
                                    Dim priceText = If(discountField, "100+20*3", item.Key)
                                    Dim discountText = If(discountField, item.Key, "2+3*4")
                                    Dim price = If(discountField, 160, item.Value)
                                    Dim discount = If(discountField, item.Value, 14)
                                    form.FeeTextBox.Text = priceText
                                    form.DiscountFeeTextBox.Text = discountText
                                    Dim before = ReceiptState(receipt)
                                    Dim group = receipt.Groups(0)
                                    Dim references = group.Details.ToArray()
                                    Dim selection = String.Join(",", form.DetailListView.SelectedIndices.Cast(Of Integer)())
                                    Dim buttonText = form.AddDetailButton.Text
                                    ValidateMoneyFields(form, price, discount)
                                    Equal(before, ReceiptState(receipt), "Evaluating a field must not mutate receipt data")
                                    Equal(True, references.SequenceEqual(group.Details))
                                    Equal(selection, String.Join(",", form.DetailListView.SelectedIndices.Cast(Of Integer)()))
                                    Equal(buttonText, form.AddDetailButton.Text)
                                    Equal(saved, ConcurrencyTests.Snapshot(fixture))

                                    ' Validatedを経由しないボタン呼出も同じ計算結果を反映する。
                                    form.FeeTextBox.Text = priceText
                                    form.DiscountFeeTextBox.Text = discountText
                                    WithoutDialogs(Sub() form.AddDetailButton.PerformClick())
                                    Equal(references.Length + If(editing, 0, 1), group.Details.Count)
                                    Dim line = If(editing, group.Details(0), group.Details.Last())
                                    Equal(price, line.Fee)
                                    Equal(discount, line.DiscountFee)
                                    If Not editing Then added.Add(line)
                                    Equal(saved, ConcurrencyTests.Snapshot(fixture))
                                Next
                            Next
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save evaluated integer fields")
                            Equal(DialogResult.OK, form.DialogResult)
                            Dim reloaded = fixture.Data.GetReceipts(2026, 10).Single()
                            Equal(mode, reloaded.TaxRoundingMode)
                            Dim expected = If(editing, New List(Of ReceiptDetail) From {receipt.Groups(0).Details(0)}, added)
                            For Each line As ReceiptDetail In expected
                                Dim actual = reloaded.Groups(0).Details.Single(Function(d) d.Seq = line.Seq)
                                Equal(line.Fee, actual.Fee)
                                Equal(line.DiscountFee, actual.DiscountFee)
                            Next
                            Equal(0L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipt_details WHERE typeof(fee)<>'integer' OR typeof(discount_fee)<>'integer'")))
                        End Using
                    Next
                Next
            End Sub)
    End Sub

    Private Sub ValidateMoneyFields(form As RegisterForm, expectedPrice As Integer, expectedDiscount As Integer)
        WithoutDialogs(
            Sub()
                form.FeeTextBox.Select()
                form.ItemNameComboBox.Select()
                form.DiscountFeeTextBox.Select()
                form.ItemNameComboBox.Select()
                Application.DoEvents()
                Equal(expectedPrice.ToString(CultureInfo.InvariantCulture), form.FeeTextBox.Text)
                Equal(expectedDiscount.ToString(CultureInfo.InvariantCulture), form.DiscountFeeTextBox.Text)
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
                    WithoutDialogs(
                        Sub()
                            form.QuantityTextBox.Select()
                            form.ItemNameComboBox.Select()
                            form.DiscountRateTextBox.Select()
                            form.ItemNameComboBox.Select()
                        End Sub)
                    Equal("1", form.QuantityTextBox.Text)
                    Equal("100", form.DiscountRateTextBox.Text)
                    Equal(original, ReceiptState(receipt), "Correcting legacy fields must not change receipt data before updating the detail")
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

    Private Sub CheckCorrection(form As RegisterForm, fixture As DatabaseFixture, quantity As String, rate As String,
                                expectedQuantity As Integer, expectedRate As Double)
        SetInput(form, quantity, rate)
        Dim before = ReceiptState(form.Receipt)
        Dim saved = ConcurrencyTests.Snapshot(fixture)
        Dim inputs = InputState(form)
        Dim group = form.Receipt.Groups(0)
        Dim details = group.Details.ToArray()
        Dim editing = form.DetailListView.SelectedItems.Count > 0
        WithoutDialogs(
            Sub()
                form.QuantityTextBox.Select()
                form.ItemNameComboBox.Select()
                Application.DoEvents()
                Equal(expectedQuantity.ToString(), form.QuantityTextBox.Text)
                form.DiscountRateTextBox.Select()
                form.ItemNameComboBox.Select()
                Application.DoEvents()
                Equal(expectedRate.ToString(), form.DiscountRateTextBox.Text)
            End Sub)
        Equal(inputs, InputState(form), "Validation must preserve the other inputs and edit selection")
        Equal(before, ReceiptState(form.Receipt), "Leaving a field must not change receipt data")
        Equal(True, details.SequenceEqual(group.Details))
        Equal(saved, ConcurrencyTests.Snapshot(fixture))

        ' 欄を離れるイベントを経由しない呼出でも、追加／更新時に同じ補正を行う。
        form.QuantityTextBox.Text = quantity
        form.DiscountRateTextBox.Text = rate
        WithoutDialogs(Sub() form.AddDetailButton.PerformClick())
        Equal(details.Length + If(editing, 0, 1), group.Details.Count)
        Dim actual = If(editing, group.Details(0), group.Details.Last())
        Equal(expectedQuantity, actual.Quantity)
        Equal(expectedRate, actual.DiscountRate)
        Equal(saved, ConcurrencyTests.Snapshot(fixture), "Detail editing must not save before registration")
    End Sub

    Private Sub WithoutDialogs(action As Action)
        Using guard As New System.Windows.Forms.Timer With {.Interval = 5000}
            AddHandler guard.Tick, Sub(sender, args) Environment.FailFast("Unexpected dialog blocked automatic input correction")
            guard.Start()
            Try
                action()
            Finally
                guard.Stop()
            End Try
        End Using
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
        Return String.Join("|", form.ItemNameComboBox.Text, form.FeeTextBox.Text,
            form.DiscountFeeTextBox.Text, form.DetailTagTextBox.Text,
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
