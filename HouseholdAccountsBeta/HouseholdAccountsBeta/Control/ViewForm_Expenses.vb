' 収支集計
Partial Public Class ViewForm

    Private Sub UpdateExpenses()

        ExpensesDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
        ExpensesDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing

        ExpensesDataGridView.Rows.Clear()


        Dim ReceiptList As List(Of Receipt) = DataAccess.GetReceipts(YearMonth.Year)
        ReceiptList = ReceiptList.Where(Function(r)
                                            Return UserComboBox.SelectedIndex = 0 OrElse r.PaymentClassification.User.Code = DirectCast(UserComboBox.SelectedValue, Integer)
                                        End Function).ToList()
        Dim PaymentClassificationList As List(Of PaymentClassification) = DataAccess.PaymentClassificationList()
        PaymentClassificationList = PaymentClassificationList.Where(Function(p) UserComboBox.SelectedIndex = 0 OrElse p.User.Code = DirectCast(UserComboBox.SelectedItem, User).Code).ToList()

        Dim incomesDict As New Dictionary(Of Integer, Integer)()
        Dim expensesDict As New Dictionary(Of Integer, Integer)()
        Dim totalDict As New Dictionary(Of Integer, Integer)()
        For Month As Integer = 1 To 13
            incomesDict.Add(Month, 0)
            expensesDict.Add(Month, 0)
            totalDict.Add(Month, 0)
        Next

        For Each Payment As PaymentClassification In PaymentClassificationList
            Dim values As New List(Of Object)()
            values.Add(Payment.Code)
            values.Add(Payment.Name)
            Dim PaymentTotal As Integer = 0

            For Month As Integer = 1 To 12
                Dim tergetMonth As Integer = Month
                'Dim Total As Integer = ReceiptList.Where(Function(r) r.PaymentClassification.Code = Payment.Code AndAlso r.Date.Month = tergetMonth).ToList().Sum(Function(r) r.Total)
                'values.Add(Total)
                'PaymentTotal += Total
                'If Total < 0 Then
                '    incomesDict.Item(Month) += Total
                'Else
                '    expensesDict.Item(Month) += Total
                'End If
                'If Payment.Code = 14 Then
                '    If Total < 0 Then
                '        incomesDict.Item(Month) += Total
                '        incomesDict.Item(13) += Total
                '    Else
                '        expensesDict.Item(Month) += Total
                '        expensesDict.Item(13) += Total
                '    End If
                'Else
                Dim incomes As Integer = 0
                Dim expenses As Integer = 0
                If Not ExpenseRadioButton.Checked Then
                    incomes = ReceiptList.Where(Function(r) r.PaymentClassification.Code = Payment.Code AndAlso r.Date.Month = tergetMonth).ToList().Sum(Function(r) r.Incomes)
                End If
                If Not IncomeRadioButton.Checked Then
                    expenses = ReceiptList.Where(Function(r) r.PaymentClassification.Code = Payment.Code AndAlso r.Date.Month = tergetMonth).ToList().Sum(Function(r) r.Expenses)
                End If
                Dim Total As Integer = incomes + expenses
                PaymentTotal += Total
                values.Add(Total)
                incomesDict.Item(Month) += incomes
                expensesDict.Item(Month) += expenses
                incomesDict.Item(13) += incomes
                expensesDict.Item(13) += expenses
                'End If
                totalDict.Item(Month) += Total
                totalDict.Item(13) += Total
            Next
            values.Add(PaymentTotal)
            ExpensesDataGridView.Rows.Add(values.ToArray())
        Next

        Dim incomesList As New List(Of Object)()
        Dim expensesList As New List(Of Object)()
        Dim totalValues As New List(Of Object)()
        incomesList.Add(-1)
        incomesList.Add("収入")
        expensesList.Add(-1)
        expensesList.Add("支出")
        totalValues.Add(-1)
        totalValues.Add("計")
        For Month As Integer = 1 To 13
            incomesList.Add(incomesDict.Item(Month))
            expensesList.Add(expensesDict.Item(Month))
            totalValues.Add(totalDict.Item(Month))
        Next
        If Not ExpenseRadioButton.Checked Then
            ExpensesDataGridView.Rows.Add(incomesList.ToArray())
        End If
        If Not IncomeRadioButton.Checked Then
            ExpensesDataGridView.Rows.Add(expensesList.ToArray())
        End If
        If IncomeAndExpenseRadioButton.Checked Then
            ExpensesDataGridView.Rows.Add(totalValues.ToArray())
        End If

        ExpensesDataGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells
        ExpensesDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize

    End Sub

    Private Sub ExpensesDataGridView_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs) Handles ExpensesDataGridView.CellFormatting
        Dim dgv As DataGridView = DirectCast(sender, DataGridView)

        'セルの列を確認
        If TypeOf e.Value Is Integer Then
            Dim value As Integer = DirectCast(e.Value, Integer)
            If value > 0 Then
                e.CellStyle.ForeColor = Color.Red
            End If
        End If
    End Sub

    Private Sub IncomeAndExpenseRadioButton_CheckedChanged(sender As Object, e As EventArgs) Handles IncomeAndExpenseRadioButton.CheckedChanged
        If YearMonth = DateTime.MinValue Then
            Return
        End If
        UpdateExpenses()
    End Sub

    Private Sub IncomeRadioButton_CheckedChanged(sender As Object, e As EventArgs) Handles IncomeRadioButton.CheckedChanged
        UpdateExpenses()
    End Sub

    Private Sub ExpenseRadioButton_CheckedChanged(sender As Object, e As EventArgs) Handles ExpenseRadioButton.CheckedChanged
        UpdateExpenses()
    End Sub
    Private Sub ExpensesDataGridView_CellMouseDoubleClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles ExpensesDataGridView.CellMouseDoubleClick
        If e.RowIndex < 0 OrElse e.ColumnIndex < 2 Then
            Return
        End If
        Dim PaymentCode As Integer = CInt(ExpensesDataGridView.Rows(e.RowIndex).Cells("ExpensesPaymentCodeColumn").Value)
        Dim Month As Integer = e.ColumnIndex - 1
        If Month = 13 Then
            Month = 0
        End If

        Dim DetailViewForm As New DetailViewForm()
        DetailViewForm.DataAccess = DataAccess
        DetailViewForm.Year = YearMonth.Year
        DetailViewForm.Month = Month
        DetailViewForm.Mode = DetailViewFormMode.Payment
        DetailViewForm.Code = PaymentCode
        DetailViewForm.ShowDialog(Me)
    End Sub

End Class
