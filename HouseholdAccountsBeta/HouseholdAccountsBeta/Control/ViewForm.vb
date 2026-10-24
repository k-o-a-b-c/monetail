Imports System.ComponentModel
Imports System.Reflection

Public Class ViewForm

    Private DataAccess As DataAccess
    Private EventEnabled As Boolean

    Private Property _YearMonth As Date

    Private Property YearMonth As Date
        Get
            Return _YearMonth
        End Get
        Set(value As Date)
            _YearMonth = value
            UpdateView()
            YearTextBox.Text = value.Year.ToString()
            MonthTextBox.Text = value.Month.ToString()
        End Set
    End Property

    Public Sub New()

        ' この呼び出しはデザイナーで必要です。
        InitializeComponent()

        ' InitializeComponent() 呼び出しの後で初期化を追加します。
        SplitContainer1.SplitterDistance = SplitContainer1.Width \ 2
        SplitContainer2.SplitterDistance = (SplitContainer2.Height \ 7) * 5

        Dim dgvType As Type = HouseholdAccountDataGridView.GetType()
        Dim pi As PropertyInfo = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance Or BindingFlags.NonPublic)
        pi.SetValue(HouseholdAccountDataGridView, True, Nothing)
        pi.SetValue(TransferDataGridView, True, Nothing)
        pi.SetValue(BalancesDataGridView, True, Nothing)
        pi.SetValue(GroupStatisticsDataGridView, True, Nothing)
        pi.SetValue(BalanceStatisticsDataGridView, True, Nothing)
        pi.SetValue(ExpensesDataGridView, True, Nothing)

    End Sub

    Private Sub ViewForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        DataAccess = DatabaseStartup.TryOpenConfiguredDatabase(
            Function() My.Settings.ConnectionString,
            Sub(message) MessageBox.Show(message, "DB設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Error),
            Function() My.Settings.UserCode)
        If DataAccess Is Nothing Then
            Close()
            Return
        End If
        Calculation.DataAccess = DataAccess

        InitList()
        InitBalance()

        Dim UserList As List(Of User) = DataAccess.UserList().ToList()
        UserList.Insert(0, New User() With {
                        .Code = 0,
                        .Name = String.Empty,
                        .AccountName = String.Empty
                        })

        EventEnabled = False

        UserComboBox.DataSource = UserList
        UserComboBox.SelectedIndex = 0

        UserListBox.DataSource = DataAccess.UserList()
        UserListBox.ClearSelected()
        UserListBox.Height = Panel7.Height
        PaymentMemoButton.Left = Panel4.Width - PaymentMemoButton.Width - NewBalanceButton.Left

        YearMonth = Date.Today

        EventEnabled = True
        AddHandler UserListBox.SelectedIndexChanged, AddressOf UserListBox_SelectedIndexChanged

        Dim g As Graphics = Me.CreateGraphics()
        Dim charSize As SizeF = g.MeasureString("あ", GroupStatisticsDataGridView.DefaultCellStyle.Font)

        Dim columnWidth As Integer

        GroupStatisticsDataGridView.Columns.Item(1).Width = Convert.ToInt32(charSize.Height * 8)
        columnWidth = (GroupStatisticsDataGridView.Width - GroupStatisticsDataGridView.Columns.Item(1).Width - SystemInformation.VerticalScrollBarWidth) \ 13
        For i As Integer = 2 To 14
            GroupStatisticsDataGridView.Columns.Item(i).Width = columnWidth
        Next

        BalanceStatisticsDataGridView.Columns.Item(1).Width = Convert.ToInt32(charSize.Height * 8)
        columnWidth = (BalanceStatisticsDataGridView.Width - BalanceStatisticsDataGridView.Columns.Item(1).Width - SystemInformation.VerticalScrollBarWidth) \ 13
        For i As Integer = 2 To 13
            BalanceStatisticsDataGridView.Columns.Item(i).Width = columnWidth
        Next

        ExpensesDataGridView.Columns.Item(1).Width = Convert.ToInt32(charSize.Height * 8)
        columnWidth = (ExpensesDataGridView.Width - ExpensesDataGridView.Columns.Item(1).Width - SystemInformation.VerticalScrollBarWidth) \ 13
        For i As Integer = 2 To 14
            ExpensesDataGridView.Columns.Item(i).Width = columnWidth
        Next

    End Sub

    Private Sub PrevMonthButton_Click(sender As Object, e As EventArgs) Handles PrevMonthButton.Click
        Try
            If MonthTextBox.Enabled Then
                YearMonth = YearMonth.AddMonths(-1)
            Else
                YearMonth = YearMonth.AddYears(-1)
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub NextMonthButton_Click(sender As Object, e As EventArgs) Handles NextMonthButton.Click
        Try
            If MonthTextBox.Enabled Then
                YearMonth = YearMonth.AddMonths(1)
            Else
                YearMonth = YearMonth.AddYears(1)
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub YearTextBox_Validating(sender As Object, e As CancelEventArgs) Handles YearTextBox.Validating
        Try
            YearMonth = New Date(Integer.Parse(YearTextBox.Text), YearMonth.Month, 1)
        Catch ex As Exception
            YearMonth = YearMonth
        End Try
    End Sub

    Private Sub MonthTextBox_Validating(sender As Object, e As CancelEventArgs) Handles MonthTextBox.Validating
        Try
            YearMonth = New Date(YearMonth.Year, Integer.Parse(MonthTextBox.Text), 1)
        Catch ex As Exception
            YearMonth = YearMonth
        End Try
    End Sub

    Private Sub MainTabControl_SelectedIndexChanged(sender As Object, e As EventArgs) Handles MainTabControl.SelectedIndexChanged
        If EventEnabled Then
            UpdateView()
        End If
    End Sub

    Private Sub UpdateView()
        Dim monthControl As Boolean
        Select Case True
            Case MainTabControl.SelectedTab Is ListTabPage
                UpdateList()
                UpdateBalance()
                monthControl = True
            Case MainTabControl.SelectedTab Is ListStatisticsTabPage
                UpdateListStatistics()
                monthControl = False
            Case MainTabControl.SelectedTab Is BalanceStatisticsTabPage
                UpdateBalanceStatistics()
                monthControl = False
            Case MainTabControl.SelectedTab Is ExpensesTabPage
                UpdateExpenses()
                monthControl = False
        End Select
        MonthTextBox.Enabled = monthControl
        MonthLabel.Enabled = monthControl
    End Sub
    Private Sub UpdateFilter()
        Select Case True
            Case MainTabControl.SelectedTab Is ListTabPage
                UpdateListFilter()
            Case MainTabControl.SelectedTab Is ListStatisticsTabPage
                UpdateListStatistics()
            Case MainTabControl.SelectedTab Is BalanceStatisticsTabPage
                UpdateBalanceStatistics()
            Case MainTabControl.SelectedTab Is ExpensesTabPage
                UpdateExpenses()
        End Select
    End Sub

    Private Sub ViewForm_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.KeyData = (Keys.Left Or Keys.Alt) Then
            PrevMonthButton.Select()
            PrevMonthButton.PerformClick()
            e.SuppressKeyPress = True
        ElseIf e.KeyData = (Keys.Right Or Keys.Alt) Then
            NextMonthButton.Select()
            NextMonthButton.PerformClick()
            e.SuppressKeyPress = True
        ElseIf e.KeyData = (Keys.N Or Keys.Control) Then
            Select Case True
                Case MainTabControl.SelectedTab Is ListTabPage
                    NewButton.Select()
                    NewButton.PerformClick()
                Case MainTabControl.SelectedTab Is ListStatisticsTabPage
                    'NOP
                Case MainTabControl.SelectedTab Is BalanceStatisticsTabPage
                    'NOP
            End Select
            e.SuppressKeyPress = True
        End If
    End Sub

    Private Sub UserComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles UserComboBox.SelectedIndexChanged

        Dim beforePayment As Integer
        If PaymentComboBox.SelectedIndex > 0 Then
            beforePayment = DirectCast(PaymentComboBox.SelectedValue, Integer)
        End If
        Dim PaymentClassificationList As New List(Of PaymentClassification)()
        PaymentClassificationList.Add(New PaymentClassification() With {.Code = 0, .DefaultPointCode = 0, .Name = ""})
        PaymentClassificationList.AddRange(DataAccess.PaymentClassificationList().Where(Function(p) UserComboBox.SelectedIndex = 0 OrElse p.User.Code = DirectCast(UserComboBox.SelectedValue, Integer)))
        PaymentComboBox.DataSource = PaymentClassificationList
        If PaymentClassificationList.Exists(Function(p) p.Code = beforePayment) Then
            PaymentComboBox.SelectedItem = PaymentClassificationList.First(Function(p) p.Code = beforePayment)
        End If

        If Not EventEnabled Then
            Return
        End If
        UpdateFilter()
    End Sub

    Private Sub UserListBox_SelectedIndexChanged(sender As Object, e As EventArgs)
        OnlyPersonCheckBox.Enabled = UserPartialRadioButton.Checked AndAlso UserListBox.SelectedItems.Count = 1
        UpdateFilter()
    End Sub

    Private Sub MasterMaintenanceButton_Click(sender As Object, e As EventArgs) Handles MasterMaintenanceButton.Click
        Dim MasterMaintenanceForm As New MasterMaintenanceForm()
        MasterMaintenanceForm.DataAccess = DataAccess
        MasterMaintenanceForm.ShowDialog()
        UpdateView()
    End Sub

    Private Sub UserPartialRadioButton_CheckedChanged(sender As Object, e As EventArgs) Handles UserPartialRadioButton.CheckedChanged
        OnlyPersonCheckBox.Enabled = UserPartialRadioButton.Checked AndAlso UserListBox.SelectedItems.Count = 1
        If EventEnabled Then
            UpdateFilter()
        End If
    End Sub

    Private Sub PerPersonCheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles OnlyPersonCheckBox.CheckedChanged
        UpdateFilter()
    End Sub

    Private Sub UpdateNotify()
        UpdateNotify(Nothing)
    End Sub

    Private Sub UpdateNotify(ByVal PaymentClassification As PaymentClassification)
        If RegisterPaymentMemoForm IsNot Nothing Then
            RegisterPaymentMemoForm.UpdateBalance(PaymentClassification)
        End If
    End Sub

    Private Sub ViewForm_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If RegisterPaymentMemoForm IsNot Nothing Then
            RegisterPaymentMemoForm.Close()
            If Not RegisterPaymentMemoForm.IsDisposed Then
                e.Cancel = True
            End If
        End If
    End Sub

End Class
