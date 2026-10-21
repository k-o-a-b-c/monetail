Partial Public Class ViewForm

    Private AccountList As List(Of Receipt)

    Private TransferList As List(Of Transfer)

    ' 計算モデルはそのまま参照し、一覧のバインド時だけ金額の範囲超過を表示に変える。
    Private NotInheritable Class ReceiptListRow
        Public ReadOnly Property Receipt As Receipt

        Public Sub New(source As Receipt)
            Receipt = source
        End Sub

        Public ReadOnly Property Seq As Integer
            Get
                Return Receipt.Seq
            End Get
        End Property

        Public ReadOnly Property [Date] As DateTime
            Get
                Return Receipt.[Date]
            End Get
        End Property

        Public ReadOnly Property ShopName As String
            Get
                Return Receipt.ShopName
            End Get
        End Property

        Public ReadOnly Property ShopLocation As String
            Get
                Return Receipt.ShopLocation
            End Get
        End Property

        Public ReadOnly Property PaymentClassification As PaymentClassification
            Get
                Return Receipt.PaymentClassification
            End Get
        End Property

        Public ReadOnly Property Tag As String
            Get
                Return Receipt.Tag
            End Get
        End Property

        Public ReadOnly Property Total As Object
            Get
                Try
                    Return Receipt.Total
                Catch ex As OverflowException
                    Return "範囲超過"
                End Try
            End Get
        End Property
    End Class

    Private Sub InitList()
        HouseholdAccountDataGridView.AutoGenerateColumns = False
        TransferDataGridView.AutoGenerateColumns = False
    End Sub

    Private Sub UpdateList()
        Dim beforeReceiptSeq As Integer = 0
        Dim beforeTransferSeq As Integer = 0
        If HouseholdAccountDataGridView.SelectedRows.Count > 0 Then
            beforeReceiptSeq = DirectCast(HouseholdAccountDataGridView.SelectedRows(0).Cells(0).Value, Integer)
        End If
        If TransferDataGridView.SelectedRows.Count > 0 Then
            beforeTransferSeq = DirectCast(TransferDataGridView.SelectedRows(0).Cells(0).Value, Integer)
        End If
        AccountList = DataAccess.GetReceipts(YearMonth)
        TransferList = DataAccess.GetTransfers(YearMonth)

        HouseholdAccountDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
        HouseholdAccountDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        TransferDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing

        UpdateListFilter()

        If beforeReceiptSeq > 0 Then
            SelectReceiptRowBySeq(beforeReceiptSeq)
        End If
        If beforeTransferSeq > 0 Then
            SelectTransferRowBySeq(beforeTransferSeq)
        End If

        HouseholdAccountDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells
        HouseholdAccountDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        TransferDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize

    End Sub

    Private Sub UpdateListFilter()

        'ここでやらないと何故かエラーになる
        TransferDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None

        HouseholdAccountDataGridView.DataSource = AccountList.Where(Function(a)
                                                                        Return (UserComboBox.SelectedIndex = 0 OrElse a.PaymentClassification.User.Code = DirectCast(UserComboBox.SelectedValue, Integer)) _
                                                                            AndAlso (PaymentComboBox.SelectedIndex = 0 OrElse a.PaymentClassification.Code = DirectCast(PaymentComboBox.SelectedValue, Integer))
                                                                    End Function).Select(Function(a) New ReceiptListRow(a)).ToList()
        TransferDataGridView.DataSource = TransferList.Where(Function(t)
                                                                 Return (UserComboBox.SelectedIndex = 0 OrElse t.FromPaymentClassification.User.Code = DirectCast(UserComboBox.SelectedValue, Integer) OrElse t.ToPaymentClassification.User.Code = DirectCast(UserComboBox.SelectedValue, Integer)) _
                                                                     AndAlso (PaymentComboBox.SelectedIndex = 0 OrElse t.FromPaymentClassification.Code = DirectCast(PaymentComboBox.SelectedValue, Integer) OrElse t.ToPaymentClassification.Code = DirectCast(PaymentComboBox.SelectedValue, Integer))
                                                             End Function).ToList()

        HouseholdAccountDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells)
        TransferDataGridView.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells)

        Dim cellStyle As New DataGridViewCellStyle()
        cellStyle.BackColor = Color.MistyRose
        For Each row As DataGridViewRow In HouseholdAccountDataGridView.Rows
            If DirectCast(row.DataBoundItem, ReceiptListRow).Receipt.Groups.Exists(Function(g) g.Details.Exists(Function(d) d.Users.Count = 0)) Then
                row.DefaultCellStyle = cellStyle
            End If
        Next

        TransferDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells

    End Sub

    Private Sub HouseholdAccountDataGridView_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs) Handles HouseholdAccountDataGridView.CellFormatting
        If e.RowIndex < 0 OrElse e.ColumnIndex <> ReceiptTotalColumn.Index Then Return
        Dim cell = HouseholdAccountDataGridView.Rows(e.RowIndex).Cells(e.ColumnIndex)
        cell.ToolTipText = If(TypeOf e.Value Is String,
            "合計金額が扱える範囲を超えています。レシートを開いて明細を確認してください。", String.Empty)
    End Sub

    Private Sub NewButton_Click(sender As Object, e As EventArgs) Handles NewButton.Click
        Dim RegisterForm As New RegisterForm()
        RegisterForm.Receipt = Nothing
        RegisterForm.DataAccess = DataAccess
        If RegisterForm.ShowDialog() = DialogResult.OK Then
            YearMonth = RegisterForm.Receipt.Date
            UpdateNotify(RegisterForm.PaymentClassification)
            UpdateList()
            SelectReceiptRowBySeq(RegisterForm.Receipt.Seq)
        ElseIf RegisterForm.ReloadRequired Then
            UpdateList()
        End If
    End Sub

    Private Sub ModifyButton_Click(sender As Object, e As EventArgs) Handles ModifyButton.Click
        If HouseholdAccountDataGridView.Rows.GetRowCount(DataGridViewElementStates.Selected) = 0 Then
            Return
        End If
        Dim seq As Long = Convert.ToInt32(HouseholdAccountDataGridView.SelectedRows(0).Cells(0).Value)
        Dim RegisterForm As New RegisterForm()
        RegisterForm.Receipt = AccountList.Where(Function(a) a.Seq = seq).First
        RegisterForm.DataAccess = DataAccess
        If RegisterForm.ShowDialog() = DialogResult.OK Then
            YearMonth = RegisterForm.Receipt.Date
            UpdateNotify(RegisterForm.PaymentClassification)
            UpdateList()
            SelectReceiptRowBySeq(RegisterForm.Receipt.Seq)
        ElseIf RegisterForm.ReloadRequired Then
            UpdateList()
        End If
    End Sub

    Private Sub PaymentComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles PaymentComboBox.SelectedIndexChanged
        If EventEnabled Then
            UpdateListFilter()
        End If
    End Sub

    Private Sub TransferNewButton_Click(sender As Object, e As EventArgs) Handles TransferNewButton.Click
        Dim RegisterTransferForm As New RegisterTransferForm()
        RegisterTransferForm.Transfer = Nothing
        RegisterTransferForm.DataAccess = DataAccess
        If RegisterTransferForm.ShowDialog() = DialogResult.OK Then
            YearMonth = RegisterTransferForm.Transfer.Date
            UpdateNotify(RegisterTransferForm.FromPaymentClassification)
            UpdateNotify(RegisterTransferForm.ToPaymentClassification)
            UpdateList()
            SelectTransferRowBySeq(RegisterTransferForm.Transfer.Seq)
        ElseIf RegisterTransferForm.ReloadRequired Then
            UpdateList()
        End If
    End Sub

    Private Sub TransferModifyButton_Click(sender As Object, e As EventArgs) Handles TransferModifyButton.Click
        If TransferDataGridView.Rows.GetRowCount(DataGridViewElementStates.Selected) = 0 Then
            Return
        End If
        Dim seq As Long = Convert.ToInt32(TransferDataGridView.SelectedRows(0).Cells(0).Value)
        Dim RegisterTransferForm As New RegisterTransferForm()
        RegisterTransferForm.Transfer = TransferList.Where(Function(a) a.Seq = seq).First
        RegisterTransferForm.DataAccess = DataAccess
        If RegisterTransferForm.ShowDialog() = DialogResult.OK Then
            YearMonth = RegisterTransferForm.Transfer.Date
            UpdateNotify(RegisterTransferForm.FromPaymentClassification)
            UpdateNotify(RegisterTransferForm.ToPaymentClassification)
            UpdateList()
            SelectTransferRowBySeq(RegisterTransferForm.Transfer.Seq)
        ElseIf RegisterTransferForm.ReloadRequired Then
            UpdateList()
        End If
    End Sub

    Private Sub HouseholdAccountDataGridView_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs) Handles HouseholdAccountDataGridView.CellDoubleClick
        ModifyButton.PerformClick()
    End Sub

    Private Sub TransferDataGridView_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs) Handles TransferDataGridView.CellDoubleClick
        TransferModifyButton.PerformClick()
    End Sub

    Private Sub SelectReceiptRowBySeq(ByVal seq As Integer)
        HouseholdAccountDataGridView.ClearSelection()
        For i As Integer = 0 To HouseholdAccountDataGridView.Rows.Count - 1
            If DirectCast(HouseholdAccountDataGridView.Rows(i).Cells(0).Value, Integer) = seq Then
                HouseholdAccountDataGridView.Rows(i).Selected = True
                Dim firstIndex As Integer = i - (HouseholdAccountDataGridView.Height - HouseholdAccountDataGridView.ColumnHeadersHeight - SystemInformation.HorizontalScrollBarHeight) \ HouseholdAccountDataGridView.Rows(i).Height \ 2
                If firstIndex < 0 Then
                    firstIndex = 0
                End If
                HouseholdAccountDataGridView.FirstDisplayedScrollingRowIndex = firstIndex
                Return
            End If
        Next
        If HouseholdAccountDataGridView.Rows.Count > 0 Then
            HouseholdAccountDataGridView.Rows(0).Selected = True
        End If
    End Sub

    Private Sub SelectTransferRowBySeq(ByVal seq As Integer)
        TransferDataGridView.ClearSelection()
        For i As Integer = 0 To TransferDataGridView.Rows.Count - 1
            If DirectCast(TransferDataGridView.Rows(i).Cells(0).Value, Integer) = seq Then
                TransferDataGridView.Rows(i).Selected = True
                TransferDataGridView.FirstDisplayedScrollingRowIndex = i
                Return
            End If
        Next
        If TransferDataGridView.Rows.Count > 0 Then
            TransferDataGridView.Rows(0).Selected = True
        End If
    End Sub

End Class
