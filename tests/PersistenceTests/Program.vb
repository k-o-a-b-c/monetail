Imports System.Data.SQLite
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms

Module Program
    <STAThread>
    Function Main() As Integer
        Check("actual SQLite receipt insert/update and both read paths", AddressOf ReceiptPersistence)
        Check("master defaults and history stay independent of saved receipt", AddressOf MasterDefaults)
        Check("failed receipt update rolls mode and total back together", AddressOf TransactionRollback)
        Check("new receipt defaults and manual override UI", AddressOf NewReceiptUi)
        Check("edit cancel, successful edit, and copy preserve source UI", AddressOf EditAndCopyUi)
        Check("master maintenance edits existing stores only", AddressOf MasterUi)
        Return Finish()
    End Function

    Private Sub ReceiptPersistence()
        Using fixture As New DatabaseFixture
            Dim data = fixture.Data
            Dim oldReceipt = data.GetReceipts(2026, 10).Single()
            Equal("floor", oldReceipt.TaxRoundingMode)
            Equal(41, oldReceipt.Total)
            Dim receipt = MakeReceipt("half_up", Group(10, Detail(19)), Group(10, Detail(19)))
            data.RegisterReceipt(receipt)
            Equal(42L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=2")))
            Equal("half_up", Convert.ToString(fixture.Scalar("SELECT tax_rounding_mode FROM t_receipts WHERE seq=2")))
            Dim fromList = data.GetReceipts(2026, 10).Single(Function(r) r.Seq = receipt.Seq)
            Dim fromBalance = data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1), receipt.PaymentClassification).Single(Function(r) r.Seq = receipt.Seq)
            Equal("half_up", fromList.TaxRoundingMode)
            Equal("half_up", fromBalance.TaxRoundingMode)
            Equal(42, fromList.Total)
            Equal(fromList.Total, fromBalance.Total)
            fromList.TaxRoundingMode = "floor"
            data.UpdateReceipt(fromList)
            Equal("floor", Convert.ToString(fixture.Scalar("SELECT tax_rounding_mode FROM t_receipts WHERE seq=2")))
            Equal(41L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=2")))
            Equal(41, data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1), receipt.PaymentClassification).Single(Function(r) r.Seq = receipt.Seq).Total)
        End Using
    End Sub

    Private Sub MasterDefaults()
        Using fixture As New DatabaseFixture
            Dim data = fixture.Data
            data.UpdateShopTaxRoundingMode(1, "half_up")
            Equal("half_up", data.ShopList.Single(Function(s) s.Code = 1).TaxRoundingMode)
            Equal("floor", data.ShopList.Single(Function(s) s.Code = 0).TaxRoundingMode)
            Dim saved = data.GetReceipts(2026, 10).Single()
            Equal("floor", saved.TaxRoundingMode)
            Equal(41, saved.Total)
            Equal(41L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
            Throws(Of ArgumentOutOfRangeException)(Sub() data.UpdateShopTaxRoundingMode(0, "half_up"))
            Throws(Of ArgumentException)(Sub() data.UpdateShopTaxRoundingMode(1, "invalid"))
            Throws(Of InvalidOperationException)(Sub() data.UpdateShopTaxRoundingMode(999, "floor"))
            Equal(1L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM m_shop")))
        End Using
    End Sub

    Private Sub TransactionRollback()
        Using fixture As New DatabaseFixture
            Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
            receipt.TaxRoundingMode = "half_up"
            ' Duplicate assignment fails after the receipt row is updated, inside its transaction.
            receipt.Groups(0).Details(0).Users.Add(receipt.Groups(0).Details(0).Users.Single())
            Throws(Of SQLiteException)(Sub() fixture.Data.UpdateReceipt(receipt))
            Equal("floor", Convert.ToString(fixture.Scalar("SELECT tax_rounding_mode FROM t_receipts WHERE seq=1")))
            Equal(41L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
            Equal(1L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipt_detail_users")))
        End Using
    End Sub

    Private Sub InvokeEvent(target As Object, name As String, ParamArray args As Object())
        target.GetType().GetMethod(name, BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(target, args)
    End Sub

    Private Sub OpenForm(form As Form)
        form.Show()
        Application.DoEvents()
    End Sub

    Private Sub CommitShop(form As RegisterForm, code As Integer)
        Dim shops = DirectCast(form.ShopNameComboBox.DataSource, System.Collections.Generic.List(Of Shop))
        form.ShopNameComboBox.SelectedIndex = shops.FindIndex(Function(s) s.Code = code)
        InvokeEvent(form, "ShopNameComboBox_SelectionChangeCommitted", form.ShopNameComboBox, EventArgs.Empty)
    End Sub

    Private Sub NewReceiptUi()
        Using fixture As New DatabaseFixture
            fixture.Data.UpdateShopTaxRoundingMode(1, "half_up")
            Using form As New RegisterForm With {.DataAccess = fixture.Data}
                OpenForm(form)
                Equal("floor", form.Receipt.TaxRoundingMode)
                CommitShop(form, 1)
                Equal("half_up", form.Receipt.TaxRoundingMode)
                CommitShop(form, 0)
                Equal("floor", form.Receipt.TaxRoundingMode)
                CommitShop(form, 1)
                form.ShopNameComboBox.Text = "free text store"
                InvokeEvent(form, "ShopNameComboBox_TextUpdate", form.ShopNameComboBox, EventArgs.Empty)
                Equal("floor", form.Receipt.TaxRoundingMode)
                form.TaxRoundingModeComboBox.SelectedIndex = 1
                CommitShop(form, 0)
                Equal("half_up", form.Receipt.TaxRoundingMode)
                form.Close()
            End Using
        End Using
    End Sub

    Private Sub EditAndCopyUi()
        Using fixture As New DatabaseFixture
            fixture.Data.UpdateShopTaxRoundingMode(1, "half_up")
            Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                OpenForm(form)
                CommitShop(form, 1)
                Equal("floor", receipt.TaxRoundingMode)
                form.TaxRoundingModeComboBox.SelectedIndex = 1
                Equal("half_up", receipt.TaxRoundingMode)
                form.Close()
                Equal("floor", receipt.TaxRoundingMode)
            End Using
            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                OpenForm(form)
                form.TaxRoundingModeComboBox.SelectedIndex = 1
                form.DialogResult = DialogResult.OK
                form.Close()
                Equal("half_up", receipt.TaxRoundingMode)
            End Using
            For Each result In New DialogResult() {DialogResult.Cancel, DialogResult.OK}
                receipt.TaxRoundingMode = "floor"
                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                    OpenForm(form)
                    form.TaxRoundingModeComboBox.SelectedIndex = 1
                    InvokeEvent(form, "BeginCopyRegistration")
                    Equal(False, Object.ReferenceEquals(receipt, form.Receipt))
                    Equal(0, form.Receipt.Seq)
                    Equal("floor", form.Receipt.TaxRoundingMode)
                    Equal(False, Object.ReferenceEquals(receipt.Groups(0).Details(0), form.Receipt.Groups(0).Details(0)))
                    form.Receipt.Groups(0).Details(0).Fee = 999
                    Equal(19, receipt.Groups(0).Details(0).Fee)
                    form.DialogResult = result
                    form.Close()
                    Equal("floor", receipt.TaxRoundingMode)
                    Equal(1, receipt.Seq)
                End Using
            Next
        End Using
    End Sub

    Private Sub MasterUi()
        Using fixture As New DatabaseFixture
            Using form As New MasterMaintenanceForm With {.DataAccess = fixture.Data}
                OpenForm(form)
                Equal(1, form.ShopComboBox.Items.Count)
                form.ShopComboBox.SelectedIndex = 0
                form.TaxRoundingModeComboBox.SelectedIndex = 1
                InvokeEvent(form, "SaveButton_Click", form.SaveButton, EventArgs.Empty)
                Equal("half_up", fixture.Data.ShopList.Single(Function(s) s.Code = 1).TaxRoundingMode)
                Equal("floor", fixture.Data.GetReceipts(2026, 10).Single().TaxRoundingMode)
                form.Close()
            End Using
        End Using
    End Sub
End Module
