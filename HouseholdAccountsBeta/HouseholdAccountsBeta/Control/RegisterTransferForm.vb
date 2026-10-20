Public Class RegisterTransferForm

    Public Property DataAccess As DataAccess

    Public Property Transfer As Transfer

    Private ReloadRequiredValue As Boolean

    Public ReadOnly Property ReloadRequired As Boolean
        Get
            Return ReloadRequiredValue
        End Get
    End Property

    Public Property FromPaymentClassification As PaymentClassification
    Public Property ToPaymentClassification As PaymentClassification

    Private Sub TransferForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        NameComboBox.DataSource = DataAccess.GetTransferNameList()
        FromPaymentClassificationComboBox.DataSource = DataAccess.PaymentClassificationList
        ToPaymentClassificationComboBox.DataSource = DataAccess.PaymentClassificationList.ToList()

        If Transfer Is Nothing Then
            Transfer = New Transfer()
            NameComboBox.SelectedIndex = -1
            FromPaymentClassificationComboBox.SelectedIndex = -1
            ToPaymentClassificationComboBox.SelectedIndex = -1
            TransferDateTextBox.Value = DateTime.Today
            TransferTimeTextBox.Value = DateTime.Today
            CopyButton.Visible = False
            DeleteButton.Visible = False
        Else
            SetTransferData()
            RegiserButton.Text = "更新"
        End If
        UpdateState()

    End Sub

#Region "値の設定"

    Private Sub SetTransferData()
        SeqLabel.Text = Transfer.Seq.ToString()
        TransferDateTextBox.Value = Transfer.Date
        TransferTimeTextBox.Value = Transfer.Date
        NameComboBox.Text = Transfer.Name
        FeeTextBox.Text = Transfer.Fee.ToString()
        FromPaymentClassificationComboBox.SelectedValue = Transfer.FromPaymentClassification.Code
        ToPaymentClassificationComboBox.SelectedValue = Transfer.ToPaymentClassification.Code
        TransferTagTextBox.Text = Transfer.Tag

        FromPaymentClassification = DirectCast(FromPaymentClassificationComboBox.SelectedItem, PaymentClassification)
        ToPaymentClassification = DirectCast(ToPaymentClassificationComboBox.SelectedItem, PaymentClassification)
    End Sub

    Private Function TryGetTransferInput(ByRef fee As Integer) As Boolean
        ' 登録・流用のボタン状態と、実行直前の検証を同じ条件にする。
        Return Not String.IsNullOrEmpty(NameComboBox.Text) AndAlso
            TypeOf FromPaymentClassificationComboBox.SelectedItem Is PaymentClassification AndAlso
            TypeOf ToPaymentClassificationComboBox.SelectedItem Is PaymentClassification AndAlso
            Integer.TryParse(FeeTextBox.Text, fee)
    End Function

    Private Sub UpdateState()
        Dim fee As Integer
        Dim canRegister As Boolean = TryGetTransferInput(fee)
        RegiserButton.Enabled = canRegister
        CopyButton.Enabled = canRegister
    End Sub

#End Region

#Region "入力支援"

    Private Sub NameComboBox_Validated(sender As Object, e As EventArgs) Handles NameComboBox.Validated

        If FromPaymentClassificationComboBox.SelectedIndex = -1 Then
            Dim paymentClassificationCode As Integer = DataAccess.GetTransferFromPaymentClassification(NameComboBox.Text)
            If paymentClassificationCode > 0 Then
                FromPaymentClassificationComboBox.SelectedValue = paymentClassificationCode
            End If
        End If

    End Sub

