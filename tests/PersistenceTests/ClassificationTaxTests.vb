Imports System.Windows.Forms

Friend Module ClassificationTaxTests
    Public Sub StandardRateLabels()
        For Each enabled In {False, True}
            Using fixture As New DatabaseFixture(enabled)
                fixture.Execute("UPDATE t_receipt_details SET tax_code=4 WHERE group_seq=1")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                            Program.OpenForm(form)
                            TaxDefaultTests.SelectGroup(form, 0)
                            Equal("food", form.GroupListView.Items(0).Text)
                            Equal("item（8%）", form.DetailListView.Items(0).Text)
                            form.DetailListView.Items(0).Selected = True
                            Application.DoEvents()
                            form.FeeTextBox.Text = "pending"
                            form.ReceiptDateTextBox.Value = New Date(2018, 1, 1)
                            Equal("item", form.DetailListView.Items(0).Text)
                            Equal("pending", form.FeeTextBox.Text)
                            Equal(1, form.DetailListView.SelectedIndices.Count)
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub AdditionAndSelection()
        For Each enabled In {False, True}
            Using fixture As New DatabaseFixture(enabled)
                Using form As New RegisterForm With {.DataAccess = fixture.Data}
                    Program.OpenForm(form)
                    form.GroupClassificationComboBox.SelectedValue = 1
                    form.AddGroupButton.PerformClick()
                    AddLine(form, 4, "eight", 19)
                    AddLine(form, 5, "ten", 19)
                    AddLine(form, 0, "outside", 1000)
                    Equal(1, form.Receipt.Groups.Count)
                    Equal("4,5,0", String.Join(",", form.Receipt.Groups.Single().Details.Select(Function(d) d.TaxRate.Code)))
                    form.GroupListView.SelectedItems.Clear()
                    Application.DoEvents()
                    form.GroupClassificationComboBox.SelectedValue = 1
                    Equal(False, form.AddGroupButton.Enabled)
                    form.Close()
                End Using
            End Using
        Next
    End Sub

    Public Sub EditsAndMerges()
        For Each enabled In {False, True}
            Using fixture As New DatabaseFixture(enabled)
                fixture.Execute("UPDATE t_receipt_groups SET tag='same '")
                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                    Program.OpenForm(form)
                    Dim oldTaxCodes = String.Join(",", form.Receipt.Groups.SelectMany(Function(g) g.Details).Select(Function(d) d.TaxRate.Code))
                    TaxDefaultTests.SelectGroup(form, 1)
                    form.GroupClassificationComboBox.SelectedValue = 1
                    Equal("統合", form.AddGroupButton.Text)
                    form.AddGroupButton.PerformClick()
                    Equal(1, form.Receipt.Groups.Count)
                    Equal(2, form.Receipt.Groups.Single().Details.Count)
                    Equal(oldTaxCodes, String.Join(",", form.Receipt.Groups.Single().Details.Select(Function(d) d.TaxRate.Code)))
                    Equal("【統合元 1】" & vbLf & "same " & vbLf & "【統合元 2】" & vbLf & "same ", form.Receipt.Groups.Single().Tag)
                    form.Close()
                End Using
            End Using
        Next
    End Sub

    Public Sub SaveAndStatistics()
        For Each enabled In {False, True}
            For Each mode In {"floor", "half_up"}
                Using fixture As New DatabaseFixture(enabled)
                    Dim group = ReceiptFixtures.Group(8, Detail(19))
                    Dim ten = Detail(38)
                    group.Details.Add(ten)
                    Dim receipt = MakeReceipt(mode, group)
                    fixture.Data.RegisterReceipt(receipt)
                    Dim saved = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = receipt.Seq)
                    Equal(1, saved.Groups.Count)
                    Equal("4,5", String.Join(",", saved.Groups.Single().Details.Select(Function(d) d.TaxRate.Code)))
                    Equal(If(mode = "floor", 61, 63), saved.Total)
                    Equal(saved.Total, saved.GetGroupStatisticsList(New List(Of User), False, False).Single().Fee)
                    Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = saved}
                        Program.OpenForm(form)
                        Program.ClickAndAnswer(form.CopyButton, DialogResult.Yes, "Immediate copy keeps detail tax")
                        Equal(DialogResult.OK, form.DialogResult)
                    End Using
                    Equal(3, fixture.Data.GetReceipts(2026, 10).Count)
                End Using
            Next
        Next
    End Sub

    Public Sub MissingTaxAndLegacyGroups()
        For Each enabled In {False, True}
            Using fixture As New DatabaseFixture(enabled)
                ' 移行済みDBの制約も、同分類の別グループ登録を拒否する。
                Throws(Of System.Data.SQLite.SQLiteException)(Sub() fixture.Execute("INSERT INTO t_receipt_groups VALUES(1,99,1,NULL)"))
                Using form As New RegisterForm With {.DataAccess = fixture.Data}
                    Program.OpenForm(form)
                    form.GroupClassificationComboBox.SelectedValue = 1
                    form.AddGroupButton.PerformClick()
                    form.DetailTaxRateComboBox.SelectedIndex = -1
                    form.ItemNameComboBox.Text = "no tax"
                    Equal(False, form.AddDetailButton.Enabled)
                    Equal(0, form.Receipt.Groups.Single().Details.Count)
                    form.Close()
                End Using
            End Using
        Next
    End Sub

    Private Sub AddLine(form As RegisterForm, tax As Integer, name As String, fee As Integer)
        form.DetailListView.SelectedItems.Clear()
        form.DetailTaxRateComboBox.SelectedValue = tax
        form.ItemNameComboBox.Text = name
        form.FeeTextBox.Text = fee.ToString()
        form.AddDetailButton.PerformClick()
    End Sub
End Module
