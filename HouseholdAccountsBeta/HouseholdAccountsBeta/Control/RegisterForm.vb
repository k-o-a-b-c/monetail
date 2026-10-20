Imports System.ComponentModel

Public Class RegisterForm

    Public Property DataAccess As DataAccess

    Public Property Receipt As Receipt

    Private ReloadRequiredValue As Boolean

    Public ReadOnly Property ReloadRequired As Boolean
        Get
            Return ReloadRequiredValue
        End Get
    End Property

    Private Property SelectedGroup As ReceiptGroup

    Private Property Detail As ReceiptDetail

    Public PaymentClassification As PaymentClassification

    Private InitializingReceiptControls As Boolean = True

    Private UpdatingTaxRoundingMode As Boolean

    Private TaxRoundingModeManuallyChanged As Boolean

    Private Sub RegisterForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        InitializingReceiptControls = True
        Dim isNewReceipt As Boolean = Receipt Is Nothing OrElse Receipt.Seq = 0
        If Receipt Is Nothing Then
            Receipt = New Receipt()
        End If
        SetTaxRoundingMode(If(isNewReceipt, "floor", Receipt.TaxRoundingMode))

        ShopNameComboBox.DataSource = DataAccess.ShopList()
        PaymentClassificationComboBox.DataSource = DataAccess.PaymentClassificationList

        If isNewReceipt Then
            ShopNameComboBox.SelectedIndex = -1
            PaymentClassificationComboBox.SelectedIndex = -1
            ReceiptDateTextBox.Value = DateTime.Today
            ReceiptTimeTextBox.Value = DateTime.Today
            CopyButton.Visible = False
            DeleteButton.Visible = False
        Else
            DataAccess.LoadReceiptChildData(Receipt)
            SetReceiptData()
            RegiserButton.Text = "更新"
        End If

        GroupTaxRateComboBox.DataSource = DataAccess.TaxRateList
        GroupClassificationComboBox.DataSource = DataAccess.GroupClassificationList
        GroupClassificationComboBox.SelectedIndex = -1

        SetGroupTaxRate()

        UserListBox.DataSource = DataAccess.UserList()
        UserListBox.Height = DetailListView.Top - UserListBox.Top

        Dim pointClassificationList As New List(Of PointClassification)
        pointClassificationList.Add(New PointClassification() With {.Code = -1, .Name = ""})
        pointClassificationList.AddRange(DataAccess.PointClassificationList())
        PointClassificationColumn.DataSource = pointClassificationList
        PointClassificationColumn.DisplayMember = "Name"
        PointClassificationColumn.ValueMember = "Code"

        InitializingReceiptControls = False
        UpdateState()

    End Sub

