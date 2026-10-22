Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports System.Text

Friend Module ReceiptUiAcceptance
    ' 税設定のキーボード操作と、拡大したフォームの描画結果を検証する。
    Friend Function Run(outputFolder As String) As Integer
        Directory.CreateDirectory(outputFolder)
        Using deadline As New System.Threading.Timer(
            Sub(state) Environment.FailFast("Receipt UI acceptance exceeded three minutes."), Nothing,
            TimeSpan.FromMinutes(3), System.Threading.Timeout.InfiniteTimeSpan)
            Check("receipt tax fields support forward reverse Tab and independent radio keyboard selection", AddressOf KeyboardNavigation)
            Check("receipt tax controls fit at four relative scales and produce reviewable Windows images", Sub() CaptureLayouts(outputFolder))
            Return Finish()
        End Using
    End Function

    ' Tab・Shift+Tabと矢印キーで税設定へ移動し、入力と元データの保持を確認する。
    Private Sub KeyboardNavigation()
        Using fixture As New DatabaseFixture
            BalanceBoundaryTests.ReadOnlyCase(fixture,
                Sub()
                    Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                    Dim before = DetailInputTests.ReceiptState(receipt)
                    Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                        Program.OpenForm(form)
                        TaxDefaultTests.SelectGroup(form, 0)
                        form.ItemNameComboBox.Text = "Tabで追加する商品"
                        form.FeeTextBox.Text = "19"
                        form.ItemNameComboBox.Select()
                        For Each expected As Control In New Control() {
                            form.FeeTextBox, form.QuantityTextBox, form.DiscountFeeTextBox,
                            form.DiscountRateTextBox, form.DetailTagTextBox, form.DetailTaxRateComboBox,
                            form.UserListBox, form.AddDetailButton}
                            DialogKey(form, Keys.Tab)
                            Equal(True, expected.ContainsFocus, "Forward Tab must reach " & expected.Name)
                        Next
                        form.DetailTaxRateComboBox.Select()
                        DialogKey(form, Keys.Tab Or Keys.Shift)
                        Equal(True, form.DetailTagTextBox.ContainsFocus, "Reverse Tab must return to detail memo")
                        DialogKey(form, Keys.Tab)
                        Equal(True, form.DetailTaxRateComboBox.ContainsFocus)
                        form.TaxExclusiveRadioButton.Select()
                        DialogKey(form, Keys.Tab)
                        Equal(True, form.TaxRoundingFloorRadioButton.ContainsFocus)
                        DialogKey(form.TaxRoundingFloorRadioButton, Keys.Right)
                        Equal(True, form.TaxRoundingHalfUpRadioButton.Checked)
                        Equal(True, form.TaxExclusiveRadioButton.Checked)
                        form.TaxExclusiveRadioButton.Select()
                        DialogKey(form.TaxExclusiveRadioButton, Keys.Left)
                        Equal(True, form.TaxInclusiveRadioButton.Checked)
                        Equal(True, form.TaxRoundingHalfUpRadioButton.Checked)
                        Equal("Tabで追加する商品", form.ItemNameComboBox.Text)
                        Equal("19", form.FeeTextBox.Text)
                        Equal(before, DetailInputTests.ReceiptState(receipt))
                        form.Close()
                    End Using
                End Sub)
        End Using
    End Sub

    ' WinFormsのキー処理へ入力を渡し、フォーカス移動と検証イベントを実行する。
    Private Sub DialogKey(target As Control, key As Keys)
        Dim method = target.GetType().GetMethod("ProcessDialogKey", BindingFlags.Instance Or BindingFlags.NonPublic)
        Equal(True, CBool(method.Invoke(target, New Object() {key})), "Key must be handled: " & key.ToString())
        Application.DoEvents()
    End Sub

    ' 未確定入力と混合税率を表示し、標準描画と相対125・150・200%の画像を保存する。
    Private Sub CaptureLayouts(outputFolder As String)
        Dim evidence As New StringBuilder("relative_scale,actual_dpi_x,actual_dpi_y,client_width,client_height,tax_width,tax_height" & vbCrLf)
        For Each factor As Single In New Single() {1.0F, 1.25F, 1.5F, 2.0F}
            Dim scale = factor
            Using fixture As New DatabaseFixture
                fixture.Execute("UPDATE m_group_classification SET name='食費' WHERE code=1;" &
                    "UPDATE t_receipt_details SET name='8%の商品',tax_code=4 WHERE group_seq=1;" &
                    "INSERT INTO t_receipt_details(receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_code,tag) " &
                    "VALUES(1,1,2,'標準税率の商品',19,1,0,'0',5,''),(1,1,3,'対象外の商品',100,1,0,'0',0,'');")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = fixture.Data.GetReceipts(2026, 10).Single()}
                            Program.OpenForm(form)
                            TaxDefaultTests.SelectGroup(form, 0)
                            form.ItemNameComboBox.Text = "入力保持の確認"
                            form.FeeTextBox.Text = "23*2"
                            form.DetailTagTextBox.Text = "入力中のメモ"
                            form.TaxRoundingHalfUpRadioButton.Checked = True
                            Equal("8%の商品（8%）", form.DetailListView.Items(0).Text)
                            Equal("標準税率の商品", form.DetailListView.Items(1).Text)
                            Equal("対象外の商品（対象外）", form.DetailListView.Items(2).Text)
                            If scale <> 1.0F Then
                                form.MaximumSize = New Size(4096, 4096)
                                form.AutoScaleMode = AutoScaleMode.None
                                form.Font = New Font(form.Font.FontFamily, form.Font.Size * scale, form.Font.Style, form.Font.Unit)
                                form.Scale(New SizeF(scale, scale))
                                form.PerformLayout()
                                Application.DoEvents()
                            End If
                            Dim suffix = CInt(scale * 100).ToString(CultureInfo.InvariantCulture)
                            Using bitmap As New Bitmap(form.Width, form.Height)
                                form.DrawToBitmap(bitmap, New Rectangle(Point.Empty, bitmap.Size))
                                bitmap.Save(Path.Combine(outputFolder, "receipt-ui-" & suffix & ".png"), ImageFormat.Png)
                            End Using
                            Using graphics = form.CreateGraphics()
                                evidence.AppendLine(String.Join(",", New String() {
                                    scale.ToString(CultureInfo.InvariantCulture), graphics.DpiX.ToString(CultureInfo.InvariantCulture),
                                    graphics.DpiY.ToString(CultureInfo.InvariantCulture), form.ClientSize.Width.ToString(CultureInfo.InvariantCulture),
                                    form.ClientSize.Height.ToString(CultureInfo.InvariantCulture), form.DetailTaxRateComboBox.Width.ToString(CultureInfo.InvariantCulture),
                                    form.DetailTaxRateComboBox.Height.ToString(CultureInfo.InvariantCulture)}))
                            End Using
                            File.WriteAllText(Path.Combine(outputFolder, "layout.csv"), evidence.ToString(), New UTF8Encoding(True))
                            Console.WriteLine("LAYOUT scale=" & scale.ToString(CultureInfo.InvariantCulture) &
                                              " client=" & form.ClientSize.ToString() & " tax=" & form.DetailTaxRateComboBox.Bounds.ToString())
                            Equal(True, If(scale = 1.0F, form.DetailTaxRateComboBox.Width = 156, form.DetailTaxRateComboBox.Width > 156),
                                  "Relative scaling must enlarge the tax field; anchored margins may alter its exact width")
                            For Each control As Control In New Control() {
                                form.FeeTextBox, form.QuantityTextBox, form.DiscountFeeTextBox, form.DiscountRateTextBox,
                                form.DetailTaxRateComboBox, form.Label3, form.DetailTagTextBox,
                                form.TaxRoundingModePanel, form.TaxRoundingModeLabel,
                                form.TaxRoundingFloorRadioButton, form.TaxRoundingHalfUpRadioButton}
                                Equal(True, control.Visible, control.Name & " must be visible")
                                Equal(True, control.Parent.ClientRectangle.Contains(control.Bounds), control.Name & " must fit inside its parent")
                            Next
                            Separate(form.ItemNameComboBox, form.FeeTextBox)
                            Separate(form.FeeTextBox, form.QuantityTextBox)
                            Separate(form.FeeTextBox, form.DiscountFeeTextBox)
                            Separate(form.DiscountRateTextBox, form.DetailTagTextBox)
                            Separate(form.DetailTaxRateComboBox, form.DiscountRateTextBox)
                            Separate(form.DetailTaxRateComboBox, form.DetailTagTextBox)
                            Separate(form.DetailTaxRateComboBox, form.Label3)
                            Separate(form.TaxRoundingFloorRadioButton, form.TaxRoundingHalfUpRadioButton)
                            Separate(form.TaxRoundingModePanel, form.TaxExclusiveRadioButton)
                            Equal("入力保持の確認", form.ItemNameComboBox.Text)
                            Equal("23*2", form.FeeTextBox.Text)
                            Equal("入力中のメモ", form.DetailTagTextBox.Text)
                            form.Close()
                        End Using
                    End Sub)
            End Using
        Next
        File.WriteAllText(Path.Combine(outputFolder, "layout.csv"), evidence.ToString(), New UTF8Encoding(True))
        Console.WriteLine("Relative font and geometry scaling exercises layout; actual Windows DPI is recorded separately in layout.csv.")
    End Sub

    ' 異なる親に属する欄も画面座標へ揃え、重なっていないことを確認する。
    Private Sub Separate(first As Control, second As Control)
        Equal(False, first.RectangleToScreen(first.ClientRectangle).IntersectsWith(second.RectangleToScreen(second.ClientRectangle)),
              first.Name & " must not overlap " & second.Name)
    End Sub
End Module
