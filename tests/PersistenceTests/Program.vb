Imports System.Data.SQLite
Imports System.Diagnostics
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Windows.Forms

Module Program
    <STAThread>
    Function Main() As Integer
        Check("actual SQLite receipt insert/update and both read paths", AddressOf ReceiptPersistence)
        Check("latest receipt history overrides unused master setting", AddressOf HistoryDefaults)
        Check("history uses own payment account and deterministic newest row", AddressOf HistoryOrderingAndOwnership)
        Check("failed receipt update rolls mode and total back together", AddressOf TransactionRollback)
        Check("new receipt defaults and manual override UI", AddressOf NewReceiptUi)
        Check("edit cancel restores mode and successful edit keeps mode", AddressOf EditUi)
        Check("canceling copy confirmation keeps form open without inserting", AddressOf CancelCopyUi)
        Check("copy confirmation immediately saves visible mode and closes", AddressOf ImmediateCopyUi)
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

    Private Sub HistoryDefaults()
        Using fixture As New DatabaseFixture
            Dim data = fixture.Data
            Dim saved = data.GetReceipts(2026, 10).Single()
            fixture.Execute("UPDATE m_shop SET tax_rounding_mode='half_up' WHERE code=1")
            Equal("floor", data.GetTaxRoundingMode("registered shop"))
            Equal("floor", data.GetTaxRoundingMode("unknown shop"))
            Equal("floor", data.GetTaxRoundingMode("history shop"))
            fixture.AddReceipt(2, "registered shop", "half_up", New Date(2026, 10, 14))
            Equal("half_up", data.GetTaxRoundingMode("registered shop"))
            fixture.AddReceipt(3, "registered shop", "floor", New Date(2026, 10, 15))
            Equal("floor", data.GetTaxRoundingMode("registered shop"))
            fixture.AddReceipt(4, "history shop", "half_up", New Date(2026, 10, 17))
            Equal("half_up", data.GetTaxRoundingMode("history shop"))
            Dim typedShop = "typed shop's name'; --"
            fixture.AddReceipt(5, typedShop, "half_up", New Date(2026, 10, 17))
            Equal("half_up", data.GetTaxRoundingMode(typedShop))
            Equal("floor", data.GetTaxRoundingMode("' OR 1=1 --"))
            Equal("floor", saved.TaxRoundingMode)
            Equal(41, saved.Total)
            Equal(41L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
            Equal("floor", data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1).TaxRoundingMode)
            Equal(1L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM m_shop")))
        End Using
    End Sub

    Private Sub HistoryOrderingAndOwnership()
        Using fixture As New DatabaseFixture
            fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'other','other-test-account',2);" &
                "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'other cash',2,2)")
            Dim shop = "same date shop"
            fixture.AddReceipt(2, shop, "half_up", New Date(2026, 10, 15, 12, 0, 0))
            fixture.AddReceipt(3, shop, "floor", New Date(2026, 10, 15, 12, 0, 0))
            Equal("floor", fixture.Data.GetTaxRoundingMode(shop))
            fixture.AddReceipt(4, shop, "half_up", New Date(2026, 10, 15, 12, 0, 0))
            Equal("half_up", fixture.Data.GetTaxRoundingMode(shop))
            ' A higher sequence on an older date must not beat the latest date.
            fixture.AddReceipt(5, shop, "floor", New Date(2026, 10, 14))
            Equal("half_up", fixture.Data.GetTaxRoundingMode(shop))
            ' Receipt ownership follows payment -> user, rather than update_user.
            fixture.AddReceipt(6, shop, "floor", New Date(2026, 10, 17), paymentCode:=2)
            fixture.Execute("UPDATE t_receipts SET update_user='irrelevant' WHERE seq=4")
            Equal("half_up", fixture.Data.GetTaxRoundingMode(shop))
            fixture.AddReceipt(7, "only other account", "half_up", New Date(2026, 10, 17), paymentCode:=2)
            Equal("floor", fixture.Data.GetTaxRoundingMode("only other account"))
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

    Private Sub CommitShop(form As RegisterForm, shopName As String)
        Dim shops = DirectCast(form.ShopNameComboBox.DataSource, System.Collections.Generic.List(Of Shop))
        Dim index = shops.FindIndex(Function(s) s.Name = shopName)
        If index < 0 Then Throw New Exception("Missing shop candidate: " & shopName)
        form.ShopNameComboBox.SelectedIndex = index
        InvokeEvent(form, "ShopNameComboBox_SelectionChangeCommitted", form.ShopNameComboBox, EventArgs.Empty)
    End Sub

    Private Sub ValidateTypedShop(form As RegisterForm, shopName As String)
        form.ShopNameComboBox.Text = shopName
        InvokeEvent(form, "ShopNameComboBox_TextUpdate", form.ShopNameComboBox, EventArgs.Empty)
        InvokeEvent(form, "ShopNameComboBox_Validated", form.ShopNameComboBox, EventArgs.Empty)
    End Sub

    Private Sub NewReceiptUi()
        Using fixture As New DatabaseFixture
            fixture.AddReceipt(2, "registered shop", "half_up", New Date(2026, 10, 17))
            fixture.AddReceipt(3, "free text store", "half_up", New Date(2026, 10, 17))
            fixture.AddReceipt(4, "history shop", "half_up", New Date(2026, 10, 17))
            Using form As New RegisterForm With {.DataAccess = fixture.Data}
                OpenForm(form)
                Equal("floor", form.Receipt.TaxRoundingMode)
                CommitShop(form, "registered shop")
                Equal("half_up", form.Receipt.TaxRoundingMode)
                CommitShop(form, "history shop")
                Equal(0, DirectCast(form.ShopNameComboBox.SelectedItem, Shop).Code)
                Equal("half_up", form.Receipt.TaxRoundingMode)
                ValidateTypedShop(form, "free text store")
                Equal("half_up", form.Receipt.TaxRoundingMode)
                ValidateTypedShop(form, "never used store")
                Equal("floor", form.Receipt.TaxRoundingMode)
                form.TaxRoundingModeComboBox.SelectedIndex = 1
                CommitShop(form, "history shop")
                ValidateTypedShop(form, "never used store")
                Equal("half_up", form.Receipt.TaxRoundingMode)
                form.TaxRoundingModeComboBox.SelectedIndex = 0
                CommitShop(form, "registered shop")
                Equal("floor", form.Receipt.TaxRoundingMode)
                form.Close()
            End Using
        End Using
    End Sub

    Private Sub EditUi()
        Using fixture As New DatabaseFixture
            fixture.AddReceipt(2, "registered shop", "half_up", New Date(2026, 10, 17))
            Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                OpenForm(form)
                CommitShop(form, "registered shop")
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
        End Using
    End Sub

    Private Sub CancelCopyUi()
        Using fixture As New DatabaseFixture
            Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                OpenForm(form)
                form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                form.TaxRoundingModeComboBox.SelectedIndex = 1
                ClickCopyAndAnswer(form, DialogResult.No)
                Equal(1L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipts")))
                Equal(1, receipt.Seq)
                Equal(True, form.Visible)
                Equal("half_up", receipt.TaxRoundingMode)
                Equal("floor", Convert.ToString(fixture.Scalar("SELECT tax_rounding_mode FROM t_receipts WHERE seq=1")))
                Equal(41L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                form.Close()
                Equal("floor", receipt.TaxRoundingMode)
            End Using
        End Using
    End Sub

    Private Sub ImmediateCopyUi()
        For Each visibleMode In New String() {"half_up", "floor"}
            Using fixture As New DatabaseFixture
                Dim historyMode = If(visibleMode = "half_up", "floor", "half_up")
                fixture.AddReceipt(2, "registered shop", historyMode, New Date(2026, 10, 17))
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                    Dim closed As Boolean
                    AddHandler form.FormClosed, Sub(sender, args) closed = True
                    OpenForm(form)
                    CommitShop(form, "registered shop")
                    form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                    form.TaxRoundingModeComboBox.SelectedIndex = If(visibleMode = "half_up", 1, 0)
                    Equal(visibleMode, receipt.TaxRoundingMode)
                    Equal(historyMode, fixture.Data.GetTaxRoundingMode("registered shop"))
                    ClickCopyAndAnswer(form, DialogResult.Yes)
                    ' No subsequent Register click: the actual Copy handler must save and close.
                    Equal(True, closed)
                    Equal(DialogResult.OK, form.DialogResult)
                    Equal(3L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipts")))
                    Equal(3, receipt.Seq)
                    Dim persisted = fixture.Data.GetReceipts(2026, 10)
                    Dim source = persisted.Single(Function(r) r.Seq = 1)
                    Equal("floor", source.TaxRoundingMode)
                    Equal(41, source.Total)
                    Equal("history shop", source.ShopName)
                    Dim copy = persisted.Single(Function(r) r.Seq = 3)
                    Equal(visibleMode, copy.TaxRoundingMode)
                    Equal("registered shop", copy.ShopName)
                    Equal(If(visibleMode = "half_up", 42, 41), copy.Total)
                    Equal(copy.Total, Convert.ToInt32(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=3")))
                    Equal(2, copy.Groups.Count)
                    Equal(1, copy.Groups(0).Details(0).Users.Count)
                End Using
            End Using
        Next
    End Sub

    ' Native MessageBox buttons have stable IDs, independent of Windows display language.
    ' Only a confirmation dialog owned by this test process is eligible for interaction.
    Private Delegate Function EnumWindowCallback(window As IntPtr, parameter As IntPtr) As Boolean

    <DllImport("user32.dll")>
    Private Function EnumWindows(callback As EnumWindowCallback, parameter As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Function GetWindowText(window As IntPtr, text As StringBuilder, capacity As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Function GetClassName(window As IntPtr, className As StringBuilder, capacity As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowThreadProcessId(window As IntPtr, ByRef processId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Function GetDlgItem(dialog As IntPtr, controlId As Integer) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Function SendMessage(window As IntPtr, message As UInteger, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private Function FindOwnConfirmation() As IntPtr
        Dim result As IntPtr
        Dim ownProcessId = CUInt(Process.GetCurrentProcess().Id)
        EnumWindows(
            Function(window, parameter)
                Dim processId As UInteger
                GetWindowThreadProcessId(window, processId)
                If processId <> ownProcessId Then Return True
                Dim title As New StringBuilder(128)
                Dim className As New StringBuilder(128)
                GetWindowText(window, title, title.Capacity)
                GetClassName(window, className, className.Capacity)
                If title.ToString() <> "確認" OrElse className.ToString() <> "#32770" Then Return True
                result = window
                Return False
            End Function, IntPtr.Zero)
        Return result
    End Function

    Private Sub ClickCopyAndAnswer(form As RegisterForm, answer As DialogResult)
        Dim answered As Integer
        Dim watch = Stopwatch.StartNew()
        Using responder As New System.Windows.Forms.Timer With {.Interval = 50}
            AddHandler responder.Tick,
                Sub(sender, args)
                    If watch.Elapsed > TimeSpan.FromSeconds(15) Then
                        Environment.FailFast("Copy confirmation did not complete within 15 seconds.")
                    End If
                    Dim dialog = FindOwnConfirmation()
                    If dialog = IntPtr.Zero Then Return
                    ' An unexpected extra confirmation is declined, then reported after the handler returns.
                    Dim button = GetDlgItem(dialog, If(answered = 0 AndAlso answer = DialogResult.Yes, 6, 7))
                    If button = IntPtr.Zero Then Return
                    answered += 1
                    SendMessage(button, &HF5UI, IntPtr.Zero, IntPtr.Zero) ' BM_CLICK
                End Sub
            responder.Start()
            Try
                form.CopyButton.PerformClick()
            Finally
                responder.Stop()
            End Try
        End Using
        Equal(1, answered, "Copy must show exactly one confirmation")
    End Sub
End Module