#Region "値の設定"

    Private Sub SetReceiptData()
        SeqLabel.Text = Receipt.Seq.ToString()
        ReceiptDateTextBox.Value = Receipt.Date
        ReceiptTimeTextBox.Value = Receipt.Date
        ShopNameComboBox.Text = Receipt.ShopName
        ShopLocationComboBox.DataSource = DataAccess.GetShopLocationList(ShopNameComboBox.Text)
        ShopLocationComboBox.Text = Receipt.ShopLocation
        TaxInclusiveRadioButton.Checked = (Receipt.TaxType = 0)
        TaxExclusiveRadioButton.Checked = (Receipt.TaxType = 1)
        PaymentClassificationComboBox.SelectedValue = Receipt.PaymentClassification.Code
        ReceiptTagTextBox.Text = Receipt.Tag
        UpdateState()

        SetGroupListView()
    End Sub

    Private Sub SetGroupListView()
        GroupListView.Items.Clear()
        For Each group As ReceiptGroup In Receipt.Groups
            Dim item As New ListViewItem(New String() {group.GroupClassification.Name, "", group.Tag})
            GroupListView.Items.Add(item)
        Next
        For i As Integer = 0 To GroupListView.Columns.Count - 1
            GroupListView.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize)
            Dim headerWidth As Integer = GroupListView.Columns(i).Width
            GroupListView.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent)
            Dim contentWidth As Integer = GroupListView.Columns(i).Width
            If headerWidth > contentWidth Then
                GroupListView.Columns(i).Width = headerWidth
            End If
        Next
    End Sub

    Private Sub SetDetailListView()
        DetailListView.Items.Clear()
        If SelectedGroup IsNot Nothing Then
            For Each Detail As ReceiptDetail In SelectedGroup.Details
                Dim item As New ListViewItem(New String() {Detail.Name, Detail.Fee.ToString(), Detail.Tag})
                DetailListView.Items.Add(item)
            Next
        End If
        For i As Integer = 0 To DetailListView.Columns.Count - 1
            DetailListView.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize)
            Dim headerWidth As Integer = DetailListView.Columns(i).Width
            DetailListView.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent)
            Dim contentWidth As Integer = DetailListView.Columns(i).Width
            If headerWidth > contentWidth Then
                DetailListView.Columns(i).Width = headerWidth
            End If
        Next
    End Sub

    Private Sub UpdateState()

        If InitializingReceiptControls OrElse Receipt Is Nothing Then
            Return
        End If

        If TaxInclusiveRadioButton.Checked OrElse TaxExclusiveRadioButton.Checked Then
            TotalTextBox.Text = Receipt.GetTotal(If(TaxExclusiveRadioButton.Checked, 1, 0), SelectedTaxRoundingMode).ToString()
        Else
            TotalTextBox.Text = String.Empty
        End If
        TotalTextBox2.Text = TotalTextBox.Text

        Dim registerCheck As Boolean = True
        registerCheck = registerCheck AndAlso Not String.IsNullOrEmpty(ShopNameComboBox.Text)
        registerCheck = registerCheck AndAlso (TaxInclusiveRadioButton.Checked OrElse TaxExclusiveRadioButton.Checked)
        registerCheck = registerCheck AndAlso (PaymentClassificationComboBox.SelectedIndex <> -1)
        registerCheck = registerCheck AndAlso Receipt.Groups.Any(Function(g) g.Details.Count > 0)

        RegiserButton.Enabled = registerCheck

    End Sub

#End Region

#Region "入力支援"

    Private Sub ShopNameComboBox_SelectionChangeCommitted(sender As Object, e As EventArgs) Handles ShopNameComboBox.SelectionChangeCommitted
        ApplyShopTaxRoundingMode()
    End Sub

    Private Sub ApplyShopTaxRoundingMode()
        If InitializingReceiptControls OrElse Receipt Is Nothing OrElse Receipt.Seq > 0 OrElse TaxRoundingModeManuallyChanged Then
            Return
        End If

        '登録済み店舗・履歴候補・自由入力のいずれも、店名に対応する直近レシートを初期値に使う。
        SetTaxRoundingMode(DataAccess.GetTaxRoundingMode(ShopNameComboBox.Text))
    End Sub

    Private Sub ShopNameComboBox_TextUpdate(sender As Object, e As EventArgs) Handles ShopNameComboBox.TextUpdate
        If InitializingReceiptControls OrElse Receipt Is Nothing OrElse Receipt.Seq > 0 OrElse TaxRoundingModeManuallyChanged Then
            Return
        End If

        '入力中は直前の店舗設定を引き継がず、入力確定時にだけ履歴を参照する。
        SetTaxRoundingMode("floor")
    End Sub

    Private Sub ShopNameComboBox_Validated(sender As Object, e As EventArgs) Handles ShopNameComboBox.Validated
        If InitializingReceiptControls OrElse Receipt Is Nothing Then
            Return
        End If

        ApplyShopTaxRoundingMode()

        'Location
        Dim beforeLocation As String = ShopLocationComboBox.Text
        ShopLocationComboBox.DataSource = DataAccess.GetShopLocationList(ShopNameComboBox.Text)
        If Receipt.Seq > 0 OrElse Not String.IsNullOrEmpty(beforeLocation) Then
            ShopLocationComboBox.Text = beforeLocation
        End If

        'TaxType
        If Not TaxInclusiveRadioButton.Checked AndAlso Not TaxExclusiveRadioButton.Checked Then
            Dim taxType As Integer = DataAccess.GetTaxType(ShopNameComboBox.Text)
            If taxType = 0 Then
                TaxInclusiveRadioButton.Checked = True
            ElseIf taxType = 1 Then
                TaxExclusiveRadioButton.Checked = True
            End If
        End If

        'PaymentClassification
        If PaymentClassificationComboBox.SelectedIndex = -1 Then
            Dim paymentClassificationCode As Integer = DataAccess.GetPaymentClassification(ShopNameComboBox.Text)
            If paymentClassificationCode > 0 Then
                PaymentClassificationComboBox.SelectedValue = paymentClassificationCode
            End If
        End If

        'GroupClassification
        If Receipt.Groups.Count = 0 Then
            Dim groupClassificationCode As Integer
            If ShopNameComboBox.SelectedItem Is Nothing Then
                groupClassificationCode = DataAccess.GetDefaultGroupClassification(ShopNameComboBox.Text)
            Else
                groupClassificationCode = DirectCast(ShopNameComboBox.SelectedItem, Shop).DefaultGroupClassificationCode
            End If
            If groupClassificationCode > 0 Then
                GroupClassificationComboBox.SelectedValue = groupClassificationCode
            End If
        End If

        UpdateState()

    End Sub

    Private Sub ReceiptDateTextBox_TextChanged(sender As Object, e As EventArgs) Handles ReceiptDateTextBox.TextChanged
        SetGroupTaxRate()
    End Sub

    Private Sub SetGroupTaxRate()
        If GroupClassificationComboBox.DataSource Is Nothing Then
            Return
        End If
        If ReceiptDateTextBox.Value.CompareTo(New Date(2019, 10, 1)) >= 0 AndAlso (GroupClassificationComboBox.SelectedItem Is Nothing OrElse Not DirectCast(GroupClassificationComboBox.SelectedItem, GroupClassification).ReducedTaxTarget) Then
            GroupTaxRateComboBox.SelectedIndex = 0
        Else
            GroupTaxRateComboBox.SelectedIndex = 1
        End If
    End Sub

