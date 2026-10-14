<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class DetailViewForm
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
        Dim DataGridViewCellStyle1 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Dim DataGridViewCellStyle2 As System.Windows.Forms.DataGridViewCellStyle = New System.Windows.Forms.DataGridViewCellStyle()
        Me.GroupDetailGridView = New System.Windows.Forms.DataGridView()
        Me.DetailCheckBox = New OriginalControls.OriginalCheckBox()
        Me.SeqColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.DateTimeColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.ShopColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.LocationColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.PaymentColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.GroupColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.TagColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.ItemColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.QuantityColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        Me.FeeColumn = New System.Windows.Forms.DataGridViewTextBoxColumn()
        CType(Me.GroupDetailGridView, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'GroupDetailGridView
        '
        Me.GroupDetailGridView.AllowUserToAddRows = False
        Me.GroupDetailGridView.AllowUserToDeleteRows = False
        Me.GroupDetailGridView.AllowUserToResizeRows = False
        Me.GroupDetailGridView.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.GroupDetailGridView.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells
        Me.GroupDetailGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize
        Me.GroupDetailGridView.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.SeqColumn, Me.DateTimeColumn, Me.ShopColumn, Me.LocationColumn, Me.PaymentColumn, Me.GroupColumn, Me.TagColumn, Me.ItemColumn, Me.QuantityColumn, Me.FeeColumn})
        Me.GroupDetailGridView.Location = New System.Drawing.Point(13, 38)
        Me.GroupDetailGridView.Margin = New System.Windows.Forms.Padding(4)
        Me.GroupDetailGridView.Name = "GroupDetailGridView"
        Me.GroupDetailGridView.ReadOnly = True
        Me.GroupDetailGridView.RowHeadersVisible = False
        Me.GroupDetailGridView.RowTemplate.Height = 21
        Me.GroupDetailGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect
        Me.GroupDetailGridView.Size = New System.Drawing.Size(798, 550)
        Me.GroupDetailGridView.TabIndex = 0
        '
        'DetailCheckBox
        '
        Me.DetailCheckBox.AutoSize = True
        Me.DetailCheckBox.Location = New System.Drawing.Point(12, 12)
        Me.DetailCheckBox.Name = "DetailCheckBox"
        Me.DetailCheckBox.Size = New System.Drawing.Size(83, 19)
        Me.DetailCheckBox.TabIndex = 1
        Me.DetailCheckBox.Text = "詳細を表示"
        Me.DetailCheckBox.UseVisualStyleBackColor = True
        '
        'SeqColumn
        '
        Me.SeqColumn.HeaderText = "Seq"
        Me.SeqColumn.Name = "SeqColumn"
        Me.SeqColumn.ReadOnly = True
        Me.SeqColumn.Width = 51
        '
        'DateTimeColumn
        '
        Me.DateTimeColumn.HeaderText = "日時"
        Me.DateTimeColumn.Name = "DateTimeColumn"
        Me.DateTimeColumn.ReadOnly = True
        Me.DateTimeColumn.Width = 56
        '
        'ShopColumn
        '
        Me.ShopColumn.HeaderText = "店名"
        Me.ShopColumn.Name = "ShopColumn"
        Me.ShopColumn.ReadOnly = True
        Me.ShopColumn.Width = 56
        '
        'LocationColumn
        '
        Me.LocationColumn.HeaderText = "場所"
        Me.LocationColumn.Name = "LocationColumn"
        Me.LocationColumn.ReadOnly = True
        Me.LocationColumn.Width = 56
        '
        'PaymentColumn
        '
        Me.PaymentColumn.HeaderText = "支払い方法"
        Me.PaymentColumn.Name = "PaymentColumn"
        Me.PaymentColumn.ReadOnly = True
        Me.PaymentColumn.Width = 90
        '
        'GroupColumn
        '
        Me.GroupColumn.HeaderText = "分類"
        Me.GroupColumn.Name = "GroupColumn"
        Me.GroupColumn.ReadOnly = True
        Me.GroupColumn.Width = 56
        '
        'TagColumn
        '
        Me.TagColumn.HeaderText = "タグ"
        Me.TagColumn.Name = "TagColumn"
        Me.TagColumn.ReadOnly = True
        Me.TagColumn.Width = 50
        '
        'ItemColumn
        '
        Me.ItemColumn.HeaderText = "品目"
        Me.ItemColumn.Name = "ItemColumn"
        Me.ItemColumn.ReadOnly = True
        Me.ItemColumn.Width = 56
        '
        'QuantityColumn
        '
        DataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight
        DataGridViewCellStyle1.Format = "N0"
        Me.QuantityColumn.DefaultCellStyle = DataGridViewCellStyle1
        Me.QuantityColumn.HeaderText = "数量"
        Me.QuantityColumn.Name = "QuantityColumn"
        Me.QuantityColumn.ReadOnly = True
        Me.QuantityColumn.Width = 56
        '
        'FeeColumn
        '
        DataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleRight
        DataGridViewCellStyle2.Format = "N0"
        DataGridViewCellStyle2.NullValue = Nothing
        Me.FeeColumn.DefaultCellStyle = DataGridViewCellStyle2
        Me.FeeColumn.HeaderText = "価格"
        Me.FeeColumn.Name = "FeeColumn"
        Me.FeeColumn.ReadOnly = True
        Me.FeeColumn.Width = 56
        '
        'DetailViewForm
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(96.0!, 96.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi
        Me.ClientSize = New System.Drawing.Size(824, 601)
        Me.Controls.Add(Me.DetailCheckBox)
        Me.Controls.Add(Me.GroupDetailGridView)
        Me.Font = New System.Drawing.Font("Yu Gothic UI", 9.0!, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, CType(128, Byte))
        Me.Margin = New System.Windows.Forms.Padding(4)
        Me.Name = "DetailViewForm"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Me.Text = "分類毎明細"
        CType(Me.GroupDetailGridView, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)
        Me.PerformLayout()

    End Sub

    Friend WithEvents GroupDetailGridView As DataGridView
    Friend WithEvents DetailCheckBox As OriginalControls.OriginalCheckBox
    Friend WithEvents SeqColumn As DataGridViewTextBoxColumn
    Friend WithEvents DateTimeColumn As DataGridViewTextBoxColumn
    Friend WithEvents ShopColumn As DataGridViewTextBoxColumn
    Friend WithEvents LocationColumn As DataGridViewTextBoxColumn
    Friend WithEvents PaymentColumn As DataGridViewTextBoxColumn
    Friend WithEvents GroupColumn As DataGridViewTextBoxColumn
    Friend WithEvents TagColumn As DataGridViewTextBoxColumn
    Friend WithEvents ItemColumn As DataGridViewTextBoxColumn
    Friend WithEvents QuantityColumn As DataGridViewTextBoxColumn
    Friend WithEvents FeeColumn As DataGridViewTextBoxColumn
End Class
