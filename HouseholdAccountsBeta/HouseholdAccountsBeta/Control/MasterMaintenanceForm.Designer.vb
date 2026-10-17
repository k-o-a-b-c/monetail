<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class MasterMaintenanceForm
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
        Me.ShopLabel = New System.Windows.Forms.Label()
        Me.ShopComboBox = New System.Windows.Forms.ComboBox()
        Me.TaxRoundingModeLabel = New System.Windows.Forms.Label()
        Me.TaxRoundingModeComboBox = New System.Windows.Forms.ComboBox()
        Me.DescriptionLabel = New System.Windows.Forms.Label()
        Me.SaveButton = New System.Windows.Forms.Button()
        Me.StatusLabel = New System.Windows.Forms.Label()
        Me.CloseButton = New System.Windows.Forms.Button()
        Me.SuspendLayout()
        '
        'ShopLabel
        '
        Me.ShopLabel.AutoSize = True
        Me.ShopLabel.Location = New System.Drawing.Point(20, 25)
        Me.ShopLabel.Text = "登録済み店舗"
        '
        'ShopComboBox
        '
        Me.ShopComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.ShopComboBox.Location = New System.Drawing.Point(120, 22)
        Me.ShopComboBox.Name = "ShopComboBox"
        Me.ShopComboBox.Size = New System.Drawing.Size(330, 20)
        Me.ShopComboBox.TabIndex = 0
        '
        'TaxRoundingModeLabel
        '
        Me.TaxRoundingModeLabel.AutoSize = True
        Me.TaxRoundingModeLabel.Location = New System.Drawing.Point(20, 65)
        Me.TaxRoundingModeLabel.Text = "税端数の初期値"
        '
        'TaxRoundingModeComboBox
        '
        Me.TaxRoundingModeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        Me.TaxRoundingModeComboBox.Items.AddRange(New Object() {"切り捨て", "四捨五入"})
        Me.TaxRoundingModeComboBox.Location = New System.Drawing.Point(120, 62)
        Me.TaxRoundingModeComboBox.Name = "TaxRoundingModeComboBox"
        Me.TaxRoundingModeComboBox.Size = New System.Drawing.Size(130, 20)
        Me.TaxRoundingModeComboBox.TabIndex = 1
        '
        'DescriptionLabel
        '
        Me.DescriptionLabel.Location = New System.Drawing.Point(20, 105)
        Me.DescriptionLabel.Size = New System.Drawing.Size(430, 45)
        Me.DescriptionLabel.Text = "店舗を明示選択した新規レシートの初期値です。" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & "各レシートでも変更できます。店舗の登録は必須ではありません。"
        '
        'SaveButton
        '
        Me.SaveButton.Enabled = False
        Me.SaveButton.Location = New System.Drawing.Point(290, 165)
        Me.SaveButton.Name = "SaveButton"
        Me.SaveButton.Size = New System.Drawing.Size(75, 28)
        Me.SaveButton.TabIndex = 2
        Me.SaveButton.Text = "保存"
        Me.SaveButton.UseVisualStyleBackColor = True
        '
        'StatusLabel
        '
        Me.StatusLabel.Location = New System.Drawing.Point(20, 210)
        Me.StatusLabel.Name = "StatusLabel"
        Me.StatusLabel.Size = New System.Drawing.Size(430, 30)
        '
        'CloseButton
        '
        Me.CloseButton.DialogResult = System.Windows.Forms.DialogResult.Cancel
        Me.CloseButton.Location = New System.Drawing.Point(375, 165)
        Me.CloseButton.Name = "CloseButton"
        Me.CloseButton.Size = New System.Drawing.Size(75, 28)
        Me.CloseButton.TabIndex = 3
        Me.CloseButton.Text = "閉じる"
        Me.CloseButton.UseVisualStyleBackColor = True
        '
        'MasterMaintenanceForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 12.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(475, 250)
        Me.CancelButton = Me.CloseButton
        Me.Controls.Add(Me.ShopLabel)
        Me.Controls.Add(Me.ShopComboBox)
        Me.Controls.Add(Me.TaxRoundingModeLabel)
        Me.Controls.Add(Me.TaxRoundingModeComboBox)
        Me.Controls.Add(Me.DescriptionLabel)
        Me.Controls.Add(Me.SaveButton)
        Me.Controls.Add(Me.StatusLabel)
        Me.Controls.Add(Me.CloseButton)
        Me.Name = "MasterMaintenanceForm"
        Me.Text = "設定 - 店舗の税端数初期値"
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub
    Friend WithEvents ShopLabel As System.Windows.Forms.Label
    Friend WithEvents ShopComboBox As System.Windows.Forms.ComboBox
    Friend WithEvents TaxRoundingModeLabel As System.Windows.Forms.Label
    Friend WithEvents TaxRoundingModeComboBox As System.Windows.Forms.ComboBox
    Friend WithEvents DescriptionLabel As System.Windows.Forms.Label
    Friend WithEvents SaveButton As System.Windows.Forms.Button
    Friend WithEvents StatusLabel As System.Windows.Forms.Label
    Friend WithEvents CloseButton As System.Windows.Forms.Button
End Class
