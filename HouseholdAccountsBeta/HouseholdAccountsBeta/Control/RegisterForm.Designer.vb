<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class RegisterForm
    Inherits System.Windows.Forms.Form

    'フォームがコンポーネントの一覧をクリーンアップするために dispose をオーバーライドします。
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Windows フォーム デザイナーで必要です。
    Private components As System.ComponentModel.IContainer

    'メモ: 以下のプロシージャは Windows フォーム デザイナーで必要です。
    'Windows フォーム デザイナーを使用して変更できます。  
    'コード エディターを使って変更しないでください。
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.Label2 = New System.Windows.Forms.Label()
        Me.Label4 = New System.Windows.Forms.Label()
        Me.Label5 = New System.Windows.Forms.Label()
        Me.TaxRoundingModeLabel = New System.Windows.Forms.Label()
        Me.TaxRoundingModePanel = New System.Windows.Forms.Panel()
        Me.TaxRoundingFloorRadioButton = New OriginalControls.OriginalRadioButton()
        Me.TaxRoundingHalfUpRadioButton = New OriginalControls.OriginalRadioButton()
        Me.GroupBox1 = New System.Windows.Forms.GroupBox()
        Me.DetailPanel = New System.Windows.Forms.Panel()
        Me.Label18 = New System.Windows.Forms.Label()
        Me.Label16 = New System.Windows.Forms.Label()
        Me.Label10 = New System.Windows.Forms.Label()
        Me.DetailListView = New System.Windows.Forms.ListView()
        Me.ItemNameColumnHeader = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.DetailFeeColumnHeader = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.DetailTagColumnHeader = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.Label11 = New System.Windows.Forms.Label()
        Me.AddDetailButton = New System.Windows.Forms.Button()
        Me.Label14 = New System.Windows.Forms.Label()
        Me.RemoveDetailButton = New System.Windows.Forms.Button()
        Me.Label17 = New System.Windows.Forms.Label()
        Me.Label12 = New System.Windows.Forms.Label()
        Me.GroupPanel = New System.Windows.Forms.Panel()
        Me.Label3 = New System.Windows.Forms.Label()
        Me.Label8 = New System.Windows.Forms.Label()
        Me.GroupListView = New System.Windows.Forms.ListView()
        Me.ClassificationNameColumnHeader = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.FeeColumnHeader = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.TagColumnHeader = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.Label9 = New System.Windows.Forms.Label()
        Me.AddGroupButton = New System.Windows.Forms.Button()
        Me.RemoveGroupButton = New System.Windows.Forms.Button()
        Me.Label6 = New System.Windows.Forms.Label()
        Me.SeqLabel = New System.Windows.Forms.Label()
        Me.Label15 = New System.Windows.Forms.Label()
        Me.DataGridViewComboBoxColumn1 = New System.Windows.Forms.DataGridViewComboBoxColumn()
        Me.DeleteButton = New System.Windows.Forms.Button()
        Me.RegiserButton = New System.Windows.Forms.Button()
        Me.CloseButton = New System.Windows.Forms.Button()
        Me.Label13 = New System.Windows.Forms.Label()
        Me.PointDataGridView = New System.Windows.Forms.DataGridView()
        Me.SeqColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.PointClassificationColumn = New System.Windows.Forms.DataGridViewComboBoxColumn()
        Me.PointRateColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.FixedPointsColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.TotalPointColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.CopyButton = New System.Windows.Forms.Button()
        Me.Label7 = New System.Windows.Forms.Label()
        Me.HelpToolTip = New System.Windows.Forms.ToolTip(Me.components)
        Me.HelpLabel = New System.Windows.Forms.Label()
        Me.TotalTextBox2 = New OriginalControls.OriginalTextBox()
        Me.ReceiptTimeTextBox = New OriginalControls.OriginalDateTimeTextBox()
        Me.ReceiptDateTextBox = New OriginalControls.OriginalDateTimeTextBox()
        Me.ShopLocationComboBox = New OriginalControls.OriginalComboBox()
        Me.ReceiptTagTextBox = New OriginalControls.OriginalTextBox()
        Me.TaxInclusiveRadioButton = New OriginalControls.OriginalRadioButton()
        Me.TaxExclusiveRadioButton = New OriginalControls.OriginalRadioButton()
        Me.QuantityTextBox = New OriginalControls.OriginalTextBox()
        Me.TaxTargetCheckBox = New OriginalControls.OriginalCheckBox()
        Me.DetailTagTextBox = New OriginalControls.OriginalTextBox()
        Me.FeeTextBox = New OriginalControls.OriginalTextBox()
        Me.DiscountRateTextBox = New OriginalControls.OriginalTextBox()
        Me.DiscountFeeTextBox = New OriginalControls.OriginalTextBox()
        Me.ItemNameComboBox = New OriginalControls.OriginalComboBox()
        Me.GroupTaxRateComboBox = New OriginalControls.OriginalComboBox()
        Me.GroupTagTextBox = New OriginalControls.OriginalTextBox()
        Me.GroupClassificationComboBox = New OriginalControls.OriginalComboBox()
        Me.PaymentClassificationComboBox = New OriginalControls.OriginalComboBox()
        Me.ShopNameComboBox = New OriginalControls.OriginalComboBox()
        Me.TotalTextBox = New OriginalControls.OriginalTextBox()
        Me.UserListBox = New OriginalControls.OriginalListBox()
        Me.TaxRoundingModePanel.SuspendLayout()
        Me.GroupBox1.SuspendLayout()
        Me.DetailPanel.SuspendLayout()
        Me.GroupPanel.SuspendLayout()
        CType(Me.PointDataGridView, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'Label1
        '
        Me.Label1.AutoSize = True
        Me.Label1.Location = New System.Drawing.Point(46, 32)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(31, 15)
        Me.Label1.TabIndex = 2
        Me.Label1.Text = "日付"
        '
        'Label2
        '
        Me.Label2.AutoSize = True
        Me.Label2.Location = New System.Drawing.Point(54, 59)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(19, 15)
        Me.Label2.TabIndex = 5
        Me.Label2.Text = "店"
        '
        'Label4
        '
        Me.Label4.AutoSize = True
        Me.Label4.Location = New System.Drawing.Point(34, 88)
        Me.Label4.Name = "Label4"
        Me.Label4.Size = New System.Drawing.Size(43, 15)
        Me.Label4.TabIndex = 8
        Me.Label4.Text = "消費税"
        '
        'Label5
        '
        Me.Label5.AutoSize = True
        Me.Label5.Location = New System.Drawing.Point(230, 88)
        Me.Label5.Name = "Label5"
        Me.Label5.Size = New System.Drawing.Size(31, 15)
        Me.Label5.TabIndex = 12
        Me.Label5.Text = "合計"
        '
        'TaxRoundingModeLabel
        '
        Me.TaxRoundingModeLabel.AutoSize = True
        Me.TaxRoundingModeLabel.Location = New System.Drawing.Point(383, 88)
        Me.TaxRoundingModeLabel.Name = "TaxRoundingModeLabel"
        Me.TaxRoundingModeLabel.Size = New System.Drawing.Size(43, 15)
        Me.TaxRoundingModeLabel.TabIndex = 11
        Me.TaxRoundingModeLabel.Text = "税端数"
        '
        'TaxRoundingModePanel
        '
        Me.TaxRoundingModePanel.AccessibleName = "消費税の端数処理"
        Me.TaxRoundingModePanel.Controls.Add(Me.TaxRoundingFloorRadioButton)
        Me.TaxRoundingModePanel.Controls.Add(Me.TaxRoundingHalfUpRadioButton)
        Me.TaxRoundingModePanel.Location = New System.Drawing.Point(430, 83)
        Me.TaxRoundingModePanel.Name = "TaxRoundingModePanel"
        Me.TaxRoundingModePanel.Size = New System.Drawing.Size(185, 27)
        Me.TaxRoundingModePanel.TabIndex = 12
        '
        'TaxRoundingFloorRadioButton
        '
        Me.TaxRoundingFloorRadioButton.AccessibleName = "税端数：切り捨て"
        Me.TaxRoundingFloorRadioButton.AutoSize = True
        Me.TaxRoundingFloorRadioButton.Location = New System.Drawing.Point(3, 3)
        Me.TaxRoundingFloorRadioButton.Name = "TaxRoundingFloorRadioButton"
        Me.TaxRoundingFloorRadioButton.Size = New System.Drawing.Size(73, 19)
        Me.TaxRoundingFloorRadioButton.TabIndex = 0
        Me.TaxRoundingFloorRadioButton.TabStop = True
        Me.TaxRoundingFloorRadioButton.Text = "切り捨て"
        Me.TaxRoundingFloorRadioButton.UseVisualStyleBackColor = True
        Me.HelpToolTip.SetToolTip(Me.TaxRoundingFloorRadioButton, "このレシートに適用する税端数処理。新規入力では店舗の直近レシートの設定を初期値にします。")
        '
        'TaxRoundingHalfUpRadioButton
        '
        Me.TaxRoundingHalfUpRadioButton.AccessibleName = "税端数：四捨五入"
        Me.TaxRoundingHalfUpRadioButton.AutoSize = True
        Me.TaxRoundingHalfUpRadioButton.Location = New System.Drawing.Point(86, 3)
        Me.TaxRoundingHalfUpRadioButton.Name = "TaxRoundingHalfUpRadioButton"
        Me.TaxRoundingHalfUpRadioButton.Size = New System.Drawing.Size(73, 19)
        Me.TaxRoundingHalfUpRadioButton.TabIndex = 1
        Me.TaxRoundingHalfUpRadioButton.TabStop = True
        Me.TaxRoundingHalfUpRadioButton.Text = "四捨五入"
        Me.TaxRoundingHalfUpRadioButton.UseVisualStyleBackColor = True
        Me.HelpToolTip.SetToolTip(Me.TaxRoundingHalfUpRadioButton, "このレシートに適用する税端数処理。新規入力では店舗の直近レシートの設定を初期値にします。")
        '
        'GroupBox1
        '
        Me.GroupBox1.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GroupBox1.Controls.Add(Me.DetailPanel)
        Me.GroupBox1.Controls.Add(Me.GroupPanel)
        Me.GroupBox1.Location = New System.Drawing.Point(12, 155)
        Me.GroupBox1.Name = "GroupBox1"
        Me.GroupBox1.Size = New System.Drawing.Size(603, 385)
        Me.GroupBox1.TabIndex = 18
        Me.GroupBox1.TabStop = False
        Me.GroupBox1.Text = "内訳"
        '
        'DetailPanel
        '
        Me.DetailPanel.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.DetailPanel.Controls.Add(Me.UserListBox)
        Me.DetailPanel.Controls.Add(Me.Label18)
        Me.DetailPanel.Controls.Add(Me.QuantityTextBox)
        Me.DetailPanel.Controls.Add(Me.Label16)
        Me.DetailPanel.Controls.Add(Me.Label10)
        Me.DetailPanel.Controls.Add(Me.DetailListView)
        Me.DetailPanel.Controls.Add(Me.Label11)
        Me.DetailPanel.Controls.Add(Me.AddDetailButton)
        Me.DetailPanel.Controls.Add(Me.TaxTargetCheckBox)
        Me.DetailPanel.Controls.Add(Me.Label14)
        Me.DetailPanel.Controls.Add(Me.RemoveDetailButton)
        Me.DetailPanel.Controls.Add(Me.DetailTagTextBox)
        Me.DetailPanel.Controls.Add(Me.FeeTextBox)
        Me.DetailPanel.Controls.Add(Me.DiscountRateTextBox)
        Me.DetailPanel.Controls.Add(Me.DiscountFeeTextBox)
        Me.DetailPanel.Controls.Add(Me.Label17)
        Me.DetailPanel.Controls.Add(Me.Label12)
        Me.DetailPanel.Controls.Add(Me.ItemNameComboBox)
        Me.DetailPanel.Enabled = False
        Me.DetailPanel.Location = New System.Drawing.Point(305, 22)
        Me.DetailPanel.Name = "DetailPanel"
        Me.DetailPanel.Size = New System.Drawing.Size(292, 357)
        Me.DetailPanel.TabIndex = 1
        '
        'Label18
        '
        Me.Label18.AutoSize = True
        Me.Label18.Location = New System.Drawing.Point(3, 152)
        Me.Label18.Name = "Label18"
        Me.Label18.Size = New System.Drawing.Size(43, 15)
        Me.Label18.TabIndex = 12
        Me.Label18.Text = "使用者"
        '
        'Label16
        '
        Me.Label16.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Label16.AutoSize = True
        Me.Label16.Location = New System.Drawing.Point(214, 36)
        Me.Label16.Name = "Label16"
        Me.Label16.Size = New System.Drawing.Size(15, 15)
        Me.Label16.TabIndex = 4
        Me.Label16.Text = "×"
        '
        'Label10
        '
        Me.Label10.AutoSize = True
        Me.Label10.Location = New System.Drawing.Point(3, 7)
        Me.Label10.Name = "Label10"
        Me.Label10.Size = New System.Drawing.Size(31, 15)
        Me.Label10.TabIndex = 0
        Me.Label10.Text = "品目"
        '
        'DetailListView
        '
        Me.DetailListView.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.DetailListView.Columns.AddRange(New System.Windows.Forms.ColumnHeader() {Me.ItemNameColumnHeader, Me.DetailFeeColumnHeader, Me.DetailTagColumnHeader})
        Me.DetailListView.HideSelection = False
        Me.DetailListView.Location = New System.Drawing.Point(52, 234)
        Me.DetailListView.MultiSelect = False
        Me.DetailListView.Name = "DetailListView"
        Me.DetailListView.Size = New System.Drawing.Size(156, 120)
        Me.DetailListView.TabIndex = 16
        Me.DetailListView.UseCompatibleStateImageBehavior = False
        Me.DetailListView.View = System.Windows.Forms.View.Details
        '
        'ItemNameColumnHeader
        '
        Me.ItemNameColumnHeader.Text = "品目"
        '
        'DetailFeeColumnHeader
        '
        Me.DetailFeeColumnHeader.Text = "金額"
        '
        'DetailTagColumnHeader
        '
        Me.DetailTagColumnHeader.Text = "タグ"
        '
        'Label11
        '
        Me.Label11.AutoSize = True
        Me.Label11.Location = New System.Drawing.Point(3, 123)
        Me.Label11.Name = "Label11"
        Me.Label11.Size = New System.Drawing.Size(25, 15)
        Me.Label11.TabIndex = 10
        Me.Label11.Text = "タグ"
        '
        'AddDetailButton
        '
        Me.AddDetailButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.AddDetailButton.Location = New System.Drawing.Point(214, 205)
        Me.AddDetailButton.Name = "AddDetailButton"
        Me.AddDetailButton.Size = New System.Drawing.Size(75, 23)
        Me.AddDetailButton.TabIndex = 15
        Me.AddDetailButton.Text = "追加"
        Me.AddDetailButton.UseVisualStyleBackColor = True
        '
        'Label14
        '
        Me.Label14.AutoSize = True
        Me.Label14.Location = New System.Drawing.Point(3, 36)
        Me.Label14.Name = "Label14"
        Me.Label14.Size = New System.Drawing.Size(31, 15)
        Me.Label14.TabIndex = 2
        Me.Label14.Text = "金額"
        '
        'RemoveDetailButton
        '
        Me.RemoveDetailButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.RemoveDetailButton.Enabled = False
        Me.RemoveDetailButton.Location = New System.Drawing.Point(214, 234)
        Me.RemoveDetailButton.Name = "RemoveDetailButton"
        Me.RemoveDetailButton.Size = New System.Drawing.Size(75, 23)
        Me.RemoveDetailButton.TabIndex = 17
        Me.RemoveDetailButton.Text = "削除"
        Me.RemoveDetailButton.UseVisualStyleBackColor = True
        '
        'Label17
        '
        Me.Label17.AutoSize = True
        Me.Label17.Location = New System.Drawing.Point(3, 94)
        Me.Label17.Name = "Label17"
        Me.Label17.Size = New System.Drawing.Size(43, 15)
        Me.Label17.TabIndex = 8
        Me.Label17.Text = "割引率"
        '
        'Label12
        '
        Me.Label12.AutoSize = True
        Me.Label12.Location = New System.Drawing.Point(3, 65)
        Me.Label12.Name = "Label12"
        Me.Label12.Size = New System.Drawing.Size(43, 15)
        Me.Label12.TabIndex = 6
        Me.Label12.Text = "割引額"
        '
        'GroupPanel
        '
        Me.GroupPanel.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.GroupPanel.Controls.Add(Me.Label3)
        Me.GroupPanel.Controls.Add(Me.GroupTaxRateComboBox)
        Me.GroupPanel.Controls.Add(Me.Label8)
        Me.GroupPanel.Controls.Add(Me.GroupListView)
        Me.GroupPanel.Controls.Add(Me.Label9)
        Me.GroupPanel.Controls.Add(Me.AddGroupButton)
        Me.GroupPanel.Controls.Add(Me.GroupTagTextBox)
        Me.GroupPanel.Controls.Add(Me.RemoveGroupButton)
        Me.GroupPanel.Controls.Add(Me.GroupClassificationComboBox)
        Me.GroupPanel.Location = New System.Drawing.Point(6, 22)
        Me.GroupPanel.Name = "GroupPanel"
        Me.GroupPanel.Size = New System.Drawing.Size(293, 357)
        Me.GroupPanel.TabIndex = 0
        '
        'Label3
        '
        Me.Label3.AutoSize = True
        Me.Label3.Location = New System.Drawing.Point(3, 36)
        Me.Label3.Name = "Label3"
        Me.Label3.Size = New System.Drawing.Size(31, 15)
        Me.Label3.TabIndex = 2
        Me.Label3.Text = "税率"
        '
        'Label8
        '
        Me.Label8.AutoSize = True
        Me.Label8.Location = New System.Drawing.Point(3, 7)
        Me.Label8.Name = "Label8"
        Me.Label8.Size = New System.Drawing.Size(31, 15)
        Me.Label8.TabIndex = 0
        Me.Label8.Text = "分類"
        '
        'GroupListView
        '
        Me.GroupListView.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.GroupListView.Columns.AddRange(New System.Windows.Forms.ColumnHeader() {Me.ClassificationNameColumnHeader, Me.FeeColumnHeader, Me.TagColumnHeader})
        Me.GroupListView.FullRowSelect = True
        Me.GroupListView.HideSelection = False
        Me.GroupListView.Location = New System.Drawing.Point(39, 91)
        Me.GroupListView.MultiSelect = False
        Me.GroupListView.Name = "GroupListView"
        Me.GroupListView.Size = New System.Drawing.Size(170, 263)
        Me.GroupListView.TabIndex = 7
        Me.GroupListView.UseCompatibleStateImageBehavior = False
        Me.GroupListView.View = System.Windows.Forms.View.Details
        '
        'ClassificationNameColumnHeader
        '
        Me.ClassificationNameColumnHeader.Text = "分類"
        '
        'FeeColumnHeader
        '
        Me.FeeColumnHeader.Text = "金額"
        '
        'TagColumnHeader
        '
        Me.TagColumnHeader.Text = "タグ"
        '
        'Label9
        '
        Me.Label9.AutoSize = True
        Me.Label9.Location = New System.Drawing.Point(3, 65)
        Me.Label9.Name = "Label9"
        Me.Label9.Size = New System.Drawing.Size(25, 15)
        Me.Label9.TabIndex = 4
        Me.Label9.Text = "タグ"
        '
        'AddGroupButton
        '
        Me.AddGroupButton.Location = New System.Drawing.Point(215, 62)
        Me.AddGroupButton.Name = "AddGroupButton"
        Me.AddGroupButton.Size = New System.Drawing.Size(75, 23)
        Me.AddGroupButton.TabIndex = 6
        Me.AddGroupButton.Text = "追加"
        Me.AddGroupButton.UseVisualStyleBackColor = True
        '
        'RemoveGroupButton
        '
        Me.RemoveGroupButton.Enabled = False
        Me.RemoveGroupButton.Location = New System.Drawing.Point(215, 91)
        Me.RemoveGroupButton.Name = "RemoveGroupButton"
        Me.RemoveGroupButton.Size = New System.Drawing.Size(75, 23)
        Me.RemoveGroupButton.TabIndex = 8
        Me.RemoveGroupButton.Text = "削除"
        Me.RemoveGroupButton.UseVisualStyleBackColor = True
        '
        'Label6
        '
        Me.Label6.AutoSize = True
        Me.Label6.Location = New System.Drawing.Point(22, 9)
        Me.Label6.Name = "Label6"
        Me.Label6.Size = New System.Drawing.Size(55, 15)
        Me.Label6.TabIndex = 0
        Me.Label6.Text = "登録番号"
        '
        'SeqLabel
        '
        Me.SeqLabel.AutoSize = True
        Me.SeqLabel.Location = New System.Drawing.Point(83, 9)
        Me.SeqLabel.Name = "SeqLabel"
        Me.SeqLabel.Size = New System.Drawing.Size(55, 15)
        Me.SeqLabel.TabIndex = 1
        Me.SeqLabel.Text = "00000000"
        '
        'Label15
        '
        Me.Label15.AutoSize = True
        Me.Label15.Location = New System.Drawing.Point(12, 117)
        Me.Label15.Name = "Label15"
        Me.Label15.Size = New System.Drawing.Size(65, 15)
        Me.Label15.TabIndex = 14
        Me.Label15.Text = "支払い方法"
        '
        'DataGridViewComboBoxColumn1
        '
        Me.DataGridViewComboBoxColumn1.DataPropertyName = "GroupClassification"
        Me.DataGridViewComboBoxColumn1.HeaderText = "GroupClassification"
        Me.DataGridViewComboBoxColumn1.Name = "DataGridViewComboBoxColumn1"
        Me.DataGridViewComboBoxColumn1.Resizable = System.Windows.Forms.DataGridViewTriState.[True]
        Me.DataGridViewComboBoxColumn1.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic
        '
        'DeleteButton
        '
        Me.DeleteButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.DeleteButton.Location = New System.Drawing.Point(93, 575)
        Me.DeleteButton.Name = "DeleteButton"
        Me.DeleteButton.Size = New System.Drawing.Size(75, 23)
        Me.DeleteButton.TabIndex = 23
        Me.DeleteButton.Text = "削除"
        Me.DeleteButton.UseVisualStyleBackColor = True
        '
        'RegiserButton
        '
        Me.RegiserButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.RegiserButton.Location = New System.Drawing.Point(540, 575)
        Me.RegiserButton.Name = "RegiserButton"
        Me.RegiserButton.Size = New System.Drawing.Size(75, 23)
        Me.RegiserButton.TabIndex = 20
        Me.RegiserButton.Text = "登録"
        Me.RegiserButton.UseVisualStyleBackColor = True
        '
        'CloseButton
        '
        Me.CloseButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.CloseButton.Location = New System.Drawing.Point(12, 575)
        Me.CloseButton.Name = "CloseButton"
        Me.CloseButton.Size = New System.Drawing.Size(75, 23)
        Me.CloseButton.TabIndex = 22
        Me.CloseButton.Text = "閉じる"
        Me.CloseButton.UseVisualStyleBackColor = True
        '
        'Label13
        '
        Me.Label13.AutoSize = True
        Me.Label13.Location = New System.Drawing.Point(230, 117)
        Me.Label13.Name = "Label13"
        Me.Label13.Size = New System.Drawing.Size(25, 15)
        Me.Label13.TabIndex = 16
        Me.Label13.Text = "タグ"
        '
        'PointDataGridView
        '
        Me.PointDataGridView.Anchor = CType(((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PointDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.PointDataGridView.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.SeqColumn, Me.PointClassificationColumn, Me.PointRateColumn, Me.FixedPointsColumn, Me.TotalPointColumn})
        Me.PointDataGridView.Location = New System.Drawing.Point(12, 458)
        Me.PointDataGridView.Name = "PointDataGridView"
        Me.PointDataGridView.RowTemplate.Height = 21
        Me.PointDataGridView.Size = New System.Drawing.Size(603, 82)
        Me.PointDataGridView.TabIndex = 19
        Me.PointDataGridView.Visible = False
        '
        'SeqColumn
        '
        Me.SeqColumn.HeaderText = "項番"
        Me.SeqColumn.Name = "SeqColumn"
        Me.SeqColumn.ReadOnly = True
        '
        'PointClassificationColumn
        '
        Me.PointClassificationColumn.DataPropertyName = "Name"
        Me.PointClassificationColumn.HeaderText = "種別"
        Me.PointClassificationColumn.Name = "PointClassificationColumn"
        '
        'PointRateColumn
        '
        Me.PointRateColumn.HeaderText = "還元率"
        Me.PointRateColumn.Name = "PointRateColumn"
        '
        'FixedPointsColumn
        '
        Me.FixedPointsColumn.HeaderText = "固定ポイント"
        Me.FixedPointsColumn.Name = "FixedPointsColumn"
        '
        'TotalPointColumn
        '
        Me.TotalPointColumn.HeaderText = "計ポイント"
        Me.TotalPointColumn.Name = "TotalPointColumn"
        Me.TotalPointColumn.ReadOnly = True
        '
        'CopyButton
        '
        Me.CopyButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.CopyButton.Location = New System.Drawing.Point(459, 575)
        Me.CopyButton.Name = "CopyButton"
        Me.CopyButton.Size = New System.Drawing.Size(75, 23)
        Me.CopyButton.TabIndex = 21
        Me.CopyButton.Text = "流用"
        Me.CopyButton.UseVisualStyleBackColor = True
        '
        'Label7
        '
        Me.Label7.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.Label7.AutoSize = True
        Me.Label7.Location = New System.Drawing.Point(478, 549)
        Me.Label7.Name = "Label7"
        Me.Label7.Size = New System.Drawing.Size(31, 15)
        Me.Label7.TabIndex = 24
        Me.Label7.Text = "合計"
        '
        'HelpLabel
        '
        Me.HelpLabel.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.HelpLabel.AutoSize = True
        Me.HelpLabel.Location = New System.Drawing.Point(596, 9)
        Me.HelpLabel.Name = "HelpLabel"
        Me.HelpLabel.Size = New System.Drawing.Size(19, 15)
        Me.HelpLabel.TabIndex = 26
        Me.HelpLabel.Text = "？"
        Me.HelpToolTip.SetToolTip(Me.HelpLabel, "Ctrl + Enter：登録" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Ctrl + T：タグにフォーカスを移動する" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "Ctrl + G：分類にフォーカスを移動して未選択状態にする")
        '
        'TotalTextBox2
        '
        Me.TotalTextBox2.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TotalTextBox2.Location = New System.Drawing.Point(515, 546)
        Me.TotalTextBox2.Name = "TotalTextBox2"
        Me.TotalTextBox2.ReadOnly = True
        Me.TotalTextBox2.Size = New System.Drawing.Size(100, 23)
        Me.TotalTextBox2.TabIndex = 25
        Me.TotalTextBox2.TabStop = False
        '
        'ReceiptTimeTextBox
        '
        Me.ReceiptTimeTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.ReceiptTimeTextBox.Location = New System.Drawing.Point(192, 29)
        Me.ReceiptTimeTextBox.Mode = OriginalControls.DateTimeMode.Time
        Me.ReceiptTimeTextBox.Name = "ReceiptTimeTextBox"
        Me.ReceiptTimeTextBox.Size = New System.Drawing.Size(100, 23)
        Me.ReceiptTimeTextBox.TabIndex = 4
        Me.ReceiptTimeTextBox.Text = "19:56:04"
        Me.ReceiptTimeTextBox.Value = New Date(2019, 3, 15, 19, 56, 4, 879)
        '
        'ReceiptDateTextBox
        '
        Me.ReceiptDateTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.ReceiptDateTextBox.Location = New System.Drawing.Point(86, 29)
        Me.ReceiptDateTextBox.Name = "ReceiptDateTextBox"
        Me.ReceiptDateTextBox.Size = New System.Drawing.Size(100, 23)
        Me.ReceiptDateTextBox.TabIndex = 3
        Me.ReceiptDateTextBox.Text = "2019/03/15"
        Me.ReceiptDateTextBox.Value = New Date(2019, 3, 15, 19, 56, 4, 881)
        '
        'ShopLocationComboBox
        '
        Me.ShopLocationComboBox.FormattingEnabled = True
        Me.ShopLocationComboBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.ShopLocationComboBox.Location = New System.Drawing.Point(213, 56)
        Me.ShopLocationComboBox.Name = "ShopLocationComboBox"
        Me.ShopLocationComboBox.Size = New System.Drawing.Size(169, 23)
        Me.ShopLocationComboBox.TabIndex = 7
        '
        'ReceiptTagTextBox
        '
        Me.ReceiptTagTextBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.ReceiptTagTextBox.Location = New System.Drawing.Point(261, 114)
        Me.ReceiptTagTextBox.Name = "ReceiptTagTextBox"
        Me.ReceiptTagTextBox.Size = New System.Drawing.Size(121, 23)
        Me.ReceiptTagTextBox.TabIndex = 17
        '
        'TaxInclusiveRadioButton
        '
        Me.TaxInclusiveRadioButton.AutoSize = True
        Me.TaxInclusiveRadioButton.Location = New System.Drawing.Point(86, 86)
        Me.TaxInclusiveRadioButton.Name = "TaxInclusiveRadioButton"
        Me.TaxInclusiveRadioButton.Size = New System.Drawing.Size(49, 19)
        Me.TaxInclusiveRadioButton.TabIndex = 9
        Me.TaxInclusiveRadioButton.Text = "内税"
        Me.TaxInclusiveRadioButton.UseVisualStyleBackColor = True
        '
        'TaxExclusiveRadioButton
        '
        Me.TaxExclusiveRadioButton.AutoSize = True
        Me.TaxExclusiveRadioButton.Location = New System.Drawing.Point(141, 86)
        Me.TaxExclusiveRadioButton.Name = "TaxExclusiveRadioButton"
        Me.TaxExclusiveRadioButton.Size = New System.Drawing.Size(49, 19)
        Me.TaxExclusiveRadioButton.TabIndex = 10
        Me.TaxExclusiveRadioButton.Text = "外税"
        Me.TaxExclusiveRadioButton.UseVisualStyleBackColor = True
        '
        'QuantityTextBox
        '
        Me.QuantityTextBox.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.QuantityTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.QuantityTextBox.Location = New System.Drawing.Point(235, 33)
        Me.QuantityTextBox.Name = "QuantityTextBox"
        Me.QuantityTextBox.Size = New System.Drawing.Size(54, 23)
        Me.QuantityTextBox.TabIndex = 5
        Me.QuantityTextBox.Text = "1"
        '
        'TaxTargetCheckBox
        '
        Me.TaxTargetCheckBox.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.TaxTargetCheckBox.AutoSize = True
        Me.TaxTargetCheckBox.Checked = True
        Me.TaxTargetCheckBox.CheckState = System.Windows.Forms.CheckState.Checked
        Me.TaxTargetCheckBox.Location = New System.Drawing.Point(227, 5)
        Me.TaxTargetCheckBox.Name = "TaxTargetCheckBox"
        Me.TaxTargetCheckBox.Size = New System.Drawing.Size(62, 19)
        Me.TaxTargetCheckBox.TabIndex = 14
        Me.TaxTargetCheckBox.Text = "税対象"
        Me.TaxTargetCheckBox.UseVisualStyleBackColor = True
        '
        'DetailTagTextBox
        '
        Me.DetailTagTextBox.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.DetailTagTextBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.DetailTagTextBox.Location = New System.Drawing.Point(52, 120)
        Me.DetailTagTextBox.Name = "DetailTagTextBox"
        Me.DetailTagTextBox.Size = New System.Drawing.Size(156, 23)
        Me.DetailTagTextBox.TabIndex = 11
        '
        'FeeTextBox
        '
        Me.FeeTextBox.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.FeeTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.FeeTextBox.Location = New System.Drawing.Point(52, 33)
        Me.FeeTextBox.Name = "FeeTextBox"
        Me.FeeTextBox.Size = New System.Drawing.Size(156, 23)
        Me.FeeTextBox.TabIndex = 3
        '
        'DiscountRateTextBox
        '
        Me.DiscountRateTextBox.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.DiscountRateTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.DiscountRateTextBox.Location = New System.Drawing.Point(52, 91)
        Me.DiscountRateTextBox.Name = "DiscountRateTextBox"
        Me.DiscountRateTextBox.Size = New System.Drawing.Size(156, 23)
        Me.DiscountRateTextBox.TabIndex = 9
        '
        'DiscountFeeTextBox
        '
        Me.DiscountFeeTextBox.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.DiscountFeeTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.DiscountFeeTextBox.Location = New System.Drawing.Point(52, 62)
        Me.DiscountFeeTextBox.Name = "DiscountFeeTextBox"
        Me.DiscountFeeTextBox.Size = New System.Drawing.Size(156, 23)
        Me.DiscountFeeTextBox.TabIndex = 7
        '
        'ItemNameComboBox
        '
        Me.ItemNameComboBox.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.ItemNameComboBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.ItemNameComboBox.Location = New System.Drawing.Point(52, 4)
        Me.ItemNameComboBox.Name = "ItemNameComboBox"
        Me.ItemNameComboBox.Size = New System.Drawing.Size(156, 23)
        Me.ItemNameComboBox.TabIndex = 1
        '
        'GroupTaxRateComboBox
        '
        Me.GroupTaxRateComboBox.DisplayMember = "Name"
        Me.GroupTaxRateComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.GroupTaxRateComboBox.FormattingEnabled = True
        Me.GroupTaxRateComboBox.Location = New System.Drawing.Point(39, 33)
        Me.GroupTaxRateComboBox.Name = "GroupTaxRateComboBox"
        Me.GroupTaxRateComboBox.Size = New System.Drawing.Size(170, 23)
        Me.GroupTaxRateComboBox.TabIndex = 3
        Me.GroupTaxRateComboBox.ValueMember = "Code"
        '
        'GroupTagTextBox
        '
        Me.GroupTagTextBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.GroupTagTextBox.Location = New System.Drawing.Point(39, 62)
        Me.GroupTagTextBox.Name = "GroupTagTextBox"
        Me.GroupTagTextBox.Size = New System.Drawing.Size(170, 23)
        Me.GroupTagTextBox.TabIndex = 5
        '
        'GroupClassificationComboBox
        '
        Me.GroupClassificationComboBox.DisplayMember = "Name"
        Me.GroupClassificationComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.GroupClassificationComboBox.FormattingEnabled = True
        Me.GroupClassificationComboBox.Location = New System.Drawing.Point(39, 4)
        Me.GroupClassificationComboBox.Name = "GroupClassificationComboBox"
        Me.GroupClassificationComboBox.Size = New System.Drawing.Size(170, 23)
        Me.GroupClassificationComboBox.TabIndex = 1
        Me.GroupClassificationComboBox.ValueMember = "Code"
        '
        'PaymentClassificationComboBox
        '
        Me.PaymentClassificationComboBox.DisplayMember = "Name"
        Me.PaymentClassificationComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.PaymentClassificationComboBox.FormattingEnabled = True
        Me.PaymentClassificationComboBox.Location = New System.Drawing.Point(86, 114)
        Me.PaymentClassificationComboBox.Name = "PaymentClassificationComboBox"
        Me.PaymentClassificationComboBox.Size = New System.Drawing.Size(121, 23)
        Me.PaymentClassificationComboBox.TabIndex = 15
        Me.PaymentClassificationComboBox.ValueMember = "Code"
        '
        'ShopNameComboBox
        '
        Me.ShopNameComboBox.DisplayMember = "Name"
        Me.ShopNameComboBox.FormattingEnabled = True
        Me.ShopNameComboBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.ShopNameComboBox.Location = New System.Drawing.Point(86, 56)
        Me.ShopNameComboBox.Name = "ShopNameComboBox"
        Me.ShopNameComboBox.Size = New System.Drawing.Size(121, 23)
        Me.ShopNameComboBox.TabIndex = 6
        '
        'TotalTextBox
        '
        Me.TotalTextBox.Location = New System.Drawing.Point(261, 85)
        Me.TotalTextBox.Name = "TotalTextBox"
        Me.TotalTextBox.ReadOnly = True
        Me.TotalTextBox.Size = New System.Drawing.Size(100, 23)
        Me.TotalTextBox.TabIndex = 13
        Me.TotalTextBox.TabStop = False
        '
        'UserListBox
        '
        Me.UserListBox.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.UserListBox.FormattingEnabled = True
        Me.UserListBox.ItemHeight = 15
        Me.UserListBox.Location = New System.Drawing.Point(52, 149)
        Me.UserListBox.Name = "UserListBox"
        Me.UserListBox.SelectionMode = System.Windows.Forms.SelectionMode.MultiSimple
        Me.UserListBox.Size = New System.Drawing.Size(156, 79)
        Me.UserListBox.TabIndex = 13
        '
        'RegisterForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(96.0!, 96.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        Me.ClientSize = New System.Drawing.Size(627, 610)
        Me.Controls.Add(Me.TaxRoundingModeLabel)
        Me.Controls.Add(Me.TaxRoundingModePanel)
        Me.Controls.Add(Me.HelpLabel)
        Me.Controls.Add(Me.TotalTextBox2)
        Me.Controls.Add(Me.Label7)
        Me.Controls.Add(Me.CopyButton)
        Me.Controls.Add(Me.ReceiptTimeTextBox)
        Me.Controls.Add(Me.ReceiptDateTextBox)
        Me.Controls.Add(Me.PointDataGridView)
        Me.Controls.Add(Me.ShopLocationComboBox)
        Me.Controls.Add(Me.ReceiptTagTextBox)
        Me.Controls.Add(Me.Label13)
        Me.Controls.Add(Me.TaxInclusiveRadioButton)
        Me.Controls.Add(Me.TaxExclusiveRadioButton)
        Me.Controls.Add(Me.CloseButton)
        Me.Controls.Add(Me.RegiserButton)
        Me.Controls.Add(Me.DeleteButton)
        Me.Controls.Add(Me.GroupBox1)
        Me.Controls.Add(Me.PaymentClassificationComboBox)
        Me.Controls.Add(Me.Label15)
        Me.Controls.Add(Me.Label6)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.Label2)
        Me.Controls.Add(Me.ShopNameComboBox)
        Me.Controls.Add(Me.SeqLabel)
        Me.Controls.Add(Me.Label4)
        Me.Controls.Add(Me.TotalTextBox)
        Me.Controls.Add(Me.Label5)
        Me.Font = New System.Drawing.Font("Yu Gothic UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(128, Byte))
        Me.KeyPreview = True
        Me.Name = "RegisterForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "登録"
        Me.TaxRoundingModePanel.ResumeLayout(False)
        Me.TaxRoundingModePanel.PerformLayout()
        Me.GroupBox1.ResumeLayout(False)
        Me.DetailPanel.ResumeLayout(False)
        Me.DetailPanel.PerformLayout()
        Me.GroupPanel.ResumeLayout(False)
        Me.GroupPanel.PerformLayout()
        CType(Me.PointDataGridView, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents Label1 As Label
    Friend WithEvents Label2 As Label
    Friend WithEvents Label4 As Label
    Friend WithEvents Label5 As Label
    Friend WithEvents TaxRoundingModeLabel As Label
    Friend WithEvents TaxRoundingModePanel As Panel
    Friend WithEvents TaxRoundingFloorRadioButton As OriginalControls.OriginalRadioButton
    Friend WithEvents TaxRoundingHalfUpRadioButton As OriginalControls.OriginalRadioButton
    Friend WithEvents GroupBox1 As GroupBox
    Friend WithEvents Label14 As Label
    Friend WithEvents Label10 As Label
    Friend WithEvents RemoveDetailButton As Button
    Friend WithEvents AddDetailButton As Button
    Friend WithEvents Label11 As Label
    Friend WithEvents Label9 As Label
    Friend WithEvents Label8 As Label
    Friend WithEvents Label6 As Label
    Friend WithEvents SeqLabel As Label
    Friend WithEvents Label12 As Label
    Friend WithEvents Label15 As Label
    Friend WithEvents RemoveGroupButton As Button
    Friend WithEvents AddGroupButton As Button
    Friend WithEvents GroupClassificationCodeDataGridViewTextBoxColumn As DataGridViewTextBoxColumn
    Friend WithEvents DataGridViewComboBoxColumn1 As DataGridViewComboBoxColumn
    Friend WithEvents GroupListView As ListView
    Friend WithEvents ClassificationNameColumnHeader As ColumnHeader
    Friend WithEvents TagColumnHeader As ColumnHeader
    Friend WithEvents FeeColumnHeader As ColumnHeader
    Friend WithEvents DetailListView As ListView
    Friend WithEvents ItemNameColumnHeader As ColumnHeader
    Friend WithEvents DetailFeeColumnHeader As ColumnHeader
    Friend WithEvents DetailTagColumnHeader As ColumnHeader
    Friend WithEvents DeleteButton As Button
    Friend WithEvents RegiserButton As Button
    Friend WithEvents CloseButton As Button
    Friend WithEvents GroupPanel As Panel
    Friend WithEvents DetailPanel As Panel
    Friend WithEvents Label13 As Label
    Friend WithEvents Label16 As Label
    Friend WithEvents Label17 As Label
    Friend WithEvents ShopNameComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents TotalTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents TaxTargetCheckBox As OriginalControls.OriginalCheckBox
    Friend WithEvents FeeTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents ItemNameComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents GroupClassificationComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents DiscountFeeTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents PaymentClassificationComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents GroupTagTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents DetailTagTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents ReceiptTagTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents QuantityTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents DiscountRateTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents ShopLocationComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents PointDataGridView As DataGridView
    Friend WithEvents TaxExclusiveRadioButton As OriginalControls.OriginalRadioButton
    Friend WithEvents TaxInclusiveRadioButton As OriginalControls.OriginalRadioButton
    Friend WithEvents ReceiptDateTextBox As OriginalControls.OriginalDateTimeTextBox
    Friend WithEvents ReceiptTimeTextBox As OriginalControls.OriginalDateTimeTextBox
    Friend WithEvents SeqColumn As DataGridViewTextBoxColumn
    Friend WithEvents PointClassificationColumn As DataGridViewComboBoxColumn
    Friend WithEvents PointRateColumn As DataGridViewTextBoxColumn
    Friend WithEvents FixedPointsColumn As DataGridViewTextBoxColumn
    Friend WithEvents TotalPointColumn As DataGridViewTextBoxColumn
    Friend WithEvents CopyButton As Button
    Friend WithEvents Label3 As Label
    Friend WithEvents GroupTaxRateComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents Label7 As Label
    Friend WithEvents TotalTextBox2 As OriginalControls.OriginalTextBox
    Friend WithEvents HelpToolTip As ToolTip
    Friend WithEvents HelpLabel As Label
    Friend WithEvents Label18 As Label
    Friend WithEvents UserListBox As OriginalControls.OriginalListBox
End Class