#End Region

#Region "再計算"

    Private ReadOnly Property SelectedTaxRoundingMode As String
        Get
            Return If(TaxRoundingHalfUpRadioButton.Checked, "half_up", "floor")
        End Get
    End Property

    Private Sub SetTaxRoundingMode(ByVal mode As String)
        Calculation.ValidateTaxRoundingMode(mode)
        UpdatingTaxRoundingMode = True
        Try
            TaxRoundingFloorRadioButton.Checked = (mode = "floor")
            TaxRoundingHalfUpRadioButton.Checked = (mode = "half_up")
        Finally
            UpdatingTaxRoundingMode = False
        End Try
        UpdateState()
    End Sub

    Private Sub TaxRoundingRadioButton_CheckedChanged(sender As Object, e As EventArgs) Handles TaxRoundingFloorRadioButton.CheckedChanged, TaxRoundingHalfUpRadioButton.CheckedChanged, TaxRoundingFloorRadioButton.Click, TaxRoundingHalfUpRadioButton.Click
        If InitializingReceiptControls OrElse UpdatingTaxRoundingMode OrElse Receipt Is Nothing Then
            Return
        End If
        Dim selectedRadioButton As RadioButton = TryCast(sender, RadioButton)
        If selectedRadioButton Is Nothing OrElse Not selectedRadioButton.Checked Then
            Return
        End If

        TaxRoundingModeManuallyChanged = True
        UpdateState()
    End Sub

    Private Sub TaxRadioButton_CheckedChanged(sender As Object, e As EventArgs) Handles TaxInclusiveRadioButton.CheckedChanged, TaxExclusiveRadioButton.CheckedChanged
        If InitializingReceiptControls OrElse Receipt Is Nothing Then
            Return
        End If
        UpdateState()
    End Sub

    Private Sub PaymentClassificationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles PaymentClassificationComboBox.SelectedIndexChanged
        UpdateState()
    End Sub

#End Region

