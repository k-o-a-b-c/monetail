Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms

Friend Module TaxDefaultTests
    Public Sub DefaultsIgnoreMasterOrder()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data}
                            Program.OpenForm(form)
                            Equal(8, DirectCast(form.GroupTaxRateComboBox.Items(0), TaxRate).Code, "Future and duplicate rates precede current rates")
                            Dim original = DetailInputTests.ReceiptState(form.Receipt)
                            For Each ascending In {True, False}
                                Dim ordered = fixture.Data.TaxRateList.OrderBy(Function(t) t.Code)
                                form.GroupTaxRateComboBox.DataSource = If(ascending, ordered.ToList(), ordered.Reverse().ToList())
                                For Each receiptDate In {New Date(2019, 9, 30), New Date(2019, 10, 1), New Date(2026, 10, 16)}
                                    form.ReceiptDateTextBox.Value = receiptDate
                                    For Each code In {-1, 1, 2}
                                        If code = -1 Then
                                            form.GroupClassificationComboBox.SelectedIndex = -1
                                        Else
                                            form.GroupClassificationComboBox.SelectedValue = code
                                        End If
                                        Application.DoEvents()
                                        Dim expected = If(receiptDate < New Date(2019, 10, 1) OrElse code = 2, 4, 5)
                                        Equal(expected, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                                        Equal(If(expected = 4, 8, 10), DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Rate)
                                        Equal(True, Object.ReferenceEquals(fixture.Data.TaxRateList.Single(Function(t) t.Code = expected), form.GroupTaxRateComboBox.SelectedItem))
                                        Equal(original, DetailInputTests.ReceiptState(form.Receipt), "Default selection must not change the receipt model")
                                    Next
                                Next
                            Next
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub MissingRatesAndManualChoices()
        For Each foreignKeys In {False, True}
            For Each available In {0, 10, 12}
                Using fixture As New DatabaseFixture(foreignKeys)
                    Seed(fixture)
                    fixture.Execute("DELETE FROM m_tax_rate;")
                    If available = 10 Then fixture.Execute("INSERT INTO m_tax_rate VALUES(5,10,'2019-10-01');")
                    If available = 12 Then fixture.Execute("INSERT INTO m_tax_rate VALUES(6,12,'2030-01-01');")
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub()
                            Using form As New RegisterForm With {.DataAccess = fixture.Data}
                                Program.OpenForm(form)
                                form.ReceiptDateTextBox.Value = New Date(2026, 10, 16)
                                form.GroupClassificationComboBox.SelectedValue = 2
                                Equal(Nothing, form.GroupTaxRateComboBox.SelectedItem, "Missing 8% must remain unselected")
                                Equal(False, form.AddGroupButton.Enabled)
                                Dim original = DetailInputTests.ReceiptState(form.Receipt)
                                form.AddGroupButton.Enabled = True
                                InvokeAddGroup(form)
                                Equal(False, form.AddGroupButton.Enabled)
                                Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                                form.GroupClassificationComboBox.SelectedValue = 1
                                Equal(available = 10, form.GroupTaxRateComboBox.SelectedItem IsNot Nothing)
                                If available > 0 Then
                                    form.GroupTaxRateComboBox.SelectedValue = If(available = 10, 5, 6)
                                    Equal(True, form.AddGroupButton.Enabled, "Explicit manual choice must enable adding")
                                    form.AddGroupButton.PerformClick()
                                    Equal(available, form.Receipt.Groups.Single().TaxRate.Rate)
                                End If
                                form.Close()
                            End Using
                        End Sub)
                End Using
            Next
            ' 統合は統合先の税率を維持するため、初期税率の欠損で操作を増やさない。
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                fixture.Execute("UPDATE t_receipt_groups SET tax=6;UPDATE t_receipt_groups SET group_classification_code=2 WHERE seq=2;" &
                    "DELETE FROM m_tax_rate WHERE code<>6;")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                            Program.OpenForm(form)
                            SelectGroup(form, 0)
                            form.ReceiptDateTextBox.Value = New Date(2026, 10, 16)
                            form.GroupClassificationComboBox.SelectedValue = 2
                            Equal(Nothing, form.GroupTaxRateComboBox.SelectedItem)
                            Equal("統合", form.AddGroupButton.Text)
                            Equal(True, form.AddGroupButton.Enabled)
                            form.AddGroupButton.PerformClick()
                            Equal(2, form.Receipt.Groups.Single().GroupClassification.Code)
                            Equal(6, form.Receipt.Groups.Single().TaxRate.Code)
                            Equal(2, form.Receipt.Groups.Single().Details.Count)
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub ExistingRatesAndSave()
        For Each foreignKeys In {False, True}
            For Each codes In New Integer()() {New Integer() {6, 4}, New Integer() {7, 8}}
                Dim firstTax = codes(0)
                Dim secondTax = codes(1)
                Using fixture As New DatabaseFixture(foreignKeys)
                    Seed(fixture)
                    fixture.Execute("UPDATE t_receipt_groups SET tax=" & firstTax.ToString() & " WHERE seq=1;" &
                        "UPDATE t_receipt_groups SET tax=" & secondTax.ToString() & ",group_classification_code=2 WHERE seq=2;")
                    Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub()
                            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                                Program.OpenForm(form)
                                Dim original = DetailInputTests.ReceiptState(receipt)
                                SelectGroup(form, 0)
                                Equal(firstTax, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                                form.ReceiptDateTextBox.Value = New Date(2019, 9, 30)
                                Equal(4, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                                Equal(original, DetailInputTests.ReceiptState(receipt))
                                SelectGroup(form, 1)
                                Equal(secondTax, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                                form.Close()
                                Equal(original, DetailInputTests.ReceiptState(receipt))
                            End Using
                        End Sub)
                    Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                        Program.OpenForm(form)
                        form.ReceiptTagTextBox.Text = "header-only update"
                        Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save must preserve existing tax codes")
                        Equal(DialogResult.OK, form.DialogResult)
                    End Using
                    Dim saved = fixture.Data.GetReceipts(2026, 10).Single()
                    Equal(firstTax.ToString() & "," & secondTax.ToString(), String.Join(",", saved.Groups.Select(Function(g) g.TaxRate.Code)))
                    Equal("header-only update", saved.Tag)
                    Equal(If(firstTax = 6, 41, 40), saved.Total)
                    Equal(If(firstTax = 6, 41L, 40L), Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                End Using
            Next
        Next
    End Sub

    Private Sub SelectGroup(form As RegisterForm, index As Integer)
        form.GroupListView.Select()
        form.GroupListView.SelectedItems.Clear()
        form.GroupListView.Items(index).Selected = True
        Application.DoEvents()
    End Sub

    Private Sub InvokeAddGroup(form As RegisterForm)
        GetType(RegisterForm).GetMethod("AddGroupButton_Click", BindingFlags.Instance Or BindingFlags.NonPublic).
            Invoke(form, New Object() {form.AddGroupButton, EventArgs.Empty})
    End Sub

    Private Sub Seed(fixture As DatabaseFixture)
        fixture.Execute("INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(2,'reduced',2,1,1);" &
            "INSERT INTO m_tax_rate(code,rate,start_date) VALUES(6,12,'2030-01-01'),(7,10,'2031-01-01'),(8,8,'2032-01-01');")
    End Sub
End Module
