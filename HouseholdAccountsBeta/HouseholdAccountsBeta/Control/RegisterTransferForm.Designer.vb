<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class RegisterTransferForm
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
        Me.TransferTagTextBox = New OriginalControls.OriginalTextBox()
        Me.Label13 = New System.Windows.Forms.Label()
        Me.FromPaymentClassificationComboBox = New OriginalControls.OriginalComboBox()
        Me.Label15 = New System.Windows.Forms.Label()
        Me.Label6 = New System.Windows.Forms.Label()
        Me.SeqLabel = New System.Windows.Forms.Label()
        Me.FeeTextBox = New OriginalControls.OriginalTextBox()
        Me.Label5 = New System.Windows.Forms.Label()
        Me.ToPaymentClassificationComboBox = New OriginalControls.OriginalComboBox()
        Me.Label1 = New System.Windows.Forms.Label()
        Me.CloseButton = New System.Windows.Forms.Button()
        Me.RegiserButton = New System.Windows.Forms.Button()
        Me.DeleteButton = New System.Windows.Forms.Button()
        Me.TransferTimeTextBox = New OriginalControls.OriginalDateTimeTextBox()
        Me.TransferDateTextBox = New OriginalControls.OriginalDateTimeTextBox()
        Me.Label2 = New System.Windows.Forms.Label()
        Me.Label3 = New System.Windows.Forms.Label()
        Me.NameComboBox = New OriginalControls.OriginalComboBox()
        Me.CopyButton = New System.Windows.Forms.Button()
        Me.HelpLabel = New System.Windows.Forms.Label()
        Me.HelpToolTip = New System.Windows.Forms.ToolTip(Me.components)
        Me.SuspendLayout()
        '
        'TransferTagTextBox
        '
        Me.TransferTagTextBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.TransferTagTextBox.Location = New System.Drawing.Point(76, 183)
        Me.TransferTagTextBox.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.TransferTagTextBox.Name = "TransferTagTextBox"
        Me.TransferTagTextBox.Size = New System.Drawing.Size(296, 23)
        Me.TransferTagTextBox.TabIndex = 14
        '
        'Label13
        '
        Me.Label13.AutoSize = True
        Me.Label13.Location = New System.Drawing.Point(12, 186)
        Me.Label13.Name = "Label13"
        Me.Label13.Size = New System.Drawing.Size(25, 15)
        Me.Label13.TabIndex = 13
        Me.Label13.Text = "タグ"
        '
        'FromPaymentClassificationComboBox
        '
        Me.FromPaymentClassificationComboBox.DisplayMember = "Name"
        Me.FromPaymentClassificationComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.FromPaymentClassificationComboBox.FormattingEnabled = True
        Me.FromPaymentClassificationComboBox.Location = New System.Drawing.Point(76, 90)
        Me.FromPaymentClassificationComboBox.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.FromPaymentClassificationComboBox.Name = "FromPaymentClassificationComboBox"
        Me.FromPaymentClassificationComboBox.Size = New System.Drawing.Size(140, 23)
        Me.FromPaymentClassificationComboBox.TabIndex = 8
        Me.FromPaymentClassificationComboBox.ValueMember = "Code"
        '
        'Label15
        '
        Me.Label15.AutoSize = True
        Me.Label15.Location = New System.Drawing.Point(12, 93)
        Me.Label15.Name = "Label15"
        Me.Label15.Size = New System.Drawing.Size(43, 15)
        Me.Label15.TabIndex = 7
        Me.Label15.Text = "移動元"
        '
        'Label6
        '
        Me.Label6.AutoSize = True
        Me.Label6.Location = New System.Drawing.Point(12, 9)
        Me.Label6.Name = "Label6"
        Me.Label6.Size = New System.Drawing.Size(55, 15)
        Me.Label6.TabIndex = 18
        Me.Label6.Text = "登録番号"
        '
        'SeqLabel
        '
        Me.SeqLabel.AutoSize = True
        Me.SeqLabel.Location = New System.Drawing.Point(73, 9)
        Me.SeqLabel.Name = "SeqLabel"
        Me.SeqLabel.Size = New System.Drawing.Size(55, 15)
        Me.SeqLabel.TabIndex = 1
        Me.SeqLabel.Text = "00000000"
        '
        'FeeTextBox
        '
        Me.FeeTextBox.Location = New System.Drawing.Point(76, 152)
        Me.FeeTextBox.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.FeeTextBox.Name = "FeeTextBox"
        Me.FeeTextBox.Size = New System.Drawing.Size(116, 23)
        Me.FeeTextBox.TabIndex = 12
        '
        'Label5
        '
        Me.Label5.AutoSize = True
        Me.Label5.Location = New System.Drawing.Point(12, 155)
        Me.Label5.Name = "Label5"
        Me.Label5.Size = New System.Drawing.Size(31, 15)
        Me.Label5.TabIndex = 11
        Me.Label5.Text = "金額"
        '
        'ToPaymentClassificationComboBox
        '
        Me.ToPaymentClassificationComboBox.DisplayMember = "Name"
        Me.ToPaymentClassificationComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ToPaymentClassificationComboBox.FormattingEnabled = True
        Me.ToPaymentClassificationComboBox.Location = New System.Drawing.Point(76, 121)
        Me.ToPaymentClassificationComboBox.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.ToPaymentClassificationComboBox.Name = "ToPaymentClassificationComboBox"
        Me.ToPaymentClassificationComboBox.Size = New System.Drawing.Size(140, 23)
        Me.ToPaymentClassificationComboBox.TabIndex = 10
        Me.ToPaymentClassificationComboBox.ValueMember = "Code"
        '
        'Label1
        '
        Me.Label1.AutoSize = True
        Me.Label1.Location = New System.Drawing.Point(12, 124)
        Me.Label1.Name = "Label1"
        Me.Label1.Size = New System.Drawing.Size(43, 15)
        Me.Label1.TabIndex = 9
        Me.Label1.Text = "移動先"
        '
        'CloseButton
        '
        Me.CloseButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.CloseButton.Location = New System.Drawing.Point(12, 214)
        Me.CloseButton.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.CloseButton.Name = "CloseButton"
        Me.CloseButton.Size = New System.Drawing.Size(75, 23)
        Me.CloseButton.TabIndex = 16
        Me.CloseButton.Text = "閉じる"
        Me.CloseButton.UseVisualStyleBackColor = True
        '
        'RegiserButton
        '
        Me.RegiserButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.RegiserButton.Location = New System.Drawing.Point(297, 214)
        Me.RegiserButton.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.RegiserButton.Name = "RegiserButton"
        Me.RegiserButton.Size = New System.Drawing.Size(75, 23)
        Me.RegiserButton.TabIndex = 15
        Me.RegiserButton.Text = "登録"
        Me.RegiserButton.UseVisualStyleBackColor = True
        '
        'DeleteButton
        '
        Me.DeleteButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Left), System.Windows.Forms.AnchorStyles)
        Me.DeleteButton.Location = New System.Drawing.Point(93, 214)
        Me.DeleteButton.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.DeleteButton.Name = "DeleteButton"
        Me.DeleteButton.Size = New System.Drawing.Size(75, 23)
        Me.DeleteButton.TabIndex = 17
        Me.DeleteButton.Text = "削除"
        Me.DeleteButton.UseVisualStyleBackColor = True
        '
        'TransferTimeTextBox
        '
        Me.TransferTimeTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.TransferTimeTextBox.Location = New System.Drawing.Point(182, 28)
        Me.TransferTimeTextBox.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.TransferTimeTextBox.Mode = OriginalControls.DateTimeMode.Time
        Me.TransferTimeTextBox.Name = "TransferTimeTextBox"
        Me.TransferTimeTextBox.Size = New System.Drawing.Size(100, 23)
        Me.TransferTimeTextBox.TabIndex = 4
        Me.TransferTimeTextBox.Text = "19:56:04"
        Me.TransferTimeTextBox.Value = New Date(2019, 3, 15, 19, 56, 4, 879)
        '
        'TransferDateTextBox
        '
        Me.TransferDateTextBox.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.TransferDateTextBox.Location = New System.Drawing.Point(76, 28)
        Me.TransferDateTextBox.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.TransferDateTextBox.Name = "TransferDateTextBox"
        Me.TransferDateTextBox.Size = New System.Drawing.Size(100, 23)
        Me.TransferDateTextBox.TabIndex = 3
        Me.TransferDateTextBox.Text = "2019/03/15"
        Me.TransferDateTextBox.Value = New Date(2019, 3, 15, 19, 56, 4, 881)
        '
        'Label2
        '
        Me.Label2.AutoSize = True
        Me.Label2.Location = New System.Drawing.Point(12, 31)
        Me.Label2.Name = "Label2"
        Me.Label2.Size = New System.Drawing.Size(31, 15)
        Me.Label2.TabIndex = 2
        Me.Label2.Text = "日付"
        '
        'Label3
        '
        Me.Label3.AutoSize = True
        Me.Label3.Location = New System.Drawing.Point(12, 62)
        Me.Label3.Name = "Label3"
        Me.Label3.Size = New System.Drawing.Size(31, 15)
        Me.Label3.TabIndex = 5
        Me.Label3.Text = "名目"
        '
        'NameComboBox
        '
        Me.NameComboBox.DisplayMember = "Name"
        Me.NameComboBox.FormattingEnabled = True
        Me.NameComboBox.ImeMode = System.Windows.Forms.ImeMode.[On]
        Me.NameComboBox.Location = New System.Drawing.Point(76, 59)
        Me.NameComboBox.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.NameComboBox.Name = "NameComboBox"
        Me.NameComboBox.Size = New System.Drawing.Size(140, 23)
        Me.NameComboBox.TabIndex = 6
        '
        'CopyButton
        '
        Me.CopyButton.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.CopyButton.Location = New System.Drawing.Point(216, 214)
        Me.CopyButton.Name = "CopyButton"
        Me.CopyButton.Size = New System.Drawing.Size(75, 23)
        Me.CopyButton.TabIndex = 22
        Me.CopyButton.Text = "流用"
        Me.CopyButton.UseVisualStyleBackColor = True
        '
        'HelpLabel
        '
        Me.HelpLabel.Anchor = CType((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.HelpLabel.AutoSize = True
        Me.HelpLabel.Location = New System.Drawing.Point(353, 9)
        Me.HelpLabel.Name = "HelpLabel"
        Me.HelpLabel.Size = New System.Drawing.Size(19, 15)
        Me.HelpLabel.TabIndex = 27
        Me.HelpLabel.Text = "？"
        Me.HelpToolTip.SetToolTip(Me.HelpLabel, "Ctrl + Enter：登録")
        '
        'TransferForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(96.0!, 96.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        Me.ClientSize = New System.Drawing.Size(384, 250)
        Me.Controls.Add(Me.HelpLabel)
        Me.Controls.Add(Me.CopyButton)
        Me.Controls.Add(Me.TransferTimeTextBox)
        Me.Controls.Add(Me.TransferDateTextBox)
        Me.Controls.Add(Me.Label2)
        Me.Controls.Add(Me.Label3)
        Me.Controls.Add(Me.NameComboBox)
        Me.Controls.Add(Me.CloseButton)
        Me.Controls.Add(Me.RegiserButton)
        Me.Controls.Add(Me.DeleteButton)
        Me.Controls.Add(Me.ToPaymentClassificationComboBox)
        Me.Controls.Add(Me.Label1)
        Me.Controls.Add(Me.TransferTagTextBox)
        Me.Controls.Add(Me.Label13)
        Me.Controls.Add(Me.FromPaymentClassificationComboBox)
        Me.Controls.Add(Me.Label15)
        Me.Controls.Add(Me.Label6)
        Me.Controls.Add(Me.SeqLabel)
        Me.Controls.Add(Me.FeeTextBox)
        Me.Controls.Add(Me.Label5)
        Me.Font = New System.Drawing.Font("Yu Gothic UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(128, Byte))
        Me.KeyPreview = True
        Me.Margin = New System.Windows.Forms.Padding(3, 4, 3, 4)
        Me.Name = "TransferForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "登録"
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents TransferTagTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents Label13 As Label
    Friend WithEvents FromPaymentClassificationComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents Label15 As Label
    Friend WithEvents Label6 As Label
    Friend WithEvents SeqLabel As Label
    Friend WithEvents FeeTextBox As OriginalControls.OriginalTextBox
    Friend WithEvents Label5 As Label
    Friend WithEvents ToPaymentClassificationComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents Label1 As Label
    Friend WithEvents CloseButton As Button
    Friend WithEvents RegiserButton As Button
    Friend WithEvents DeleteButton As Button
    Friend WithEvents TransferTimeTextBox As OriginalControls.OriginalDateTimeTextBox
    Friend WithEvents TransferDateTextBox As OriginalControls.OriginalDateTimeTextBox
    Friend WithEvents Label2 As Label
    Friend WithEvents Label3 As Label
    Friend WithEvents NameComboBox As OriginalControls.OriginalComboBox
    Friend WithEvents CopyButton As Button
    Friend WithEvents HelpLabel As Label
    Friend WithEvents HelpToolTip As ToolTip
End Class