#Region "Group"

    Private Sub UpdateAddGroupButton()

        If SelectedGroup Is Nothing Then
            AddGroupButton.Text = "追加"
            If GroupClassificationComboBox.SelectedIndex = -1 OrElse Receipt.Groups.Any(Function(g) g.GroupClassification.Code = DirectCast(GroupClassificationComboBox.SelectedValue, Integer)) Then
                AddGroupButton.Enabled = False
            Else
                AddGroupButton.Enabled = True
            End If
            GroupTagTextBox.Enabled = True
        ElseIf GroupClassificationComboBox.SelectedValue IsNot Nothing Then
            If Receipt.Groups.Any(Function(g) g.GroupClassification.Code = DirectCast(GroupClassificationComboBox.SelectedValue, Integer)) AndAlso SelectedGroup.GroupClassification.Code <> DirectCast(GroupClassificationComboBox.SelectedValue, Integer) Then
                AddGroupButton.Text = "統合"
                GroupTagTextBox.Enabled = False
            Else
                AddGroupButton.Text = "更新"
                GroupTagTextBox.Enabled = True
            End If
            AddGroupButton.Enabled = True
        End If

        UpdateState()

    End Sub

    Private Sub GroupClassificationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles GroupClassificationComboBox.SelectedIndexChanged

        UpdateAddGroupButton()
        SetGroupTaxRate()

    End Sub

    Private Sub GroupListView_SelectedIndexChanged(sender As Object, e As EventArgs) Handles GroupListView.SelectedIndexChanged

        ClearDetailControl(True)

        If GroupListView.SelectedItems.Count = 0 Then
            SelectedGroup = Nothing
            RemoveGroupButton.Enabled = False
            DetailPanel.Enabled = False

            GroupTagTextBox.Text = String.Empty
        Else
            SelectedGroup = Receipt.Groups(GroupListView.SelectedIndices(0))
            RemoveGroupButton.Enabled = True
            DetailPanel.Enabled = True

            GroupClassificationComboBox.SelectedItem = SelectedGroup.GroupClassification
            GroupTaxRateComboBox.SelectedItem = SelectedGroup.TaxRate
            GroupTagTextBox.Text = SelectedGroup.Tag

            Dim ItemNameList As List(Of String) = DataAccess.GetItemNameList(ShopNameComboBox.Text, SelectedGroup.GroupClassification.Code)
            ItemNameList.Remove(GroupClassificationComboBox.Text)
            ItemNameList.Insert(0, GroupClassificationComboBox.Text)
            ItemNameComboBox.DataSource = ItemNameList
            If SelectedGroup.Details.Count = 0 Then
                ItemNameComboBox.Text = GroupClassificationComboBox.Text
            Else
                ItemNameComboBox.Text = String.Empty
            End If
        End If

        UpdateAddGroupButton()
        SetDetailListView()

    End Sub

    Private Sub AddGroupButton_Click(sender As Object, e As EventArgs) Handles AddGroupButton.Click

        Dim group As ReceiptGroup
        If SelectedGroup Is Nothing Then
            group = New ReceiptGroup() With {
                .GroupClassification = DirectCast(GroupClassificationComboBox.SelectedItem, GroupClassification),
                .TaxRate = DirectCast(GroupTaxRateComboBox.SelectedItem, TaxRate),
                .Tag = GroupTagTextBox.Text
            }
            Receipt.Groups.Add(group)
        ElseIf Receipt.Groups.Any(Function(g) g.GroupClassification.Code = DirectCast(GroupClassificationComboBox.SelectedValue, Integer)) AndAlso
           SelectedGroup.GroupClassification.Code <> DirectCast(GroupClassificationComboBox.SelectedValue, Integer) Then
            group = Receipt.Groups.Where(Function(g) g.GroupClassification.Code = DirectCast(GroupClassificationComboBox.SelectedValue, Integer)).First()
            group.Details.AddRange(SelectedGroup.Details)
            RemoveGroupButton.PerformClick()
        Else
            group = SelectedGroup
            group.GroupClassification = DirectCast(GroupClassificationComboBox.SelectedItem, GroupClassification)
            group.TaxRate = DirectCast(GroupTaxRateComboBox.SelectedItem, TaxRate)
            group.Tag = GroupTagTextBox.Text
        End If

        SetGroupListView()

        GroupListView.Items(Receipt.Groups.FindIndex(Function(g) g.GroupClassification.Code = group.GroupClassification.Code)).Selected = True
        TaxTargetCheckBox.Checked = group.GroupClassification.DefaultTaxTarget

        ItemNameComboBox.Select()
        ItemNameComboBox.SelectAll()

    End Sub

    Private Sub RemoveGroupButton_Click(sender As Object, e As EventArgs) Handles RemoveGroupButton.Click

        Dim selectedIndex As Integer = GroupListView.SelectedIndices.Item(0)
        Receipt.Groups.RemoveAt(selectedIndex)
        GroupListView.Items.RemoveAt(selectedIndex)

        GroupClassificationComboBox.Select()

        UpdateState()

    End Sub

