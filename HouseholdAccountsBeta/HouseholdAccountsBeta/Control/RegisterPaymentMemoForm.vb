Imports System.ComponentModel
Imports System.Net.Security

Public Class RegisterPaymentMemoForm

    Public Property DataAccess As DataAccess

    Private UpdateDate As DateTime

    Private Sub RegisterBalanceForm_Load(sender As Object, e As EventArgs) Handles Me.Load
        UpdateDate = Date.Today
        For Each PaymentMemo As PaymentMemo In DataAccess.GetPaymentMemoList
            Dim values As Object() = {PaymentMemo.PaymentClassification, Calculation.AutoBalance(UpdateDate, PaymentMemo.PaymentClassification), PaymentMemo.CheckDate, "反映", PaymentMemo.Tag}
            SetRowColor(PaymentMemoDataGridView.Rows.Add(values))
        Next
        PaymentMemoDataGridView.AutoResizeColumn(0)
        PaymentMemoDataGridView.AutoResizeColumn(1)
        PaymentMemoDataGridView.AutoResizeColumn(2)
        PaymentMemoDataGridView.AutoResizeColumn(3)
    End Sub

    Private Sub RegisterPaymentMemoForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Dim tempWidth As Integer = PaymentMemoDataGridView.Columns(4).Width
        PaymentMemoDataGridView.AutoResizeColumn(4)
        If PaymentMemoDataGridView.Columns(4).Width < tempWidth Then
            PaymentMemoDataGridView.Columns(4).Width = tempWidth
        End If
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Me.Close()
    End Sub

    Private Sub PaymentMemoDataGridView_CellContentClick(sender As Object, e As DataGridViewCellEventArgs) Handles PaymentMemoDataGridView.CellContentClick
        '反映ボタン
        If PaymentMemoDataGridView.Columns.Item(e.ColumnIndex) Is SetTodayColumn AndAlso e.RowIndex >= 0 Then
            PaymentMemoDataGridView.Rows.Item(e.RowIndex).Cells.Item(CheckDateColumn.Index).Value = DateTime.Today
            SetRowColor(e.RowIndex)
        End If
    End Sub

    Private Sub PaymentMemoDataGridView_CellValidated(sender As Object, e As DataGridViewCellEventArgs) Handles PaymentMemoDataGridView.CellValidated
        '日付
        If PaymentMemoDataGridView.Columns.Item(e.ColumnIndex) Is CheckDateColumn AndAlso PaymentMemoDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value IsNot Nothing Then
            Dim input As String = PaymentMemoDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value.ToString()
            If Not String.IsNullOrEmpty(input) Then
                PaymentMemoDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value = OriginalControls.OriginalDateTimeTextBox.StringToDateValue(input)
            Else
                PaymentMemoDataGridView.Rows.Item(e.RowIndex).Cells.Item(e.ColumnIndex).Value = Nothing
            End If
        End If
        SetRowColor(e.RowIndex)
    End Sub

    Private Sub SetRowColor(ByVal row As Integer)
        Dim cellStyle As New DataGridViewCellStyle()
        If PaymentMemoDataGridView.Rows.Item(row).Cells.Item(CheckDateColumn.Index).Value Is Nothing OrElse
            DirectCast(PaymentMemoDataGridView.Rows.Item(row).Cells.Item(CheckDateColumn.Index).Value, DateTime) <> DateTime.Today Then
            cellStyle.BackColor = Color.MistyRose
        End If
        PaymentMemoDataGridView.Rows.Item(row).DefaultCellStyle = cellStyle
    End Sub

    Private Sub PaymentMemoDataGridView_CellEnter(sender As Object, e As DataGridViewCellEventArgs) Handles PaymentMemoDataGridView.CellEnter
        '日付
        If PaymentMemoDataGridView.Columns.Item(e.ColumnIndex) Is CheckDateColumn Then
            PaymentMemoDataGridView.ImeMode = ImeMode.Disable
        End If
    End Sub

    Private Sub RegisterPaymentMemoForm_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        Select Case e.KeyData
            Case Keys.Enter Or Keys.Control
                RegistPaymentMemo(True)
                e.SuppressKeyPress = True
        End Select
        Select Case e.KeyCode
            Case Keys.Escape
                'Me.Close()
        End Select
    End Sub

    Private Sub RegisterPaymentMemoForm_Disposed(sender As Object, e As EventArgs) Handles Me.Disposed
        ViewForm.Activate()
    End Sub

    Private Sub AlwaysTopCheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles TopMostCheckBox.CheckedChanged
        Me.TopMost = TopMostCheckBox.Checked
    End Sub

    Public Sub UpdateBalance(ByVal PaymentClassification As PaymentClassification)
        If UpdateDate = Date.Today AndAlso PaymentClassification IsNot Nothing Then
            For Each row As DataGridViewRow In PaymentMemoDataGridView.Rows
                If DirectCast(row.Cells.Item(PaymentClassificationColumn.Index).Value, PaymentClassification).Code = PaymentClassification.Code Then
                    row.Cells.Item(AutoBalanceColumn.Index).Value = Calculation.AutoBalance(UpdateDate, PaymentClassification)
                End If
            Next
        Else
            UpdateDate = Date.Today
            For Each row As DataGridViewRow In PaymentMemoDataGridView.Rows
                row.Cells.Item(AutoBalanceColumn.Index).Value = Calculation.AutoBalance(UpdateDate, DirectCast(row.Cells.Item(PaymentClassificationColumn.Index).Value, PaymentClassification))
            Next
        End If
    End Sub

    Private Sub RegisterPaymentMemoForm_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        Select Case MessageBox.Show("登録しますか？", "確認", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
            Case DialogResult.Yes
                RegistPaymentMemo(False)
            Case DialogResult.No
                'NOP
            Case DialogResult.Cancel
                e.Cancel = True
        End Select
    End Sub

    Private Sub RegistPaymentMemo(ByVal ShowMessage As Boolean)

        If ShowMessage AndAlso MessageBox.Show("登録しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) = DialogResult.No Then
            Return
        End If

        Dim PaymentMemoList = New List(Of PaymentMemo)()
        For Each row As DataGridViewRow In PaymentMemoDataGridView.Rows
            Dim PaymentMemo As New PaymentMemo()
            PaymentMemo.PaymentClassification = DirectCast(row.Cells.Item(PaymentClassificationColumn.Index).Value, PaymentClassification)
            If row.Cells.Item(CheckDateColumn.Index).Value IsNot Nothing Then
                PaymentMemo.CheckDate = DateTime.Parse(row.Cells.Item(CheckDateColumn.Index).Value.ToString())
            Else
                PaymentMemo.CheckDate = Nothing
            End If
            If row.Cells.Item(TagColumn.Index).Value IsNot Nothing Then
                PaymentMemo.Tag = row.Cells.Item(TagColumn.Index).Value.ToString()
            Else
                PaymentMemo.Tag = String.Empty
            End If
            PaymentMemoList.Add(PaymentMemo)
        Next

        DataAccess.RegisterPaymentMemo(PaymentMemoList)
    End Sub

End Class
