Imports System.Linq
Imports System.Reflection

Friend Module TaxRateSelectionUiTests
    Public Sub OrderAndDateChanges()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                fixture.Data.TaxRateList.Reverse()
                Dim reduced As New GroupClassification With {.Code = 2, .Name = "reduced", .ReducedTaxTarget = True}
                fixture.Data.GroupClassificationList.Add(reduced)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data}
                            Program.OpenForm(form)
                            Equal(10, SelectedRate(form))
                            form.GroupClassificationComboBox.SelectedItem = reduced
                            Equal(8, SelectedRate(form))
                            form.ReceiptDateTextBox.Value = New Date(2019, 9, 30)
                            Equal(8, SelectedRate(form))
                            form.GroupClassificationComboBox.SelectedIndex = 0
                            Equal(8, SelectedRate(form))
                            form.ReceiptDateTextBox.Value = New Date(2019, 10, 1)
                            Equal(10, SelectedRate(form))
                            form.GroupClassificationComboBox.SelectedIndex = -1
                            Equal(10, SelectedRate(form))
                            Equal(0, form.Receipt.Groups.Count)
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub MissingRateAndManualRecovery()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                fixture.Data.TaxRateList.RemoveAll(Function(rate) rate.Rate = 10)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data}
                            Program.OpenForm(form)
                            form.GroupClassificationComboBox.SelectedIndex = 0
                            Equal(-1, form.GroupTaxRateComboBox.SelectedIndex)
                            Equal(False, form.AddGroupButton.Enabled)
                            GetType(RegisterForm).GetMethod("AddGroupButton_Click", BindingFlags.Instance Or BindingFlags.NonPublic).
                                Invoke(form, New Object() {form.AddGroupButton, EventArgs.Empty})
                            Equal(0, form.Receipt.Groups.Count)
                            form.GroupTaxRateComboBox.SelectedIndex = 0
                            Equal(True, form.AddGroupButton.Enabled)
                            form.AddGroupButton.PerformClick()
                            Equal(8, form.Receipt.Groups.Single().TaxRate.Rate)
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub ExistingReceiptAndManualSelection()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                ' 現行の初期値と異なる保存済み8%を、既存グループの選択で復元する。
                fixture.Execute("UPDATE t_receipt_groups SET tax=4 WHERE receipt_seq=1;")
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                Dim expectedTotal = receipt.Total
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                            Program.OpenForm(form)
                            form.GroupListView.Items(0).Selected = True
                            Application.DoEvents()
                            Equal(8, SelectedRate(form))
                            Dim savedRate = receipt.Groups(0).TaxRate
                            form.GroupTaxRateComboBox.SelectedItem = fixture.Data.TaxRateList.Single(Function(rate) rate.Rate = 10)
                            form.GroupTagTextBox.Text = "unsaved"
                            Equal(10, SelectedRate(form))
                            form.ReceiptDateTextBox.Value = New Date(2019, 9, 30)
                            Equal(8, SelectedRate(form))
                            Equal(True, Object.ReferenceEquals(savedRate, receipt.Groups(0).TaxRate))
                            Equal(expectedTotal, receipt.Total)
                            Equal(New Date(2026, 10, 16, 12, 0, 0), receipt.Date)
                            form.Close()
                        End Using
                        Using reopened As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                            Program.OpenForm(reopened)
                            reopened.GroupListView.Items(0).Selected = True
                            Application.DoEvents()
                            Equal(8, SelectedRate(reopened))
                            Equal(expectedTotal, receipt.Total)
                            reopened.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Private Function SelectedRate(form As RegisterForm) As Integer
        Return DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Rate
    End Function
End Module