#End Region

#Region "Detail"

    Private Sub ClearDetailControl(ByVal isAllClear As Boolean)
        ItemNameComboBox.Text = String.Empty
        FeeTextBox.Text = String.Empty
        QuantityTextBox.Text = "1"
        DiscountFeeTextBox.Text = String.Empty
        If isAllClear Then
            DiscountRateTextBox.Text = String.Empty
        End If
        DetailTagTextBox.Text = String.Empty
        If isAllClear Then
            TaxTargetCheckBox.Checked = True
        End If
        DetailListView.SelectedItems.Clear()
        RemoveDetailButton.Enabled = False
    End Sub

    Private Sub ItemNameComboBox_TextChanged(sender As Object, e As EventArgs) Handles ItemNameComboBox.TextChanged
        AddDetailButton.Enabled = Not String.IsNullOrEmpty(ItemNameComboBox.Text)
    End Sub

    Private Sub DetailListView_SelectedIndexChanged(sender As Object, e As EventArgs) Handles DetailListView.SelectedIndexChanged

        If DetailListView.SelectedItems.Count = 0 Then
            Detail = Nothing
            RemoveDetailButton.Enabled = False

            AddDetailButton.Text = "追加"
        Else
            Detail = SelectedGroup.Details(DetailListView.SelectedIndices(0))
            RemoveDetailButton.Enabled = True

            ItemNameComboBox.Text = Detail.Name
            FeeTextBox.Text = Detail.Fee.ToString()
            QuantityTextBox.Text = Detail.Quantity.ToString()
            DiscountFeeTextBox.Text = Detail.DiscountFee.ToString()
            DiscountRateTextBox.Text = Detail.DiscountRate.ToString()
            DetailTagTextBox.Text = Detail.Tag
            TaxTargetCheckBox.Checked = Detail.TaxTarget
            UserListBox.ClearSelected()
            Detail.Users.ForEach(Sub(u) UserListBox.SelectedItem = u)

            AddDetailButton.Text = "更新"
        End If

    End Sub

    Private Function NormalizeQuantity() As Integer
        Dim value As Integer
        If Not Integer.TryParse(QuantityTextBox.Text, value) OrElse value < 1 Then value = 1
        QuantityTextBox.Text = value.ToString()
        Return value
    End Function

    Private Function NormalizeDiscountRate() As Double
        Dim value As Double
        If Not Double.TryParse(DiscountRateTextBox.Text, value) OrElse Double.IsNaN(value) OrElse Double.IsInfinity(value) Then
            value = 0
        Else
            value = Math.Min(100, Math.Max(0, value))
        End If
        DiscountRateTextBox.Text = value.ToString()
        Return value
    End Function

    Private Sub QuantityTextBox_Validated(sender As Object, e As EventArgs) Handles QuantityTextBox.Validated
        NormalizeQuantity()
    End Sub

    Private Sub DiscountRateTextBox_Validated(sender As Object, e As EventArgs) Handles DiscountRateTextBox.Validated
        NormalizeDiscountRate()
    End Sub

    Private Sub AddDetailButton_Click(sender As Object, e As EventArgs) Handles AddDetailButton.Click

        Dim quantity As Integer = NormalizeQuantity()
        Dim discountRate As Double = NormalizeDiscountRate()

        Dim detail As ReceiptDetail
        If DetailListView.SelectedItems.Count = 0 Then
            detail = New ReceiptDetail()
        Else
            detail = SelectedGroup.Details(DetailListView.SelectedIndices(0))
            AddDetailButton.Text = "追加"
        End If

        detail.Name = ItemNameComboBox.Text
        Integer.TryParse(FeeTextBox.Text, detail.Fee)
        detail.Quantity = quantity
        Integer.TryParse(DiscountFeeTextBox.Text, detail.DiscountFee)
        detail.DiscountRate = discountRate
        detail.Tag = DetailTagTextBox.Text
        detail.TaxTarget = TaxTargetCheckBox.Checked
        detail.Users.Clear()
        detail.Users.AddRange(UserListBox.SelectedItems.Cast(Of User))

        If DetailListView.SelectedItems.Count = 0 Then
            SelectedGroup.Details.Add(detail)
        End If

        SetDetailListView()
        UpdateState()

        ClearDetailControl(False)

        ItemNameComboBox.Select()

    End Sub

    Private Sub RemoveDetailButton_Click(sender As Object, e As EventArgs) Handles RemoveDetailButton.Click

        Dim selectedIndex As Integer = DetailListView.SelectedIndices.Item(0)
        SelectedGroup.Details.RemoveAt(selectedIndex)
        DetailListView.Items.RemoveAt(selectedIndex)

        UpdateState()

    End Sub

