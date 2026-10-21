Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms

Friend Module ClassificationTaxTests
    Public Sub StandardRateLabels()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                fixture.Execute("INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(3,'reduced',3,1,1);" &
                    "INSERT INTO m_tax_rate(code,rate,start_date) VALUES(7,10,'2031-01-01'),(8,8,'2032-01-01');" &
                    "UPDATE t_receipt_groups SET tax=4 WHERE seq=1;" &
                    "INSERT INTO t_receipt_groups(receipt_seq,seq,group_classification_code,tax,tag) VALUES" &
                    "(1,3,3,4,''),(1,4,3,5,''),(1,5,1,7,''),(1,6,1,8,'');")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                            Program.OpenForm(form)
                            AssertLabels(form, {"交際費（8%）", "交際費", "reduced", "reduced（10%）", "交際費", "交際費（8%）"})
                            Dim original = DetailInputTests.ReceiptState(form.Receipt)
                            SelectGroup(form, 1)
                            form.GroupTagTextBox.Text = "pending note"
                            form.ItemNameComboBox.Text = "pending item"
                            form.FeeTextBox.Text = "123"
                            form.ReceiptDateTextBox.Value = New Date(2019, 9, 30)
                            AssertLabels(form, {"交際費", "交際費（10%）", "reduced", "reduced（10%）", "交際費（10%）", "交際費"})
                            Equal(4, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                            Equal(1, form.GroupListView.SelectedIndices(0))
                            Equal("pending note", form.GroupTagTextBox.Text)
                            Equal("pending item", form.ItemNameComboBox.Text)
                            Equal("123", form.FeeTextBox.Text)
                            Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                            form.ReceiptDateTextBox.Value = New Date(2019, 10, 1)
                            AssertLabels(form, {"交際費（8%）", "交際費", "reduced", "reduced（10%）", "交際費", "交際費（8%）"})
                            Equal(5, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                            Equal(1, form.GroupListView.SelectedIndices(0))
                            Equal("pending note", form.GroupTagTextBox.Text)
                            form.GroupTaxRateComboBox.SelectedValue = 4
                            Equal("交際費", form.GroupListView.Items(1).Text, "Manual selection must not redefine the standard")
                            Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                            form.Close()
                            Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                        End Using
                    End Sub)
            End Using
            Using fixture As New DatabaseFixture(foreignKeys)
                fixture.Execute("UPDATE t_receipt_groups SET tax=4;DELETE FROM m_tax_rate WHERE code=5;")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                            Program.OpenForm(form)
                            AssertLabels(form, {"food（8%）", "food（8%）"})
                            Dim original = DetailInputTests.ReceiptState(form.Receipt)
                            SelectGroup(form, 0)
                            form.ReceiptDateTextBox.Value = New Date(2019, 9, 30)
                            AssertLabels(form, {"food", "food"})
                            form.ReceiptDateTextBox.Value = New Date(2019, 10, 1)
                            Equal(Nothing, form.GroupTaxRateComboBox.SelectedItem)
                            AssertLabels(form, {"food（8%）", "food（8%）"})
                            Equal(0, form.GroupListView.SelectedIndices(0))
                            Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub AdditionAndSelection()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data}
                            Program.OpenForm(form)
                            AddGroup(form, 1, 4)
                            Dim eight = form.Receipt.Groups.Single()
                            Equal("交際費（8%）", form.GroupListView.Items(0).Text)
                            AddDetail(form, "eight", 19)
                            ClearGroupSelection(form)
                            Equal(False, form.AddGroupButton.Enabled, "The same classification and tax cannot be added twice")
                            Dim before = DetailInputTests.ReceiptState(form.Receipt)
                            InvokeAddGroup(form)
                            Equal(before, DetailInputTests.ReceiptState(form.Receipt))
                            AddGroup(form, 1, 5)
                            Dim ten = form.Receipt.Groups(1)
                            Equal(2, form.Receipt.Groups.Count)
                            Equal(1, form.GroupListView.SelectedIndices(0), "Adding the second tax must select its own row")
                            Equal("交際費", form.GroupListView.Items(1).Text)
                            Equal(5, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                            AddDetail(form, "ten", 31)
                            Equal("eight", eight.Details.Single().Name)
                            Equal("ten", ten.Details.Single().Name)
                            form.GroupTagTextBox.Text = "ten tag"
                            form.AddGroupButton.PerformClick()
                            Equal(1, form.GroupListView.SelectedIndices(0))
                            Equal("ten tag", ten.Tag)
                            Equal("", eight.Tag)
                            ClearGroupSelection(form)
                            Equal(False, form.AddGroupButton.Enabled)
                            form.GroupTaxRateComboBox.SelectedValue = 4
                            Equal(False, form.AddGroupButton.Enabled)
                            form.GroupClassificationComboBox.SelectedIndex = -1
                            InvokeAddGroup(form)
                            Equal(2, form.Receipt.Groups.Count)
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub EditsAndMerges()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                fixture.Execute("UPDATE t_receipt_groups SET tax=4,tag='destination' WHERE seq=1;" &
                    "INSERT INTO t_receipt_groups(receipt_seq,seq,group_classification_code,tax,tag) VALUES(1,3,2,5,'other destination');")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                            Program.OpenForm(form)
                            Dim eight = receipt.Groups(0)
                            Dim ten = receipt.Groups(1)
                            Dim other = receipt.Groups(2)
                            Dim sourceDetail = ten.Details.Single()
                            SelectGroup(form, 1)
                            form.GroupTaxRateComboBox.SelectedValue = 6
                            Equal("更新", form.AddGroupButton.Text)
                            Equal(True, form.GroupTagTextBox.Enabled)
                            form.AddGroupButton.PerformClick()
                            Equal(3, receipt.Groups.Count)
                            Equal(6, ten.TaxRate.Code)
                            Equal(4, eight.TaxRate.Code)
                            Equal(1, form.GroupListView.SelectedIndices(0))
                            form.GroupTaxRateComboBox.SelectedValue = 4
                            Equal("統合", form.AddGroupButton.Text)
                            Equal(False, form.GroupTagTextBox.Enabled)
                            form.AddGroupButton.PerformClick()
                            Equal(2, receipt.Groups.Count)
                            Equal(True, Object.ReferenceEquals(eight, receipt.Groups(0)))
                            Equal(True, Object.ReferenceEquals(sourceDetail, eight.Details(1)))
                            Equal(False, receipt.Groups.Contains(ten))
                            Equal("destination", eight.Tag)
                            Equal(4, eight.TaxRate.Code)
                            Equal(0, form.GroupListView.SelectedIndices(0))
                            ' 分類変更でも税率が違えば統合せず、同じ分類に両方を保持する。
                            form.GroupClassificationComboBox.SelectedValue = 2
                            form.GroupTaxRateComboBox.SelectedValue = 4
                            Equal("更新", form.AddGroupButton.Text)
                            form.AddGroupButton.PerformClick()
                            Equal(2, receipt.Groups.Count)
                            Equal(2, eight.GroupClassification.Code)
                            Equal(4, eight.TaxRate.Code)
                            Equal(5, other.TaxRate.Code)
                            form.GroupTaxRateComboBox.SelectedValue = 5
                            Equal("統合", form.AddGroupButton.Text)
                            form.AddGroupButton.PerformClick()
                            Equal(True, Object.ReferenceEquals(other, receipt.Groups.Single()))
                            Equal("other destination", other.Tag)
                            Equal(5, other.TaxRate.Code)
                            Equal(2, other.Details.Count)
                            Equal(True, Object.ReferenceEquals(sourceDetail, other.Details(1)))
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub SaveAndStatistics()
        For Each foreignKeys In {False, True}
            For Each mode In {"floor", "half_up"}
                For Each taxType In {0, 1}
                    Using fixture As New DatabaseFixture(foreignKeys)
                        Seed(fixture)
                        fixture.Data.DeleteReceipt(fixture.Data.GetReceipts(2026, 10).Single())
                        Dim expected = If(taxType = 0, 57, If(mode = "floor", 61, 63))
                        Using form As New RegisterForm With {.DataAccess = fixture.Data}
                            Program.OpenForm(form)
                            form.ReceiptDateTextBox.Value = New Date(2026, 10, 16)
                            form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                            form.ShopNameComboBox.Text = "mixed tax shop"
                            form.PaymentClassificationComboBox.SelectedValue = 1
                            form.TaxInclusiveRadioButton.Checked = (taxType = 0)
                            form.TaxExclusiveRadioButton.Checked = (taxType = 1)
                            form.TaxRoundingFloorRadioButton.Checked = (mode = "floor")
                            form.TaxRoundingHalfUpRadioButton.Checked = (mode = "half_up")
                            AddGroup(form, 1, 4)
                            AddDetail(form, "eight", 19)
                            AddGroup(form, 1, 5)
                            AddDetail(form, "ten one", 19)
                            AddDetail(form, "ten two", 19)
                            Equal(expected.ToString(), form.TotalTextBox.Text)
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save both tax rates within one classification", expectedConfirmations:=0)
                            Equal(DialogResult.OK, form.DialogResult)
                        End Using
                        Dim saved = fixture.Data.GetReceipts(2026, 10).Single()
                        AssertSaved(saved, mode, taxType, expected)
                        Equal(CLng(expected), Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts")))
                        BalanceBoundaryTests.ReadOnlyCase(fixture,
                            Sub()
                                fixture.Data.LoadReceiptChildData(saved)
                                AssertSaved(saved, mode, taxType, expected)
                                Using statistics As New ViewForm(fixture.Data)
                                    statistics.Show()
                                    Application.DoEvents()
                                    statistics.RefreshStatistics(True)
                                    Equal(expected, CInt(statistics.GroupStatisticsDataGridView.Rows(0).Cells(11).Value))
                                    Equal(expected, CInt(statistics.GroupStatisticsDataGridView.Rows(0).Cells(14).Value))
                                    Equal(0, CInt(statistics.GroupStatisticsDataGridView.Rows(1).Cells(14).Value))
                                    Equal(expected, CInt(statistics.GroupStatisticsDataGridView.Rows(2).Cells(14).Value))
                                    statistics.Close()
                                End Using
                            End Sub)
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = saved}
                            Program.OpenForm(form)
                            SelectGroup(form, 1)
                            Equal(5, DirectCast(form.GroupTaxRateComboBox.SelectedItem, TaxRate).Code)
                            form.GroupTagTextBox.Text = "ten only"
                            form.AddGroupButton.PerformClick()
                            Equal("", saved.Groups(0).Tag)
                            Equal("ten only", saved.Groups(1).Tag)
                            Equal(1, form.GroupListView.SelectedIndices(0))
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Update the selected tax group")
                            Equal(DialogResult.OK, form.DialogResult)
                        End Using
                        saved = fixture.Data.GetReceipts(2026, 10).Single()
                        AssertSaved(saved, mode, taxType, expected)
                        Equal("ten only", saved.Groups(1).Tag)
                        Dim sourceSeq = saved.Seq
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = saved}
                            Program.OpenForm(form)
                            Program.ClickAndAnswer(form.CopyButton, DialogResult.Yes, "Copy must immediately save both tax groups")
                            Equal(DialogResult.OK, form.DialogResult)
                        End Using
                        Dim both = fixture.Data.GetReceipts(2026, 10)
                        Equal(2, both.Count)
                        For Each receipt In both
                            AssertSaved(receipt, mode, taxType, expected)
                            Equal("ten only", receipt.Groups(1).Tag)
                        Next
                        Equal(True, both.Any(Function(r) r.Seq = sourceSeq))
                    End Using
                Next
            Next
        Next
    End Sub

    Public Sub MissingTaxAndLegacyGroups()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                fixture.Execute("UPDATE t_receipt_groups SET tax=6 WHERE seq=1;" &
                    "UPDATE t_receipt_groups SET group_classification_code=2 WHERE seq=2;" &
                    "INSERT INTO t_receipt_groups(receipt_seq,seq,group_classification_code,tax,tag) VALUES(1,3,2,6,'');" &
                    "DELETE FROM m_tax_rate WHERE code=4;")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                            Program.OpenForm(form)
                            SelectGroup(form, 0)
                            form.ReceiptDateTextBox.Value = New Date(2019, 9, 30)
                            form.GroupClassificationComboBox.SelectedValue = 2
                            Equal(Nothing, form.GroupTaxRateComboBox.SelectedItem)
                            Equal(False, form.AddGroupButton.Enabled, "Two destination tax rates require an explicit choice")
                            Dim original = DetailInputTests.ReceiptState(form.Receipt)
                            form.AddGroupButton.Enabled = True
                            InvokeAddGroup(form)
                            Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                            Equal(False, form.AddGroupButton.Enabled)
                            form.GroupTaxRateComboBox.SelectedValue = 5
                            Equal("統合", form.AddGroupButton.Text)
                            form.AddGroupButton.PerformClick()
                            Equal(2, form.Receipt.Groups.Count)
                            Equal(5, form.Receipt.Groups(0).TaxRate.Code)
                            Equal(2, form.Receipt.Groups(0).Details.Count)
                            Equal(6, form.Receipt.Groups(1).TaxRate.Code)
                            form.Close()
                        End Using
                    End Sub)
            End Using
            Using fixture As New DatabaseFixture(foreignKeys)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                            Program.OpenForm(form)
                            Dim original = DetailInputTests.ReceiptState(form.Receipt)
                            SelectGroup(form, 1)
                            Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                            Equal("更新", form.AddGroupButton.Text)
                            form.GroupTagTextBox.Text = "second legacy group"
                            form.AddGroupButton.PerformClick()
                            Equal(2, form.Receipt.Groups.Count)
                            Equal(1, form.GroupListView.SelectedIndices(0))
                            Equal("", form.Receipt.Groups(0).Tag)
                            Equal("second legacy group", form.Receipt.Groups(1).Tag)
                            Equal(41, form.Receipt.Total, "Legacy duplicate groups still round once per rate")
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Private Sub AssertLabels(form As RegisterForm, expected As String())
        Equal(String.Join("|", expected), String.Join("|", form.GroupListView.Items.Cast(Of ListViewItem)().Select(Function(item) item.Text)))
    End Sub

    Private Sub AssertSaved(receipt As Receipt, mode As String, taxType As Integer, total As Integer)
        Equal(2, receipt.Groups.Count)
        Equal("1,1", String.Join(",", receipt.Groups.Select(Function(g) g.GroupClassification.Code)))
        Equal("4,5", String.Join(",", receipt.Groups.Select(Function(g) g.TaxRate.Code)))
        Equal("1,2", String.Join(",", receipt.Groups.Select(Function(g) g.Details.Count)))
        Equal("eight,ten one,ten two", String.Join(",", receipt.Groups.SelectMany(Function(g) g.Details).Select(Function(d) d.Name)))
        Equal(True, receipt.Groups.SelectMany(Function(g) g.Details).All(Function(d) d.Users.Count = 1 AndAlso d.Users(0).Code = 1))
        Equal(mode, receipt.TaxRoundingMode)
        Equal(taxType, receipt.TaxType)
        Equal(total, receipt.Total)
        Equal(0, receipt.Incomes)
        Equal(total, receipt.Expenses)
    End Sub

    Private Sub AddGroup(form As RegisterForm, classification As Integer, tax As Integer)
        ClearGroupSelection(form)
        form.GroupClassificationComboBox.SelectedValue = classification
        form.GroupTaxRateComboBox.SelectedValue = tax
        Equal(True, form.AddGroupButton.Enabled)
        form.AddGroupButton.PerformClick()
    End Sub

    Private Sub ClearGroupSelection(form As RegisterForm)
        form.GroupListView.SelectedItems.Clear()
        Application.DoEvents()
    End Sub

    Private Sub SelectGroup(form As RegisterForm, index As Integer)
        form.GroupListView.Select()
        ClearGroupSelection(form)
        form.GroupListView.Items(index).Selected = True
        Application.DoEvents()
    End Sub

    Private Sub AddDetail(form As RegisterForm, name As String, fee As Integer)
        form.DetailListView.SelectedItems.Clear()
        form.ItemNameComboBox.Text = name
        form.FeeTextBox.Text = fee.ToString()
        form.QuantityTextBox.Text = "1"
        form.DiscountFeeTextBox.Text = "0"
        form.DiscountRateTextBox.Text = "0"
        form.TaxTargetCheckBox.Checked = True
        form.UserListBox.ClearSelected()
        form.UserListBox.SelectedItem = form.UserListBox.Items.Cast(Of User)().Single(Function(u) u.Code = 1)
        Equal(True, form.AddDetailButton.Enabled)
        form.AddDetailButton.PerformClick()
    End Sub

    Private Sub InvokeAddGroup(form As RegisterForm)
        GetType(RegisterForm).GetMethod("AddGroupButton_Click", BindingFlags.Instance Or BindingFlags.NonPublic).
            Invoke(form, New Object() {form.AddGroupButton, EventArgs.Empty})
    End Sub

    Private Sub Seed(fixture As DatabaseFixture)
        fixture.Execute("UPDATE m_group_classification SET name='交際費' WHERE code=1;" &
            "INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(2,'other',2,1,0);" &
            "INSERT INTO m_tax_rate(code,rate,start_date) VALUES(6,12,'2030-01-01');")
    End Sub
End Module
