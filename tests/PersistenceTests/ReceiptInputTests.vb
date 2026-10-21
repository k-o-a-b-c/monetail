Imports System.Data.SQLite
Imports System.Linq
Imports System.Reflection

Friend Module ReceiptInputTests
    Public Sub InvalidInputsAndRecovery()
        For Each foreignKeys In New Boolean() {False, True}
            For Each editing In New Boolean() {False, True}
                For invalidCase As Integer = 0 To 4
                    Console.WriteLine("INFO receipt invalid input FK=" & foreignKeys & " editing=" & editing & " case=" & invalidCase)
                    Using fixture = Seed(foreignKeys)
                        Dim source = fixture.Data.GetReceipts(2026, 10).Single()
                        Dim saved = ConcurrencyTests.Snapshot(fixture)
                        Dim writes As Integer
                        Dim observe As SQLiteUpdateEventHandler = Sub(sender, args) writes += 1
                        AddHandler fixture.ActualDataConnection.Update, observe
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = If(editing, source, Nothing)}
                            Program.OpenForm(form)
                            SetHeader(form)
                            If Not editing Then AddItem(form)
                            Equal(True, form.RegiserButton.Enabled)
                            Equal(True, form.CopyButton.Enabled)
                            Select Case invalidCase
                                Case 0
                                    form.ShopNameComboBox.Text = ""
                                Case 1
                                    form.TaxInclusiveRadioButton.Checked = False
                                    form.TaxExclusiveRadioButton.Checked = False
                                Case 2
                                    form.PaymentClassificationComboBox.SelectedIndex = -1
                                Case 3
                                    For index As Integer = 0 To form.GroupListView.Items.Count - 1
                                        SelectGroup(form, index)
                                        For remaining As Integer = form.DetailListView.Items.Count - 1 To 0 Step -1
                                            form.DetailListView.Items(0).Selected = True
                                            Application.DoEvents()
                                            Equal(True, form.RemoveDetailButton.Enabled, "Detail deletion must be available")
                                            form.RemoveDetailButton.PerformClick()
                                            Equal(remaining, form.DetailListView.Items.Count, "Detail deletion must remove one item")
                                        Next
                                    Next
                                Case 4
                                    For remaining As Integer = form.GroupListView.Items.Count - 1 To 0 Step -1
                                        SelectGroup(form, 0)
                                        Equal(True, form.RemoveGroupButton.Enabled, "Group deletion must be available")
                                        form.RemoveGroupButton.PerformClick()
                                        Equal(remaining, form.GroupListView.Items.Count, "Group deletion must remove one item")
                                    Next
                            End Select
                            Equal(False, form.RegiserButton.Enabled)
                            Equal(False, form.CopyButton.Enabled)
                            Dim original = DetailInputTests.ReceiptState(form.Receipt)
                            Dim groups = form.Receipt.Groups
                            Dim groupReferences = groups.ToArray()
                            Dim detailReferences = groups.SelectMany(Function(g) g.Details).ToArray()
                            Dim payment = form.Receipt.PaymentClassification
                            Dim pending = InputState(form)
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Disabled incomplete receipt register", expectedConfirmations:=0)
                            Program.ClickAndAnswer(form.CopyButton, DialogResult.Yes, "Disabled incomplete receipt copy", expectedConfirmations:=0)
                            For Each copying In New Boolean() {False, True}
                                Dim isCopy = copying
                                ' ボタン状態を迂回しても、確認・Seq変更・ヘッダー転記より前に中止する。
                                form.RegiserButton.Enabled = True
                                form.CopyButton.Enabled = True
                                Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Direct incomplete receipt guard",
                                    expectedConfirmations:=0, invoke:=Sub() InvokeRegister(form, isCopy))
                                Equal(False, form.RegiserButton.Enabled)
                                Equal(False, form.CopyButton.Enabled)
                                Equal(True, form.Visible)
                                Equal(DialogResult.None, form.DialogResult)
                                Equal(False, form.ReloadRequired)
                                Equal(pending, InputState(form))
                                Equal(original, DetailInputTests.ReceiptState(form.Receipt))
                                Equal(True, Object.ReferenceEquals(groups, form.Receipt.Groups))
                                Equal(True, groupReferences.SequenceEqual(form.Receipt.Groups))
                                Equal(True, detailReferences.SequenceEqual(form.Receipt.Groups.SelectMany(Function(g) g.Details)))
                                Equal(True, Object.ReferenceEquals(payment, form.Receipt.PaymentClassification))
                                Equal(saved, ConcurrencyTests.Snapshot(fixture))
                            Next
                            RemoveHandler fixture.ActualDataConnection.Update, observe
                            Equal(0, writes, "Rejected receipt input must not issue writes")
                            SetHeader(form)
                            If Not form.Receipt.Groups.Any(Function(g) g.Details.Count > 0) Then AddItem(form)
                            Equal(True, form.RegiserButton.Enabled)
                            Equal(True, form.CopyButton.Enabled)
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Save corrected receipt", expectedConfirmations:=If(editing, 1, 0))
                            Equal(DialogResult.OK, form.DialogResult)
                            Dim loaded = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = form.Receipt.Seq)
                            Equal("pending unregistered shop", loaded.ShopName)
                            Equal("pending 'tag'", loaded.Tag)
                            Equal(2, loaded.PaymentClassification.Code)
                            Equal(True, loaded.Groups.Any(Function(g) g.Details.Count > 0))
                        End Using
                    End Using
                Next
            Next
        Next
    End Sub

    Public Sub ValidRegistrationAndImmediateCopy()
        For Each foreignKeys In New Boolean() {False, True}
            For operation As Integer = 0 To 2
                For Each mode In New String() {"floor", "half_up"}
                    For taxType As Integer = 0 To 1
                        Using fixture = Seed(foreignKeys)
                            Dim source = fixture.Data.GetReceipts(2026, 10).Single()
                            Dim saved = ConcurrencyTests.Snapshot(fixture)
                            Dim originalToken = source.RevisionToken
                            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = If(operation = 0, Nothing, source)}
                                Program.OpenForm(form)
                                SetHeader(form)
                                If operation = 0 Then
                                    AddItem(form)
                                    form.GroupListView.SelectedItems.Clear()
                                    Application.DoEvents()
                                    form.GroupClassificationComboBox.SelectedValue = 2
                                    form.AddGroupButton.PerformClick()
                                    Equal(2, form.Receipt.Groups.Count)
                                    Equal(0, form.Receipt.Groups(1).Details.Count)
                                End If
                                ' 店名の空白だけを新たに禁止せず、従来の必須条件を維持する。
                                Dim shop = If(taxType = 0 AndAlso mode = "floor", " ", "pending unregistered shop")
                                form.ShopNameComboBox.Text = shop
                                form.TaxInclusiveRadioButton.Checked = (taxType = 0)
                                form.TaxExclusiveRadioButton.Checked = (taxType = 1)
                                form.TaxRoundingFloorRadioButton.Checked = (mode = "floor")
                                form.TaxRoundingHalfUpRadioButton.Checked = (mode = "half_up")
                                Equal(True, form.RegiserButton.Enabled)
                                Equal(True, form.CopyButton.Enabled)
                                Dim expected As Integer = If(operation = 0, 19, 38)
                                If taxType = 1 Then expected += If(mode = "floor", If(operation = 0, 1, 3), If(operation = 0, 2, 4))
                                If operation = 2 Then
                                    Program.ClickAndAnswer(form.CopyButton, DialogResult.Yes, "Immediate valid receipt copy")
                                Else
                                    Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Valid receipt register", expectedConfirmations:=If(operation = 0, 0, 1))
                                End If
                                Equal(DialogResult.OK, form.DialogResult)
                                Equal(False, form.Visible)
                                Dim loaded = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = form.Receipt.Seq)
                                Equal(shop, loaded.ShopName)
                                Equal("pending location", loaded.ShopLocation)
                                Equal("pending 'tag'", loaded.Tag)
                                Equal(New Date(2026, 10, 16, 12, 0, 0), loaded.Date)
                                Equal(2, loaded.PaymentClassification.Code)
                                Equal(taxType, loaded.TaxType)
                                Equal(mode, loaded.TaxRoundingMode)
                                Equal(expected, loaded.Total)
                                Equal(CLng(expected), Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=" & loaded.Seq)))
                                Equal(If(operation = 0, 1, 2), loaded.Groups.Sum(Function(g) g.Details.Count))
                                If operation <> 1 Then
                                    Equal(2, loaded.Seq)
                                    Dim original = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                                    Equal(originalToken, original.RevisionToken)
                                    Equal(41, original.Total)
                                    Equal(saved, ConcurrencyTests.Snapshot(fixture, EntityKind.Receipt, loaded.Seq))
                                End If
                            End Using
                        End Using
                    Next
                Next
            Next
        Next
    End Sub

    Public Sub DeclinedConfirmations()
        For Each foreignKeys In New Boolean() {False, True}
            Using fixture = Seed(foreignKeys)
                Dim source = fixture.Data.GetReceipts(2026, 10).Single()
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = source}
                            Program.OpenForm(form)
                            Dim original = DetailInputTests.ReceiptState(source)
                            Dim groups = source.Groups
                            Dim details = groups.SelectMany(Function(g) g.Details).ToArray()
                            SetHeader(form)
                            form.TaxInclusiveRadioButton.Checked = True
                            form.TaxRoundingHalfUpRadioButton.Checked = True
                            Dim pending = InputState(form)
                            Program.ClickAndAnswer(form.CopyButton, DialogResult.No, "Decline validated receipt copy")
                            Equal(pending, InputState(form))
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.No, "Decline validated receipt update")
                            Equal(pending, InputState(form))
                            form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16)
                            pending = InputState(form)
                            Program.ClickAndAnswer(form.CopyButton, DialogResult.No, "Decline receipt time confirmation")
                            Equal(pending, InputState(form))
                            form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                            form.ReceiptDateTextBox.Value = New Date(2026, 10, 17)
                            pending = InputState(form)
                            Program.ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Decline changed receipt date", expectedConfirmations:=2)
                            Equal(pending, InputState(form))
                            Equal(original, DetailInputTests.ReceiptState(source))
                            Equal(True, Object.ReferenceEquals(groups, source.Groups))
                            Equal(True, details.SequenceEqual(source.Groups.SelectMany(Function(g) g.Details)))
                            Equal(True, form.Visible)
                            Equal(DialogResult.None, form.DialogResult)
                            Equal(False, form.ReloadRequired)
                        End Using
                    End Sub)
            End Using
        Next
    End Sub

    Private Function Seed(foreignKeys As Boolean) As DatabaseFixture
        Dim fixture As New DatabaseFixture(foreignKeys)
        Try
            fixture.Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'bank',2,1);" &
                "INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(2,'other',2,1,0)")
            Return fixture
        Catch
            fixture.Dispose()
            Throw
        End Try
    End Function

    Private Sub SetHeader(form As RegisterForm)
        form.ShopNameComboBox.Text = "pending unregistered shop"
        form.ShopLocationComboBox.Text = "pending location"
        form.ReceiptDateTextBox.Value = New Date(2026, 10, 16)
        form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
        form.TaxExclusiveRadioButton.Checked = True
        form.PaymentClassificationComboBox.SelectedValue = 2
        form.ReceiptTagTextBox.Text = "pending 'tag'"
    End Sub

    Private Sub AddItem(form As RegisterForm)
        If form.GroupListView.Items.Count = 0 Then
            form.GroupClassificationComboBox.SelectedValue = 1
            form.AddGroupButton.PerformClick()
        Else
            SelectGroup(form, 0)
        End If
        form.ItemNameComboBox.Text = "new item"
        form.FeeTextBox.Text = "19"
        form.QuantityTextBox.Text = "1"
        form.DiscountFeeTextBox.Text = "0"
        form.DiscountRateTextBox.Text = "0"
        form.TaxTargetCheckBox.Checked = True
        form.AddDetailButton.PerformClick()
        Equal(True, form.Receipt.Groups.Any(Function(g) g.Details.Count > 0))
    End Sub

    Private Sub SelectGroup(form As RegisterForm, index As Integer)
        form.GroupListView.SelectedItems.Clear()
        form.GroupListView.Items(index).Selected = True
        Application.DoEvents()
    End Sub

    Private Sub InvokeRegister(form As RegisterForm, isCopy As Boolean)
        GetType(RegisterForm).GetMethod("RegisterReceipt", BindingFlags.NonPublic Or BindingFlags.Instance).
            Invoke(form, New Object() {isCopy})
    End Sub

    Private Function InputState(form As RegisterForm) As String
        Return String.Join("|", form.ShopNameComboBox.Text, form.ShopLocationComboBox.Text,
            form.ReceiptDateTextBox.Value.Date.ToString("O"), form.ReceiptTimeTextBox.Value.TimeOfDay.ToString(),
            form.TaxInclusiveRadioButton.Checked, form.TaxExclusiveRadioButton.Checked,
            form.TaxRoundingFloorRadioButton.Checked, form.TaxRoundingHalfUpRadioButton.Checked,
            form.PaymentClassificationComboBox.SelectedIndex, form.ReceiptTagTextBox.Text,
            form.TotalTextBox.Text, form.TotalTextBox2.Text,
            String.Join(",", form.GroupListView.SelectedIndices.Cast(Of Integer)()),
            String.Join(",", form.DetailListView.SelectedIndices.Cast(Of Integer)()))
    End Function
End Module