#End Region

    Private Sub NameComboBox_TextChanged(sender As Object, e As EventArgs) Handles NameComboBox.TextChanged
        UpdateState()
    End Sub

    Private Sub FromPaymentClassificationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles FromPaymentClassificationComboBox.SelectedIndexChanged
        UpdateState()

        If ToPaymentClassificationComboBox.SelectedIndex = -1 AndAlso FromPaymentClassificationComboBox.SelectedIndex >= 0 Then
            Dim paymentClassificationCode As Integer = DataAccess.GetTransferToPaymentClassification(DirectCast(FromPaymentClassificationComboBox.SelectedValue, Integer), NameComboBox.Text)
            If paymentClassificationCode > 0 Then
                ToPaymentClassificationComboBox.SelectedValue = paymentClassificationCode
            End If
        End If

    End Sub

    Private Sub ToPaymentClassificationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ToPaymentClassificationComboBox.SelectedIndexChanged
        UpdateState()
    End Sub

    Private Sub FeeTextBox_TextChanged(sender As Object, e As EventArgs) Handles FeeTextBox.TextChanged
        UpdateState()
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        Me.Close()
    End Sub

    Private Sub RegiserButton_Click(sender As Object, e As EventArgs) Handles RegiserButton.Click
        RegisterTransfer(False)
    End Sub

    Private Sub CopyButton_Click(sender As Object, e As EventArgs) Handles CopyButton.Click
        RegisterTransfer(True)
    End Sub

    Public Sub RegisterTransfer(ByVal isCopy As Boolean)

        ' 確認ダイアログや流用のSeq変更より前に、直接呼出でも入力を確認する。
        Dim fee As Integer
        If Not TryGetTransferInput(fee) Then
            UpdateState()
            Return
        End If
        Dim fromPayment = DirectCast(FromPaymentClassificationComboBox.SelectedItem, PaymentClassification)
        Dim toPayment = DirectCast(ToPaymentClassificationComboBox.SelectedItem, PaymentClassification)

        If fromPayment.Code = toPayment.Code Then
            MessageBox.Show("移動元と移動先が同一です。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        If TransferTimeTextBox.Value.TimeOfDay = TimeSpan.FromTicks(0) AndAlso MessageBox.Show("時刻を設定していません。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
            Return
        End If

        If isCopy Then
            If MessageBox.Show("流用登録します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) = DialogResult.No Then
                Return
            End If
            Transfer.Seq = 0
        Else
            If Transfer.Seq <> 0 Then
                If MessageBox.Show("登録します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) = DialogResult.No Then
                    Return
                End If
                If Transfer.Date.Date <> TransferDateTextBox.Value.Date AndAlso MessageBox.Show("日付が変更されています。本当によろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
                    Return
                End If
            End If
        End If

        Transfer.Date = TransferDateTextBox.Value.Date + New TimeSpan(TransferTimeTextBox.Value.TimeOfDay.Hours, TransferTimeTextBox.Value.TimeOfDay.Minutes, TransferTimeTextBox.Value.TimeOfDay.Seconds)
        Transfer.Name = NameComboBox.Text
        Transfer.Fee = fee
        Transfer.FromPaymentClassification = DataAccess.PaymentClassification(fromPayment.Code)
        Transfer.ToPaymentClassification = DataAccess.PaymentClassification(toPayment.Code)
        Transfer.Tag = TransferTagTextBox.Text

        Try
            If Transfer.Seq = 0 Then
                DataAccess.RegisterTransfer(Transfer)
            Else
                DataAccess.UpdateTransfer(Transfer)
            End If
        Catch ex As System.Data.DBConcurrencyException
            ReloadRequiredValue = True
            MessageBox.Show(ex.Message, "更新の競合", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try

        FromPaymentClassification = DirectCast(FromPaymentClassificationComboBox.SelectedItem, PaymentClassification)
        ToPaymentClassification = DirectCast(ToPaymentClassificationComboBox.SelectedItem, PaymentClassification)

        Me.DialogResult = DialogResult.OK

        Me.Close()

    End Sub

    Private Sub DeleteButton_Click(sender As Object, e As EventArgs) Handles DeleteButton.Click

        If MessageBox.Show("削除します。よろしいですか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.No Then
            Return
        End If

        Try
            DataAccess.DeleteTransfer(Transfer)
        Catch ex As System.Data.DBConcurrencyException
            ReloadRequiredValue = True
            MessageBox.Show(ex.Message, "更新の競合", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try

        Me.DialogResult = DialogResult.OK

        Me.Close()

    End Sub

    Private Sub TransferForm_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
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