Imports System.ComponentModel
Imports System.Net.Security

Public Class RegisterBalanceForm

    Public Property DataAccess As DataAccess

    Public Property Balance As Balance

    Private ReloadRequiredValue As Boolean

    Public ReadOnly Property ReloadRequired As Boolean
        Get
            Return ReloadRequiredValue
        End Get
    End Property

    Private Sub RegisterBalanceForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If Balance Is Nothing Then
            Balance = New Balance()
            BalanceDateTextBox.Value = DateTime.Today
            For Each Payment As PaymentClassification In DataAccess.PaymentClassificationList
                Dim values As Object() = {Payment, Nothing, Nothing, "反映", Nothing, String.Empty}
                BalanceDataGridView.Rows.Add(values)
            Next
            DeleteButton.Visible = False
        Else
            SeqLabel.Text = Balance.Seq.ToString()
            BalanceDateTextBox.Value = Balance.Date
            BalanceTagTextBox.Text = Balance.Tag
            For Each Payment As PaymentClassification In DataAccess.PaymentClassificationList
                Dim values As Object() = {Payment, Nothing, Nothing, "反映", Nothing, String.Empty}
                Dim registedBalanceGroup As BalanceGroup = Balance.Groups.FirstOrDefault(Function(g) g.PaymentClassification.Code = Payment.Code)
                If registedBalanceGroup IsNot Nothing Then
                    values(1) = registedBalanceGroup.Balance
                    values(4) = registedBalanceGroup.ValuationAmount
                    values(5) = registedBalanceGroup.Tag
                End If
                BalanceDataGridView.Rows.Add(values)
            Next
            RegiserButton.Text = "更新"
        End If

        FillAutoBalance()

        BalanceDataGridView.AutoResizeColumn(0)
        BalanceDataGridView.AutoResizeColumn(1)
        BalanceDataGridView.AutoResizeColumn(2)
        BalanceDataGridView.AutoResizeColumn(3)
        BalanceDataGridView.AutoResizeColumn(4)
    End Sub

    Private Sub RegisterBalanceForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        If BalanceDataGridView.Columns(1).Width < BalanceDataGridView.Columns(2).Width Then
            BalanceDataGridView.Columns(1).Width = BalanceDataGridView.Columns(2).Width
        End If
        If BalanceDataGridView.Columns(4).Width < BalanceDataGridView.Columns(2).Width Then
            BalanceDataGridView.Columns(4).Width = BalanceDataGridView.Columns(2).Width
        End If

        Dim tempWidth As Integer = BalanceDataGridView.Columns(5).Width
        BalanceDataGridView.AutoResizeColumn(5)
        If BalanceDataGridView.Columns(5).Width < tempWidth Then
            BalanceDataGridView.Columns(5).Width = tempWidth
        End If
        'Me.Height += BalanceDataGridView.ColumnHeadersHeight + BalanceDataGridView.Rows.GetRowsHeight(DataGridViewElementStates.Visible) - BalanceDataGridView.Height
        '        Debug.Print("" & Me.Height & " " & BalanceDataGridView.Rows.Cast(Of DataGridViewRow).Sum(Function(r) r.Height))
    End Sub

    Private Sub FillAutoBalance()
        For Each row As DataGridViewRow In BalanceDataGridView.Rows
            row.Cells.Item(AutoBalanceColumn.Index).Value = BalanceDisplay.AutoBalance(BalanceDateTextBox.Value, DirectCast(row.Cells.Item(PaymentClassificationColumn.Index).Value, PaymentClassification))
            SetRowColor(row.Index)
        Next
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Me.Close()
    End Sub

    Private Sub DeleteButton_Click(sender As Object, e As EventArgs) Handles DeleteButton.Click

        If MessageBox.Show("削除します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
            Return
        End If

        Try
            DataAccess.DeleteBalance(Balance)
        Catch ex As System.Data.DBConcurrencyException
            ReloadRequiredValue = True
            MessageBox.Show(ex.Message, "更新の競合", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try

        Me.DialogResult = DialogResult.OK

        Me.Close()

    End Sub

    Private Sub RegiserButton_Click(sender As Object, e As EventArgs) Handles RegiserButton.Click

        If DataAccess.ExistsBalance(BalanceDateTextBox.Value, Balance.Seq) Then
            MessageBox.Show("該当の日付にはデータが存在します。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        If Balance.Seq <> 0 Then
            If MessageBox.Show("登録します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) = DialogResult.No Then
                Return
            End If
            If Balance.Date.Date <> BalanceDateTextBox.Value.Date AndAlso MessageBox.Show("日付が変更されています。本当によろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
                Return
            End If
        End If

        Balance.Date = BalanceDateTextBox.Value.Date
        Balance.Tag = BalanceTagTextBox.Text
        Balance.Groups = New List(Of BalanceGroup)()
        For Each row As DataGridViewRow In BalanceDataGridView.Rows
            If row.Cells.Item(BalanceColumn.Index).Value Is Nothing Then
                If row.Cells.Item(ValuationAmountColumn.Index).Value IsNot Nothing Then
                    MessageBox.Show("評価額を登録する場合は残高の入力が必要です。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1)
                    Return
                End If
                Continue For
            End If

            Dim group As New BalanceGroup()
            group.PaymentClassification = DirectCast(row.Cells.Item(PaymentClassificationColumn.Index).Value, PaymentClassification)
            group.Balance = Integer.Parse(row.Cells.Item(BalanceColumn.Index).Value.ToString())
            If row.Cells.Item(ValuationAmountColumn.Index).Value Is Nothing Then
                group.ValuationAmount = Nothing
            Else
                group.ValuationAmount = Integer.Parse(row.Cells.Item(ValuationAmountColumn.Index).Value.ToString())
            End If
            If row.Cells.Item(TagColumn.Index).Value IsNot Nothing Then
                group.Tag = row.Cells.Item(TagColumn.Index).Value.ToString()
            Else
                group.Tag = String.Empty
            End If
            Balance.Groups.Add(group)
        Next

        If Balance.Groups.Count = 0 Then
            MessageBox.Show("残高情報がありません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1)
            Return
        End If

        Try
            If Balance.Seq = 0 Then
                DataAccess.RegisterBalance(Balance)
            Else
                DataAccess.UpdateBalance(Balance)
            End If
        Catch ex As System.Data.DBConcurrencyException
            ReloadRequiredValue = True
            MessageBox.Show(ex.Message, "更新の競合", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try

        Me.DialogResult = DialogResult.OK

        Me.Close()

    End Sub

    Private Sub BalanceDataGridView_CellContentClick(sender As Object, e As DataGridViewCellEventArgs) Handles BalanceDataGridView.CellContentClick
        '反映ボタン
        If BalanceDataGridView.Columns.Item(e.ColumnIndex) Is CopyAutoBalanceColumn AndAlso e.RowIndex >= 0 Then
            ' 範囲超過の表示文字列は、登録する残高へ転記しない。
            Dim value As Object = BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(AutoBalanceColumn.Index).Value
            If TypeOf value Is String Then Return
            BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(BalanceColumn.Index).Value = value
            SetRowColor(e.RowIndex)
        End If
    End Sub

    Private Sub BalanceDataGridView_CellValidated(sender As Object, e As DataGridViewCellEventArgs) Handles BalanceDataGridView.CellValidated
        '金額, 評価額
        If (BalanceDataGridView.Columns.Item(e.ColumnIndex) Is BalanceColumn OrElse
           BalanceDataGridView.Columns.Item(e.ColumnIndex) Is ValuationAmountColumn) AndAlso
           BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value IsNot Nothing Then
            Dim input As String = BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value.ToString()
            Dim inputNumber As Integer
            If Integer.TryParse(input, inputNumber) Then
                BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value = inputNumber
            Else
                BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value = Nothing
            End If
        End If

        SetRowColor(e.RowIndex)

    End Sub

    Private Sub SetRowColor(ByVal row As Integer)
        Dim cellStyle As New DataGridViewCellStyle()
        If BalanceDataGridView.Rows.Item(row).Cells.Item(BalanceColumn.Index).Value IsNot Nothing AndAlso
           BalanceDataGridView.Rows.Item(row).Cells.Item(AutoBalanceColumn.Index).Value IsNot Nothing AndAlso
           BalanceDataGridView.Rows.Item(row).Cells.Item(BalanceColumn.Index).Value.ToString() <> BalanceDataGridView.Rows.Item(row).Cells.Item(AutoBalanceColumn.Index).Value.ToString() Then
            cellStyle.BackColor = Color.MistyRose
        End If
        BalanceDataGridView.Rows.Item(row).DefaultCellStyle = cellStyle
    End Sub

    Private Sub BalanceDataGridView_CellEnter(sender As Object, e As DataGridViewCellEventArgs) Handles BalanceDataGridView.CellEnter
        '金額
        If BalanceDataGridView.Columns.Item(e.ColumnIndex) Is BalanceColumn OrElse BalanceDataGridView.Columns.Item(e.ColumnIndex) Is ValuationAmountColumn Then
            BalanceDataGridView.ImeMode = ImeMode.Disable
        End If
    End Sub

    Private Sub BalanceDateTextBox_Validated(sender As Object, e As EventArgs) Handles BalanceDateTextBox.Validated
        If DataAccess.ExistsBalance(BalanceDateTextBox.Value, Balance.Seq) Then
            MessageBox.Show("該当の日付にはデータが存在します。日付を変更してください。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
            RegiserButton.Enabled = False
        Else
            RegiserButton.Enabled = True
        End If
        FillAutoBalance()
    End Sub

    Private Sub BalanceDataGridView_CellBeginEdit(sender As Object, e As DataGridViewCellCancelEventArgs) Handles BalanceDataGridView.CellBeginEdit
        '金額, 評価額
        If (BalanceDataGridView.Columns.Item(e.ColumnIndex) Is BalanceColumn OrElse
           BalanceDataGridView.Columns.Item(e.ColumnIndex) Is ValuationAmountColumn) AndAlso
           BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value IsNot Nothing Then
            BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value = BalanceDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value.ToString().Replace(",", "")
        End If
    End Sub

    Private Sub RegisterBalanceForm_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        Select Case e.KeyData
            Case Keys.Enter Or Keys.Control
                RegiserButton.PerformClick()
                e.SuppressKeyPress = True
        End Select
        Select Case e.KeyCode
            Case Keys.Escape
                'Me.Close()
        End Select
    End Sub

End Class
