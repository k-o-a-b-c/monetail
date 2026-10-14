<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class RegisterPaymentMemoForm
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
        Dim DataGridViewCellStyle1 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Dim DataGridViewCellStyle2 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Dim DataGridViewCellStyle3 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Me.CloseButton = New System.Windows.Forms.Button()
        Me.PaymentMemoDataGridView = New System.Windows.Forms.DataGridView()
        Me.PaymentClassificationColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.AutoBalanceColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.CheckDateColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.SetTodayColumn = New System.Windows.Forms.DataGridViewButtonColumn()
        Me.TagColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.HelpToolTip = New System.Windows.Forms.ToolTip(Me.components)
        Me.HelpLabel = New System.Windows.Forms.Label()
        Me.Label2 = New System.Windows.Forms.Label()
        Me.TopMostCheckBox = New OriginalControls.OriginalCheckBox()
        CType(Me.PaymentMemoDataGridView, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'CloseButton
        '
        Me.CloseButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.CloseButton.Location = New System.Drawing.Point(12, 646)
        Me.CloseButton.Name = "CloseButton"
        Me.CloseButton.Size = New System.Drawing.Size(75, 23)
        Me.CloseButton.TabIndex = 5
        Me.CloseButton.Text = "閉じる"
        Me.CloseButton.UseVisualStyleBackColor = True
        '
        'PaymentMemoDataGridView
        '
        Me.PaymentMemoDataGridView.AllowUserToAddRows = False
        Me.PaymentMemoDataGridView.AllowUserToDeleteRows = False
        Me.PaymentMemoDataGridView.AllowUserToResizeRows = False
        Me.PaymentMemoDataGridView.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.PaymentMemoDataGridView.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.DisplayedCells
        DataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft
        DataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Control
        DataGridViewCellStyle1.Font = New System.Drawing.Font("Yu Gothic UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(128, Byte))
        DataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText
        DataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight
        DataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText
        DataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.[False]
        Me.PaymentMemoDataGridView.ColumnHeadersDefaultCellStyle = DataGridViewCellStyle1
        Me.PaymentMemoDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.PaymentMemoDataGridView.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.PaymentClassificationColumn, Me.AutoBalanceColumn, Me.CheckDateColumn, Me.SetTodayColumn, Me.TagColumn})
        Me.PaymentMemoDataGridView.Location = New System.Drawing.Point(12, 27)
        Me.PaymentMemoDataGridView.Name = "PaymentMemoDataGridView"
        Me.PaymentMemoDataGridView.RowHeadersVisible = False
        Me.PaymentMemoDataGridView.RowTemplate.Height = 21
        Me.PaymentMemoDataGridView.Size = New System.Drawing.Size(440, 613)
        Me.PaymentMemoDataGridView.TabIndex = 3
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
        'AutoBalanceColumn
        '
        DataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight
        DataGridViewCellStyle2.Format = "N0"
        DataGridViewCellStyle2.NullValue = Nothing
        Me.AutoBalanceColumn.DefaultCellStyle = DataGridViewCellStyle2
        Me.AutoBalanceColumn.HeaderText = "自動計算"
        Me.AutoBalanceColumn.Name = "AutoBalanceColumn"
        Me.AutoBalanceColumn.ReadOnly = True
        Me.AutoBalanceColumn.Width = 160
        '
        'CheckDateColumn
        '
        DataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight
        DataGridViewCellStyle3.Format = "d"
        DataGridViewCellStyle3.NullValue = Nothing
        Me.CheckDateColumn.DefaultCellStyle = DataGridViewCellStyle3
        Me.CheckDateColumn.HeaderText = "確認日"
        Me.CheckDateColumn.Name = "CheckDateColumn"
        Me.CheckDateColumn.Width = 180
        '
        'SetTodayColumn
        '
        Me.SetTodayColumn.HeaderText = "本日"
        Me.SetTodayColumn.Name = "SetTodayColumn"
        Me.SetTodayColumn.ReadOnly = True
        '
        'TagColumn
        '
        Me.TagColumn.HeaderText = "タグ"
        Me.TagColumn.Name = "TagColumn"
        Me.TagColumn.Width = 260
        '
        'HelpLabel
        '
        Me.HelpLabel.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.HelpLabel.AutoSize = True
        Me.HelpLabel.Location = New System.Drawing.Point(433, 9)
        Me.HelpLabel.Name = "HelpLabel"
        Me.HelpLabel.Size = New System.Drawing.Size(19, 15)
        Me.HelpLabel.TabIndex = 2
        Me.HelpLabel.Text = "？"
        Me.HelpToolTip.SetToolTip(Me.HelpLabel, "Ctrl + Enter：登録")
        '
        'Label2
        '
        Me.Label2.AutoSize = True
        Me.Label2.Location = New System.Drawing.Point(12, 9)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(24, 15)
        Me.Label2.TabIndex = 1
        Me.Label2.Text = "メモ"
        '
        'TopMostCheckBox
        '
        Me.TopMostCheckBox.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.TopMostCheckBox.AutoSize = True
        Me.TopMostCheckBox.Checked = True
        Me.TopMostCheckBox.CheckState = System.Windows.Forms.CheckState.Checked
        Me.TopMostCheckBox.Location = New System.Drawing.Point(93, 649)
        Me.TopMostCheckBox.Name = "TopMostCheckBox"
        Me.TopMostCheckBox.Size = New System.Drawing.Size(104, 19)
        Me.TopMostCheckBox.TabIndex = 6
        Me.TopMostCheckBox.Text = "常に手前に表示"
        Me.TopMostCheckBox.UseVisualStyleBackColor = True
        '
        'RegisterPaymentMemoForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(96.0!, 96.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        Me.ClientSize = New System.Drawing.Size(464, 681)
        Me.Controls.Add(Me.TopMostCheckBox)
        Me.Controls.Add(Me.HelpLabel)
        Me.Controls.Add(Me.PaymentMemoDataGridView)
        Me.Controls.Add(Me.CloseButton)
        Me.Controls.Add(Me.Label2)
        Me.Font = New System.Drawing.Font("Yu Gothic UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(128, Byte))
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(4)
        Me.Name = "RegisterPaymentMemoForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        Me.Text = "支払いメモ"
        CType(Me.PaymentMemoDataGridView, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents CloseButton As Button
    Friend WithEvents PaymentMemoDataGridView As DataGridView
    Friend WithEvents HelpToolTip As ToolTip
    Friend WithEvents HelpLabel As Label
    Friend WithEvents Label2 As Label
    Friend WithEvents TopMostCheckBox As OriginalControls.OriginalCheckBox
    Friend WithEvents PaymentClassificationColumn As DataGridViewTextBoxColumn
    Friend WithEvents AutoBalanceColumn As DataGridViewTextBoxColumn
    Friend WithEvents CheckDateColumn As DataGridViewTextBoxColumn
    Friend WithEvents SetTodayColumn As DataGridViewButtonColumn
    Friend WithEvents TagColumn As DataGridViewTextBoxColumn
End Class
