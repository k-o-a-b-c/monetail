Imports System.Linq
Imports System.Reflection

Friend Module TaxRateSelectionUiTests
    Public Sub OrderAndDateChanges()
        For Each foreignKeys In {False, True}
            For Each reversed In {False, True}
                Using fixture As New DatabaseFixture(foreignKeys)
                    ' 税率とは異なるコードと追加候補で、値・並び順への依存を確認する。
                    Dim rates = fixture.Data.TaxRateList
                    Dim reducedRate As New TaxRate With {.Code = 81, .Name = "8"}
                    Dim standardRate As New TaxRate With {.Code = 101, .Name = "10"}
                    rates.Clear()
                    rates.AddRange({New TaxRate With {.Code = 10, .Name = "0"}, New TaxRate With {.Code = 8, .Name = "5"}, reducedRate, standardRate})
                    If reversed Then rates.Reverse()
                    Dim originalOrder = rates.ToArray()
                    Dim reduced As New GroupClassification With {.Code = 2, .Name = "reduced", .ReducedTaxTarget = True}
                    fixture.Data.GroupClassificationList.Add(reduced)
                    BalanceBoundaryTests.ReadOnlyCase(fixture,
                        Sub()
                            Using form As New RegisterForm With {.DataAccess = fixture.Data}
                                Program.OpenForm(form)
                                For Each receiptDate In {New Date(2019, 9, 30, 23, 59, 59), New Date(2019, 10, 1), New Date(2026, 10, 23)}
                                    form.ReceiptDateTextBox.Value = receiptDate
                                    For Each classificationIndex In {-1, 0, 1}
                                        form.GroupClassificationComboBox.SelectedIndex = classificationIndex
                                        Dim expected = If(receiptDate.Date < New Date(2019, 10, 1) OrElse classificationIndex = 1, reducedRate, standardRate)
                                        Equal(True, Object.ReferenceEquals(expected, form.GroupTaxRateComboBox.SelectedItem))
                                    Next
                                Next
                                Equal(True, originalOrder.SequenceEqual(rates))
                                Equal(0, form.Receipt.Groups.Count)
                                form.Close()
                            End Using
                        End Sub)
                End Using
            Next
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
                            Dim savedGroup = form.Receipt.Groups.Single()
                            Equal(8, savedGroup.TaxRate.Rate)
                            ' 候補が空でも未選択となり、直接呼出で既存グループを変更しない。
                            fixture.Data.TaxRateList.Clear()
                            form.GroupTaxRateComboBox.DataSource = Nothing
                            form.GroupTaxRateComboBox.DataSource = fixture.Data.TaxRateList
                            form.ReceiptDateTextBox.Value = New Date(2019, 9, 30)
                            Equal(-1, form.GroupTaxRateComboBox.SelectedIndex)
                            Equal(False, form.AddGroupButton.Enabled)
                            GetType(RegisterForm).GetMethod("AddGroupButton_Click", BindingFlags.Instance Or BindingFlags.NonPublic).
                                Invoke(form, New Object() {form.AddGroupButton, EventArgs.Empty})
                            Equal(True, Object.ReferenceEquals(savedGroup, form.Receipt.Groups.Single()))
                            Equal(8, savedGroup.TaxRate.Rate)
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
