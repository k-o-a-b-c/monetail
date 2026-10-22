Imports System.Reflection
Imports System.Windows.Forms

Friend Module TaxDefaultTests
    Public Sub DefaultsIgnoreMasterOrder()
        For Each enabled In {False, True}
            Using fixture As New DatabaseFixture(enabled)
                fixture.Execute("INSERT INTO m_tax_rate VALUES(9,1,'2030-01-01','taxable');INSERT INTO m_tax_category VALUES(9,'another reduced');" &
                    "INSERT INTO m_tax_category_rule VALUES(1,'2030-01-01',9),(1,'2030-02-01',4),(9,'2030-01-01',4)")
                Dim classification = fixture.Data.GroupClassificationList.Single(Function(c) c.Code = 1)
                Equal(Nothing, fixture.Data.GetInitialTaxRate(classification, New Date(2014, 3, 31)))
                For Each test In {Tuple.Create(New Date(2014, 4, 1), 4), Tuple.Create(New Date(2019, 10, 1), 5), Tuple.Create(New Date(2030, 1, 31), 9), Tuple.Create(New Date(2030, 2, 1), 4)}
                    Equal(test.Item2, fixture.Data.GetInitialTaxRate(classification, test.Item1).Code)
                Next
                Equal(4, fixture.Data.GetInitialTaxRate(New GroupClassification With {.DefaultTaxCategoryCode = 9}, New Date(2030, 1, 1)).Code)
                Using form As New RegisterForm With {.DataAccess = fixture.Data}
                    Program.OpenForm(form)
                    form.ReceiptDateTextBox.Value = New Date(2026, 10, 1)
                    form.GroupClassificationComboBox.SelectedValue = 1
                    form.AddGroupButton.PerformClick()
                    Equal(5, DirectCast(form.DetailTaxRateComboBox.SelectedItem, TaxRate).Code)
                    form.DetailTaxRateComboBox.SelectedValue = 4
                    GetType(RegisterForm).GetMethod("DetailTaxRateComboBox_SelectionChangeCommitted", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, {form.DetailTaxRateComboBox, EventArgs.Empty})
                    form.ReceiptDateTextBox.Value = New Date(2030, 1, 1)
                    Equal(4, DirectCast(form.DetailTaxRateComboBox.SelectedItem, TaxRate).Code)
                    form.Close()
                End Using
            End Using
        Next
    End Sub

    Public Sub MissingRatesAndManualChoices()
        For Each enabled In {False, True}
            Using fixture As New DatabaseFixture(enabled)
                fixture.Execute("DELETE FROM m_tax_category_rule")
                Using form As New RegisterForm With {.DataAccess = fixture.Data}
                    Program.OpenForm(form)
                    form.GroupClassificationComboBox.SelectedValue = 1
                    Equal(True, form.AddGroupButton.Enabled)
                    form.AddGroupButton.PerformClick()
                    Equal(Nothing, form.DetailTaxRateComboBox.SelectedItem)
                    form.ItemNameComboBox.Text = "manual"
                    Equal(False, form.AddDetailButton.Enabled)
                    form.DetailTaxRateComboBox.SelectedValue = 0
                    Equal(True, form.AddDetailButton.Enabled)
                    form.FeeTextBox.Text = "19"
                    form.AddDetailButton.PerformClick()
                    Equal("out_of_scope", form.Receipt.Groups.Single().Details.Single().TaxRate.TaxTreatment)
                    form.Close()
                End Using
            End Using
        Next
    End Sub

    Public Sub ExistingRatesAndSave()
        For Each enabled In {False, True}
            Using fixture As New DatabaseFixture(enabled)
                fixture.Execute("UPDATE t_receipt_details SET tax_code=4 WHERE group_seq=1")
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                    Program.OpenForm(form)
                    SelectGroup(form, 0)
                    form.DetailListView.Items(0).Selected = True
                    Application.DoEvents()
                    Equal(4, DirectCast(form.DetailTaxRateComboBox.SelectedItem, TaxRate).Code)
                    form.ReceiptDateTextBox.Value = New Date(2030, 1, 1)
                    Equal(4, DirectCast(form.DetailTaxRateComboBox.SelectedItem, TaxRate).Code)
                    Equal(4, receipt.Groups(0).Details(0).TaxRate.Code)
                    form.ReceiptTagTextBox.Text = "header"
                    Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Header save keeps fixed detail tax")
                End Using
                Equal(4, fixture.Data.GetReceipts(2030, 1).Single().Groups(0).Details(0).TaxRate.Code)
            End Using
        Next
    End Sub

    Friend Sub SelectGroup(form As RegisterForm, index As Integer)
        form.GroupListView.Select()
        form.GroupListView.SelectedItems.Clear()
        form.GroupListView.Items(index).Selected = True
        Application.DoEvents()
    End Sub
End Module
