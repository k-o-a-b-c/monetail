Partial Public Class ViewForm

    Private BalanceList As List(Of Balance)

    Private RegisterPaymentMemoForm As RegisterPaymentMemoForm

    Private Sub InitBalance()
        BalancesDataGridView.AutoGenerateColumns = False
    End Sub

    Private Sub UpdateBalance()
        Dim beforeBalanceSeq As Integer = 0
        If BalancesDataGridView.SelectedRows.Count > 0 Then
            beforeBalanceSeq = DirectCast(BalancesDataGridView.SelectedRows(0).Cells(0).Value, Integer)
        End If
        BalanceList = DataAccess.GetBalances(YearMonth)

        UpdateBalanceFilter()

        If beforeBalanceSeq > 0 Then
            SelectBalanceRowBySeq(beforeBalanceSeq)
        End If
    End Sub

    Private Sub UpdateBalanceFilter()
        BalancesDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
        BalancesDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing

        BalancesDataGridView.DataSource = BalanceList
        BalancesDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells)

        BalancesDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells
        BalancesDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
    End Sub

    Private Sub BalancesDataGridView_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs) Handles BalancesDataGridView.CellDoubleClick
        ModifyBalanceButton.PerformClick()
    End Sub

    Private Sub SelectBalanceRowBySeq(ByVal seq As Integer)
        BalancesDataGridView.ClearSelection()
        For i As Integer = 0 To BalancesDataGridView.Rows.Count - 1
            If DirectCast(BalancesDataGridView.Rows(i).Cells(0).Value, Integer) = seq Then
                BalancesDataGridView.Rows(i).Selected = True
                Dim firstIndex As Integer = i - (BalancesDataGridView.Height - BalancesDataGridView.ColumnHeadersHeight - SystemInformation.HorizontalScrollBarHeight) \ BalancesDataGridView.Rows(i).Height \ 2
                If firstIndex < 0 Then
                    firstIndex = 0
                End If
                BalancesDataGridView.FirstDisplayedScrollingRowIndex = firstIndex
                Return
            End If
        Next
        If BalancesDataGridView.Rows.Count > 0 Then
            BalancesDataGridView.Rows(0).Selected = True
        End If
    End Sub

    Private Sub NewBalanceButton_Click(sender As Object, e As EventArgs) Handles NewBalanceButton.Click
        Dim RegisterBalanceForm As New RegisterBalanceForm()
        RegisterBalanceForm.Balance = Nothing
        RegisterBalanceForm.DataAccess = DataAccess
        If RegisterBalanceForm.ShowDialog() = DialogResult.OK Then
            YearMonth = RegisterBalanceForm.Balance.Date
            UpdateBalance()
            UpdateNotify()
            SelectBalanceRowBySeq(RegisterBalanceForm.Balance.Seq)
        ElseIf RegisterBalanceForm.ReloadRequired Then
            UpdateBalance()
        End If
    End Sub

    Private Sub ModifyBalanceButton_Click(sender As Object, e As EventArgs) Handles ModifyBalanceButton.Click
        If BalancesDataGridView.Rows.GetRowCount(DataGridViewElementStates.Selected) = 0 Then
            Return
        End If
        Dim seq As Long = Convert.ToInt32(BalancesDataGridView.SelectedRows(0).Cells(0).Value)
        Dim RegisterBalanceForm As New RegisterBalanceForm()
        RegisterBalanceForm.Balance = BalanceList.Where(Function(a) a.Seq = seq).First
        RegisterBalanceForm.DataAccess = DataAccess
        If RegisterBalanceForm.ShowDialog() = DialogResult.OK Then
            YearMonth = RegisterBalanceForm.Balance.Date
            UpdateBalance()
            UpdateNotify()
            SelectBalanceRowBySeq(RegisterBalanceForm.Balance.Seq)
        ElseIf RegisterBalanceForm.ReloadRequired Then
            UpdateBalance()
        End If
    End Sub

    Private Sub PaymentMemoButton_Click(sender As Object, e As EventArgs) Handles PaymentMemoButton.Click
        If RegisterPaymentMemoForm Is Nothing OrElse RegisterPaymentMemoForm.IsDisposed Then
            RegisterPaymentMemoForm = New RegisterPaymentMemoForm()
            RegisterPaymentMemoForm.DataAccess = DataAccess
            RegisterPaymentMemoForm.Top = Me.Top
            RegisterPaymentMemoForm.Left = Me.Left + Me.Width

            Dim targetScreen As Screen = Screen.FromControl(Me)
            If targetScreen.Bounds.Left + targetScreen.Bounds.Width < RegisterPaymentMemoForm.Left + RegisterPaymentMemoForm.Width Then
                RegisterPaymentMemoForm.Left = targetScreen.Bounds.Left + targetScreen.Bounds.Width - RegisterPaymentMemoForm.Width
            End If
        End If
        If RegisterPaymentMemoForm.Visible Then
            RegisterPaymentMemoForm.Activate()
        Else
            RegisterPaymentMemoForm.Show()
        End If
    End Sub

End Class
