<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class RegisterBalanceForm
    Inherits System.Windows.Forms.Form

    'フォームがコンポーネントの一覧をクリーンアップするために dispose をオーバーライドします。
    <System.Diagnostics.DebuggerNonUserCode()> _
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
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()
        Dim DataGridViewCellStyle1 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Dim DataGridViewCellStyle2 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Dim DataGridViewCellStyle3 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Dim DataGridViewCellStyle4 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Me.Label6 = New System.Windows.Forms.Label()
        Me.SeqLabel = New System.Windows.Forms.Label()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.BalanceDateTextBox = New OriginalControls.OriginalDateTimeTextBox()
        Me.Label13 = New System.Windows.Forms.Label()
        Me.BalanceTagTextBox = New OriginalControls.OriginalTextBox()
        Me.Label2 = New System.Windows.Forms.Label()
        Me.CloseButton = New System.Windows.Forms.Button()
        Me.RegiserButton = New System.Windows.Forms.Button()
        Me.DeleteButton = New System.Windows.Forms.Button()
        Me.BalanceDataGridView = New System.Windows.Forms.DataGridView()
        Me.HelpToolTip = New System.Windows.Forms.ToolTip(Me.components)
        Me.HelpLabel = New System.Windows.Forms.Label()
        Me.PaymentClassificationColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.BalanceColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.AutoBalanceColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.CopyAutoBalanceColumn = New System.Windows.Forms.DataGridViewButtonColumn()
        Me.ValuationAmountColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.TagColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        CType(Me.BalanceDataGridView, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'Label6
        '
        Me.Label6.AutoSize = True
        Me.Label6.Location = New System.Drawing.Point(13, 9)
        Me.Label6.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.Label6.Name = "Label6"
        Me.Label6.Size = New System.Drawing.Size(55, 15)
        Me.Label6.TabIndex = 1
        Me.Label6.Text = "登録番号"
        '
        'SeqLabel
        '
        Me.SeqLabel.AutoSize = True
        Me.SeqLabel.Location = New System.Drawing.Point(76, 9)
        Me.SeqLabel.Margin = New System.Windows.Forms.Padding(4, 0, 4, 0)
        Me.SeqLabel.Name = "SeqLabel"
        Me.SeqLabel.Size = New System.Drawing.Size(55, 15)
        Me.SeqLabel.TabIndex = 2
        Me.SeqLabel.Text = "00000000"
        '
        'Label1
        '
        Me.Label1.AutoSize = True
        Me.Label1.Location = New System.Drawing.Point(37, 30)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(31, 15)
        Me.Label1.TabIndex = 3
        Me.Label1.Text = "日付"
        '
        'BalanceDateTextBox
        '
        Me.BalanceDateTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.BalanceDateTextBox.Location = New System.Drawing.Point(79, 27)
        Me.BalanceDateTextBox.Name = "BalanceDateTextBox"
        Me.BalanceDateTextBox.Size = New System.Drawing.Size(100, 23)
        Me.BalanceDateTextBox.TabIndex = 4
        Me.BalanceDateTextBox.Text = "2019/03/15"
        Me.BalanceDateTextBox.Value = New Date(2019, 3, 15, 19, 56, 4, 881)
        '
        'Label13
        '
        Me.Label13.AutoSize = True
        Me.Label13.Location = New System.Drawing.Point(43, 59)
        Me.Label13.Name = "Label13"
        Me.Label13.Size = New System.Drawing.Size(25, 15)
        Me.Label13.TabIndex = 5
        Me.Label13.Text = "タグ"
        '
        'BalanceTagTextBox
        '
        Me.BalanceTagTextBox.Anchor = CType(((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BalanceTagTextBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.BalanceTagTextBox.Location = New System.Drawing.Point(79, 56)
        Me.BalanceTagTextBox.Name = "BalanceTagTextBox"
        Me.BalanceTagTextBox.Size = New System.Drawing.Size(493, 23)
        Me.BalanceTagTextBox.TabIndex = 6
        '
        'Label2
        '
        Me.Label2.AutoSize = True
        Me.Label2.Location = New System.Drawing.Point(37, 85)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(31, 15)
        Me.Label2.TabIndex = 7
        Me.Label2.Text = "残高"
        '
        'CloseButton
        '
        Me.CloseButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.CloseButton.Location = New System.Drawing.Point(12, 646)
        Me.CloseButton.Name = "CloseButton"
        Me.CloseButton.Size = New System.Drawing.Size(75, 23)
        Me.CloseButton.TabIndex = 10
        Me.CloseButton.Text = "閉じる"
        Me.CloseButton.UseVisualStyleBackColor = True
        '
        'RegiserButton
        '
        Me.RegiserButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.RegiserButton.Location = New System.Drawing.Point(497, 646)
        Me.RegiserButton.Name = "RegiserButton"
        Me.RegiserButton.Size = New System.Drawing.Size(75, 23)
        Me.RegiserButton.TabIndex = 9
        Me.RegiserButton.Text = "登録"
        Me.RegiserButton.UseVisualStyleBackColor = True
        '
        'DeleteButton
        '
        Me.DeleteButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.DeleteButton.Location = New System.Drawing.Point(93, 646)
        Me.DeleteButton.Name = "DeleteButton"
        Me.DeleteButton.Size = New System.Drawing.Size(75, 23)
        Me.DeleteButton.TabIndex = 11
        Me.DeleteButton.Text = "削除"
        Me.DeleteButton.UseVisualStyleBackColor = True
        '
        'BalanceDataGridView
        '
        Me.BalanceDataGridView.AllowUserToAddRows = False
        Me.BalanceDataGridView.AllowUserToDeleteRows = False
        Me.BalanceDataGridView.AllowUserToResizeRows = False
        Me.BalanceDataGridView.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.BalanceDataGridView.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.DisplayedCells
        DataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft
        DataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Control
        DataGridViewCellStyle1.Font = New System.Drawing.Font("Yu Gothic UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(128, Byte))
        DataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText
        DataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight
        DataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText
        DataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.[False]
        Me.BalanceDataGridView.ColumnHeadersDefaultCellStyle = DataGridViewCellStyle1
        Me.BalanceDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.BalanceDataGridView.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.PaymentClassificationColumn, Me.BalanceColumn, Me.AutoBalanceColumn, Me.CopyAutoBalanceColumn, Me.ValuationAmountColumn, Me.TagColumn})
        Me.BalanceDataGridView.Location = New System.Drawing.Point(79, 85)
        Me.BalanceDataGridView.Name = "BalanceDataGridView"
        Me.BalanceDataGridView.RowHeadersVisible = False
        Me.BalanceDataGridView.RowTemplate.Height = 21
        Me.BalanceDataGridView.Size = New System.Drawing.Size(493, 555)
        Me.BalanceDataGridView.TabIndex = 8
        '
        'HelpLabel
        '
        Me.HelpLabel.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.HelpLabel.AutoSize = True
        Me.HelpLabel.Location = New System.Drawing.Point(553, 9)
        Me.HelpLabel.Name = "HelpLabel"
        Me.HelpLabel.Size = New System.Drawing.Size(19, 15)
        Me.HelpLabel.TabIndex = 27
        Me.HelpLabel.Text = "？"
        Me.HelpToolTip.SetToolTip(Me.HelpLabel, "Ctrl + Enter：登録")
        '
        'PaymentClassificationColumn
        '
        Me.PaymentClassificationColumn.DataPropertyName = "Name"
        Me.PaymentClassificationColumn.Frozen = True
        Me.PaymentClassificationColumn.HeaderText = "支払い方法"
        Me.PaymentClassificationColumn.Name = "PaymentClassificationColumn"
        Me.PaymentClassificationColumn.ReadOnly = True
        Me.PaymentClassificationColumn.Width = 400
        '
        'BalanceColumn
        '
        DataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight
        DataGridViewCellStyle2.Format = "N0"
        DataGridViewCellStyle2.NullValue = Nothing
        Me.BalanceColumn.DefaultCellStyle = DataGridViewCellStyle2
        Me.BalanceColumn.HeaderText = "残高"
        Me.BalanceColumn.Name = "BalanceColumn"
        Me.BalanceColumn.Width = 160
        '
        'AutoBalanceColumn
        '
        DataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight
        DataGridViewCellStyle3.Format = "N0"
        DataGridViewCellStyle3.NullValue = Nothing
        Me.AutoBalanceColumn.DefaultCellStyle = DataGridViewCellStyle3
        Me.AutoBalanceColumn.HeaderText = "自動計算"
        Me.AutoBalanceColumn.Name = "AutoBalanceColumn"
        Me.AutoBalanceColumn.ReadOnly = True
        Me.AutoBalanceColumn.Width = 160
        '
        'CopyAutoBalanceColumn
        '
        Me.CopyAutoBalanceColumn.HeaderText = "反映"
        Me.CopyAutoBalanceColumn.Name = "CopyAutoBalanceColumn"
        Me.CopyAutoBalanceColumn.ReadOnly = True
        '
        'ValuationAmountColumn
        '
        DataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight
        DataGridViewCellStyle4.Format = "N0"
        DataGridViewCellStyle4.NullValue = Nothing
        Me.ValuationAmountColumn.DefaultCellStyle = DataGridViewCellStyle4
        Me.ValuationAmountColumn.HeaderText = "評価額"
        Me.ValuationAmountColumn.Name = "ValuationAmountColumn"
        Me.ValuationAmountColumn.Width = 160
        '
        'TagColumn
        '
        Me.TagColumn.HeaderText = "タグ"
        Me.TagColumn.Name = "TagColumn"
        '
        'RegisterBalanceForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(96.0!, 96.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        Me.ClientSize = New System.Drawing.Size(584, 681)
        Me.Controls.Add(Me.HelpLabel)
        Me.Controls.Add(Me.BalanceDataGridView)
        Me.Controls.Add(Me.CloseButton)
        Me.Controls.Add(Me.RegiserButton)
        Me.Controls.Add(Me.DeleteButton)
        Me.Controls.Add(Me.Label2)
        Me.Controls.Add(Me.BalanceTagTextBox)
        Me.Controls.Add(Me.Label13)
        Me.Controls.Add(Me.BalanceDateTextBox)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.SeqLabel)
        Me.Controls.Add(Me.Label6)
        Me.Font = New System.Drawing.Font("Yu Gothic UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(128, Byte))
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(4)
        Me.Name = "RegisterBalanceForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "残高登録"
        CType(Me.BalanceDataGridView, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents Label6 As Label
    Friend WithEvents SeqLabel As Label
    Friend WithEvents Label1 As Label
    Friend WithEvents BalanceDateTextBox As OriginalControls.OriginalDateTimeTextBox
    Friend WithEvents Label13 As Label
    Friend WithEvents BalanceTagTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents Label2 As Label
    Friend WithEvents CloseButton As Button
    Friend WithEvents RegiserButton As Button
    Friend WithEvents DeleteButton As Button
    Friend WithEvents BalanceDataGridView As DataGridView
    Friend WithEvents HelpToolTip As ToolTip
    Friend WithEvents HelpLabel As Label
    Friend WithEvents PaymentClassificationColumn As DataGridViewTextBoxColumn
    Friend WithEvents BalanceColumn As DataGridViewTextBoxColumn
    Friend WithEvents AutoBalanceColumn As DataGridViewTextBoxColumn
    Friend WithEvents CopyAutoBalanceColumn As DataGridViewButtonColumn
    Friend WithEvents ValuationAmountColumn As DataGridViewTextBoxColumn
    Friend WithEvents TagColumn As DataGridViewTextBoxColumn
End Class
