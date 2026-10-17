Public Class MasterMaintenanceForm

    Public Property DataAccess As DataAccess

    Private Sub MasterMaintenanceForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ShopComboBox.DisplayMember = "Name"
        ShopComboBox.DataSource = DataAccess.ShopList.Where(Function(shop) shop.Code > 0).ToList()
        ShopComboBox.SelectedIndex = -1
        TaxRoundingModeComboBox.SelectedIndex = -1
        SaveButton.Enabled = False
    End Sub

    Private Sub ShopComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ShopComboBox.SelectedIndexChanged
        Dim shop As Shop = TryCast(ShopComboBox.SelectedItem, Shop)
        TaxRoundingModeComboBox.SelectedIndex = If(shop Is Nothing, -1, If(shop.TaxRoundingMode = "half_up", 1, 0))
        SaveButton.Enabled = shop IsNot Nothing
        StatusLabel.Text = String.Empty
    End Sub

    Private Sub SaveButton_Click(sender As Object, e As EventArgs) Handles SaveButton.Click
        Dim shop As Shop = TryCast(ShopComboBox.SelectedItem, Shop)
        If shop Is Nothing OrElse TaxRoundingModeComboBox.SelectedIndex < 0 Then
            Return
        End If
        Dim mode As String = If(TaxRoundingModeComboBox.SelectedIndex = 1, "half_up", "floor")
        DataAccess.UpdateShopTaxRoundingMode(shop.Code, mode)
        shop.TaxRoundingMode = mode
        StatusLabel.Text = "保存しました。次の新規入力から初期値として使われます。"
    End Sub

End Class