#End Region

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Me.Close()
    End Sub

    Private Sub RegisertButton_Click(sender As Object, e As EventArgs) Handles RegiserButton.Click
        RegisterReceipt(False)
    End Sub

    Private Sub CopyButton_Click(sender As Object, e As EventArgs) Handles CopyButton.Click
        RegisterReceipt(True)
    End Sub

    Private Sub RegisterReceipt(ByVal isCopy As Boolean)

        If ReceiptTimeTextBox.Value.TimeOfDay = TimeSpan.FromTicks(0) AndAlso MessageBox.Show("時刻を設定していません。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
            Return
        End If

        If isCopy Then
            If MessageBox.Show("流用登録します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) = DialogResult.No Then
                Return
            End If
            Receipt.Seq = 0
        Else
            If Receipt.Seq <> 0 Then
                If MessageBox.Show("登録します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) = DialogResult.No Then
                    Return
                End If
                If Receipt.Date.Date <> ReceiptDateTextBox.Value.Date AndAlso MessageBox.Show("日付が変更されています。本当によろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
                    Return
                End If
            End If
        End If

        Receipt.Date = ReceiptDateTextBox.Value.Date + New TimeSpan(ReceiptTimeTextBox.Value.TimeOfDay.Hours, ReceiptTimeTextBox.Value.TimeOfDay.Minutes, ReceiptTimeTextBox.Value.TimeOfDay.Seconds)
        Receipt.ShopName = ShopNameComboBox.Text
        Receipt.ShopLocation = ShopLocationComboBox.Text
        Receipt.TaxType = If(TaxExclusiveRadioButton.Checked, 1, 0)
        Receipt.TaxRoundingMode = SelectedTaxRoundingMode
        Receipt.PaymentClassification = DataAccess.PaymentClassification(DirectCast(PaymentClassificationComboBox.SelectedValue, Integer))
        Receipt.Tag = ReceiptTagTextBox.Text

        Try
            If Receipt.Seq = 0 Then
                DataAccess.RegisterReceipt(Receipt)
                PaymentClassification = DirectCast(PaymentClassificationComboBox.SelectedItem, PaymentClassification)
            Else
                DataAccess.UpdateReceipt(Receipt)
                PaymentClassification = Nothing
            End If
        Catch ex As System.Data.DBConcurrencyException
            ReloadRequiredValue = True
            MessageBox.Show(ex.Message, "更新の競合", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try


        Me.DialogResult = DialogResult.OK

        Me.Close()

    End Sub

    Private Sub DeleteButton_Click(sender As Object, e As EventArgs) Handles DeleteButton.Click

        If MessageBox.Show("削除します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
            Return
        End If

        Try
            DataAccess.DeleteReceipt(Receipt)
        Catch ex As System.Data.DBConcurrencyException
            ReloadRequiredValue = True
            MessageBox.Show(ex.Message, "更新の競合", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try

        Me.DialogResult = DialogResult.OK

        Me.Close()

    End Sub

    Private Sub RegisterForm_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        Select Case e.KeyData
            Case Keys.Enter Or Keys.Control
                RegiserButton.PerformClick()
                e.SuppressKeyPress = True
            Case Keys.T Or Keys.Control
                ReceiptTagTextBox.Focus()
                e.Handled = True
            Case Keys.G Or Keys.Control
                GroupListView.SelectedItems.Clear()
                GroupClassificationComboBox.Focus()
        End Select
        Select Case e.KeyCode
            Case Keys.Escape
                'Me.Close()
        End Select
    End Sub

    Private Sub FeeTextBox_Validated(sender As Object, e As EventArgs) Handles FeeTextBox.Validated
        Dim inputString As String = FeeTextBox.Text
        Dim index As Integer = 0
        Dim startIndex As Integer
        Dim tempValue As Integer
        Dim parseValue As Integer
        Dim minusSign As Boolean

        inputString = inputString.Replace(" ", "")
        While index < inputString.Length
            Select Case inputString.Chars(index)
                Case "0"c, "1"c, "2"c, "3"c, "4"c, "5"c, "6"c, "7"c, "8"c, "9"c
                Case "+"c, "-"c
                    If startIndex <> index Then
                        If Not Integer.TryParse(inputString.Substring(startIndex, index - startIndex), parseValue) Then
                            Return
                        End If
                        If Not minusSign Then
                            tempValue += parseValue
                        Else
                            tempValue -= parseValue
                        End If
                    End If
                    startIndex = index + 1
                    minusSign = inputString.Chars(index) = "-"c

                Case Else
                    Return
            End Select
            index += 1
        End While

        If startIndex <> index Then
            If Not Integer.TryParse(inputString.Substring(startIndex, index - startIndex), parseValue) Then
                Return
            End If
            If Not minusSign Then
                tempValue += parseValue
            Else
                tempValue -= parseValue
            End If
        End If

        FeeTextBox.Text = tempValue.ToString()

    End Sub

#Region "ポイント"

    'Private Sub PointDataGridView_CellValueChanged(sender As Object, e As DataGridViewCellEventArgs) Handles PointDataGridView.CellValueChanged
    '    If e.RowIndex = -1 Then
    '        Return
    '    End If
    '    Select Case e.ColumnIndex
    '        Case PointClassificationColumn.Index
    '            If PointDataGridView.Rows(e.RowIndex).Cells(e.ColumnIndex).Value Is Nothing Then
    '                PointDataGridView.Rows(e.RowIndex).Cells(PointRateColumn.Index).Value = ""
    '            Else
    '                Dim pointClassificationCode As Integer = DirectCast(PointDataGridView.Rows(e.RowIndex).Cells(e.ColumnIndex).Value, Integer)
    '                PointDataGridView.Rows(e.RowIndex).Cells(PointRateColumn.Index).Value = DataAccess.PointClassificationList.First(Function(a) a.Code = pointClassificationCode).DefaultRate
    '            End If
    '        Case PointRateColumn.Index, FixedPointsColumn.Index
    '            If PointDataGridView.Rows(e.RowIndex).Cells(PointRateColumn.Index).Value Is Nothing Then
    '            Else
    '                Dim pointRate As Double
    '                Dim fixedPoint As Integer
    '                If PointDataGridView.Rows(e.RowIndex).Cells(PointRateColumn.Index).Value Is Nothing OrElse
    '                   Not Double.TryParse(PointDataGridView.Rows(e.RowIndex).Cells(PointRateColumn.Index).Value.ToString(), pointRate) Then
    '                    pointRate = 0
    '                End If
    '                If PointDataGridView.Rows(e.RowIndex).Cells(FixedPointsColumn.Index).Value Is Nothing OrElse
    '                   Not Integer.TryParse(PointDataGridView.Rows(e.RowIndex).Cells(FixedPointsColumn.Index).Value.ToString(), fixedPoint) Then
    '                    fixedPoint = 0
    '                End If
    '                PointDataGridView.Rows(e.RowIndex).Cells(TotalPointColumn.Index).Value = Receipt.TotalWithoutTax * pointRate / 100 + fixedPoint
    '            End If
    '    End Select

    'End Sub


#End Region

End Class
