
Imports System.Reflection
Imports System.Text
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.ListView

Public Enum DetailViewFormMode
    Group
    Payment
End Enum

Public Class DetailViewForm

    Public Property DataAccess As DataAccess


    Public Year As Integer
    Public Month As Integer
    Public Mode As DetailViewFormMode

    Public Code As Integer

    Public Sub New()
        InitializeComponent()

        Dim dgvType As Type = GroupDetailGridView.GetType()
        Dim pi As PropertyInfo = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance Or BindingFlags.NonPublic)
        pi.SetValue(GroupDetailGridView, True, Nothing)
    End Sub

    Private Sub ShowGroupStatistics(ByVal Year As Integer, ByVal Month As Integer, ByVal Code As Integer)

        ' 処理高速化（開始）
        GroupDetailGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
        GroupDetailGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        GroupDetailGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing

        GroupDetailGridView.Rows.Clear()
        Try
            For Each Column As DataGridViewColumn In GroupDetailGridView.Columns
                Column.HeaderCell.SortGlyphDirection = SortOrder.None
            Next

            Dim Receipts As List(Of Receipt)
            If Mode = DetailViewFormMode.Group Then
                Receipts = DataAccess.GetReceipts(Year, Month, Code)
            Else
                Receipts = DataAccess.GetReceipts(Year, Month, -1, Code)
            End If

            GroupDetailGridView.Columns("GroupColumn").Visible = (Code < 0)

            If DetailCheckBox.Checked Then
                GroupDetailGridView.Columns("ItemColumn").Visible = True
                GroupDetailGridView.Columns("QuantityColumn").Visible = True
                For Each Receipt As Receipt In Receipts
                    Dim amounts = Calculation.GetReceiptDetailAmounts(Receipt.Groups.SelectMany(Function(g) g.Details), Receipt.TaxType, Receipt.TaxRoundingMode)
                    For Each Group As ReceiptGroup In Receipt.Groups
                        For Each Detail As ReceiptDetail In Group.Details
                            Dim rowIndex As Integer = GroupDetailGridView.Rows.Add()
                            Dim row As DataGridViewRow = GroupDetailGridView.Rows(rowIndex)
                            row.Cells("SeqColumn").Value = Receipt.Seq
                            row.Cells("DateTimeColumn").Value = Receipt.Date.ToString("yyyy/MM/dd HH:mm:ss")
                            row.Cells("ShopColumn").Value = Receipt.ShopName
                            row.Cells("LocationColumn").Value = Receipt.ShopLocation
                            row.Cells("PaymentColumn").Value = Receipt.PaymentClassification.Name
                            row.Cells("TagColumn").Value = Receipt.Tag
                            row.Cells("GroupColumn").Value = Group.GroupClassification.Name
                            row.Cells("ItemColumn").Value = Detail.Name
                            row.Cells("QuantityColumn").Value = Detail.Quantity
                            row.Cells("FeeColumn").Value = Calculation.DetailCalculatedFee(Receipt, Group, Detail, amounts)
                        Next
                    Next
                Next
            Else
                GroupDetailGridView.Columns("ItemColumn").Visible = False
                GroupDetailGridView.Columns("QuantityColumn").Visible = False
                For Each Receipt As Receipt In Receipts
                    Dim amounts = Calculation.GetReceiptDetailAmounts(Receipt.Groups.SelectMany(Function(g) g.Details), Receipt.TaxType, Receipt.TaxRoundingMode)
                    For Each Group As ReceiptGroup In Receipt.Groups
                        Dim rowIndex As Integer = GroupDetailGridView.Rows.Add()
                        Dim row As DataGridViewRow = GroupDetailGridView.Rows(rowIndex)
                        row.Cells("SeqColumn").Value = Receipt.Seq
                        row.Cells("DateTimeColumn").Value = Receipt.Date.ToString("yyyy/MM/dd HH:mm:ss")
                        row.Cells("ShopColumn").Value = Receipt.ShopName
                        row.Cells("LocationColumn").Value = Receipt.ShopLocation
                        row.Cells("PaymentColumn").Value = Receipt.PaymentClassification.Name
                        row.Cells("TagColumn").Value = Receipt.Tag
                        row.Cells("GroupColumn").Value = Group.GroupClassification.Name
                        row.Cells("FeeColumn").Value = Calculation.GroupCalculatedFee(Receipt, Group, amounts)
                    Next
                Next
            End If

        Catch ex As OverflowException
            ' 前の条件や途中までの明細を、正常な結果として残さない。
            GroupDetailGridView.Rows.Clear()
            Dim row = GroupDetailGridView.Rows(GroupDetailGridView.Rows.Add())
            row.Cells("DateTimeColumn").Value = "集計不可"
            row.Cells("FeeColumn").Value = "範囲超過"
            For Each cell As DataGridViewCell In row.Cells
                cell.ToolTipText = "金額が扱える範囲を超えたため、詳細を表示できません。明細表示や対象条件を見直してください。"
            Next
        Finally
            ' 金額以外の例外も表示設定を戻してから呼出元へ返す。
            ' 処理高速化（終了）
            GroupDetailGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells
            GroupDetailGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
            GroupDetailGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize

            If GroupDetailGridView.Columns("ItemColumn").Width > 500 Then
                GroupDetailGridView.Columns("ItemColumn").AutoSizeMode = DataGridViewAutoSizeColumnMode.None
                GroupDetailGridView.Columns("ItemColumn").Width = 500
            End If
        End Try

    End Sub

    Private Sub DetailViewForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Dim sb As StringBuilder = New StringBuilder("詳細表示 - ")
        sb.Append(Year).Append("年")
        If Month <> 0 Then
            sb.Append(Month).Append("月")
        End If
        If Code >= 0 Then
            If Mode = DetailViewFormMode.Group Then
                sb.Append(" 分類: ")
                sb.Append(DataAccess.GroupClassificationList.FirstOrDefault(Function(a) a.Code = Code).Name)
            Else
                sb.Append(" 支払い方法: ")
                sb.Append(DataAccess.PaymentClassificationList.FirstOrDefault(Function(a) a.Code = Code).Name)
            End If
        Else
            sb.Append(" 全て")
        End If
        Me.Text = sb.ToString()
        ShowGroupStatistics(Year, Month, Code)
    End Sub

    Private Sub DetailCheckBox_CheckedChanged(sender As Object, e As EventArgs) Handles DetailCheckBox.CheckedChanged
        ShowGroupStatistics(Year, Month, Code)
    End Sub

End Class