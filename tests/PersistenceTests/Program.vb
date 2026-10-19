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
        Check("XML deployment settings and generated defaults agree", AddressOf StartupTests.ConfigurationDefaults)
        Check("relative database paths use the executable directory and preserve connection options", AddressOf StartupTests.RelativePathsAndOptions)
        Check("missing or invalid database settings never create a database", AddressOf StartupTests.MissingOrInvalidSettings)
        Check("empty corrupt and unrelated databases are rejected without changes or retained handles", AddressOf StartupTests.InvalidDatabases)
        Check("required tables columns and both migrations are checked without schema changes", AddressOf StartupTests.RequiredSchema)
        Check("startup reports setup and XML errors once and allows corrected settings", AddressOf StartupTests.ErrorReportingAndRecovery)
        Check("actual SQLite receipt insert/update and both read paths", AddressOf ReceiptPersistence)
        Check("discount rates survive receipt insert and all three read paths", AddressOf FractionalDiscountTests.InsertRoundTrips)
        Check("discount rates survive receipt update and all three read paths", AddressOf FractionalDiscountTests.UpdateRoundTrips)
        Check("payment memo batches preserve dates and nullable tags with foreign keys on and off", AddressOf PaymentMemoTests.BatchRoundTrips)
        Check("late payment memo insert/update failure rolls the whole batch back and allows retry", AddressOf PaymentMemoTests.BatchRollback)
        Check("empty payment memo batch preserves rows and allows later save", AddressOf PaymentMemoTests.EmptyBatch)
        Check("latest receipt history overrides unused master setting", AddressOf HistoryDefaults)
        Check("history uses own payment account and deterministic newest row", AddressOf HistoryOrderingAndOwnership)
        Check("failed receipt update rolls mode and total back together", AddressOf TransactionRollback)
        Check("receipt update replaces children with foreign keys on and off", AddressOf ForeignKeyTests.UpdateChildren)
        Check("receipt delete preserves neighbors with foreign keys on and off", AddressOf ForeignKeyTests.DeleteChildren)
        Check("late receipt update failure rolls all rows back with foreign keys on and off", AddressOf ForeignKeyTests.UpdateRollback)
        Check("late receipt delete failure rolls all rows back with foreign keys on and off", AddressOf ForeignKeyTests.DeleteRollback)
        Check("balance delete preserves neighbors with zero or multiple children and foreign keys on and off", AddressOf BalanceForeignKeyTests.DeleteChildren)
        Check("late balance delete failure restores all rows and allows retry with foreign keys on and off", AddressOf BalanceForeignKeyTests.DeleteRollback)
        Check("nullable receipt header text normalizes only NULL on both read paths", AddressOf NullTextTests.HeaderText)
        Check("nullable child tags normalize only NULL and reload without database changes", AddressOf NullTextTests.ChildText)
        Check("fully NULL receipt opens and cancels without changing saved data", AddressOf NullTextTests.OpenAndCancel)
        Check("nullable balance header tags normalize only NULL through all three readers", AddressOf BalanceTransferNullTests.HeaderTags)
        Check("nullable balance group tags preserve amounts and valuations through all three readers", AddressOf BalanceTransferNullTests.GroupTags)
        Check("nullable transfer tags preserve both read paths and all-NULL auto-balance", AddressOf BalanceTransferNullTests.TransferTags)
        Check("overlapping receipt/transfer/balance registration serializes sequence allocation with foreign keys on and off", AddressOf ConcurrencyTests.ConcurrentRegistration)
        Check("held write locks reject whole saves and allow same-connection retry with foreign keys on and off", AddressOf ConcurrencyTests.HeldWriteLock)
        Check("stale receipt/transfer/balance edits preserve the first writer with foreign keys on and off", AddressOf ConcurrencyTests.StaleEditsRejected)
        Check("stale receipt/transfer/balance deletes reject before changing children with foreign keys on and off", AddressOf RevisionTests.StaleDeletesRejected)
        Check("deleted and reused receipt/transfer/balance IDs reject old snapshots with foreign keys on and off", AddressOf RevisionTests.DeletedAndReusedIdsRejected)
        Check("current receipt/transfer/balance saves and copies publish fresh committed tokens with foreign keys on and off", AddressOf RevisionTests.CurrentSavesAndCopies)
        Check("late failed receipt/transfer/balance saves preserve tokens and allow retry with foreign keys on and off", AddressOf RevisionTests.FailedSavesPreserveTokens)
        Check("all seven receipt/transfer/balance readers return initial and refreshed tokens without writes", AddressOf RevisionTests.AllReaderTokens)
        Check("unknown receipt/transfer/balance tokens reject updates and deletes without writes", AddressOf RevisionTests.UnknownTokensRejected)
        Check("new receipt defaults and manual override UI", AddressOf NewReceiptUi)
        Check("edit previews tax choices without changing receipt until confirmed save", AddressOf EditUi)
        Check("declined confirmations leave receipt unchanged and keep form open", AddressOf CancelCopyUi)
        Check("copy confirmation immediately saves visible mode and closes", AddressOf ImmediateCopyUi)
        Check("current receipt/transfer/balance edit and delete confirmations still save and close", AddressOf RevisionUiTests.CurrentOperationsClose)
        Check("receipt/transfer/balance edit and delete conflicts warn and preserve open forms and input", AddressOf RevisionUiTests.ConflictsKeepInput)
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

    Friend Sub OpenForm(form As Form)
        form.Show()
        Application.DoEvents()
    End Sub

    Private Sub CommitShop(form As RegisterForm, shopName As String)
        Dim taxType = form.Receipt.TaxType
        Dim mode = form.Receipt.TaxRoundingMode
        Dim total = form.Receipt.Total
        Dim shops = DirectCast(form.ShopNameComboBox.DataSource, System.Collections.Generic.List(Of Shop))
        Dim index = shops.FindIndex(Function(s) s.Name = shopName)
        If index < 0 Then Throw New Exception("Missing shop candidate: " & shopName)
        form.ShopNameComboBox.SelectedIndex = index
        InvokeEvent(form, "ShopNameComboBox_SelectionChangeCommitted", form.ShopNameComboBox, EventArgs.Empty)
        AssertReceiptTaxState(form.Receipt, taxType, mode, total)
    End Sub

    Private Sub ValidateTypedShop(form As RegisterForm, shopName As String)
        Dim taxType = form.Receipt.TaxType
        Dim mode = form.Receipt.TaxRoundingMode
        Dim total = form.Receipt.Total
        form.ShopNameComboBox.Text = shopName
        InvokeEvent(form, "ShopNameComboBox_TextUpdate", form.ShopNameComboBox, EventArgs.Empty)
        AssertReceiptTaxState(form.Receipt, taxType, mode, total)
        InvokeEvent(form, "ShopNameComboBox_Validated", form.ShopNameComboBox, EventArgs.Empty)
        AssertReceiptTaxState(form.Receipt, taxType, mode, total)
    End Sub

    Private Sub AssertRoundingSelection(form As RegisterForm, mode As String)
        Equal(mode = "floor", form.TaxRoundingFloorRadioButton.Checked)
        Equal(mode = "half_up", form.TaxRoundingHalfUpRadioButton.Checked)
    End Sub

    Private Sub AssertTaxControls(form As RegisterForm, taxType As Integer, mode As String, total As Integer)
        Equal(taxType = 0, form.TaxInclusiveRadioButton.Checked)
        Equal(taxType = 1, form.TaxExclusiveRadioButton.Checked)
        AssertRoundingSelection(form, mode)
        Equal(total, Convert.ToInt32(form.TotalTextBox.Text))
        Equal(total, Convert.ToInt32(form.TotalTextBox2.Text))
    End Sub

    Private Sub AssertReceiptTaxState(receipt As Receipt, taxType As Integer, mode As String, total As Integer)
        Equal(taxType, receipt.TaxType)
        Equal(mode, receipt.TaxRoundingMode)
        Equal(total, receipt.Total)
    End Sub

    Private Sub SelectRounding(form As RegisterForm, mode As String)
        Dim taxType = form.Receipt.TaxType
        Dim originalMode = form.Receipt.TaxRoundingMode
        Dim total = form.Receipt.Total
        Dim inclusiveChecked = form.TaxInclusiveRadioButton.Checked
        Dim exclusiveChecked = form.TaxExclusiveRadioButton.Checked
        If mode = "half_up" Then
            form.TaxRoundingHalfUpRadioButton.PerformClick()
        Else
            form.TaxRoundingFloorRadioButton.PerformClick()
        End If
        AssertRoundingSelection(form, mode)
        AssertReceiptTaxState(form.Receipt, taxType, originalMode, total)
        Equal(inclusiveChecked, form.TaxInclusiveRadioButton.Checked)
        Equal(exclusiveChecked, form.TaxExclusiveRadioButton.Checked)
    End Sub

    Private Sub SelectTaxType(form As RegisterForm, taxType As Integer)
        Dim originalTaxType = form.Receipt.TaxType
        Dim originalMode = form.Receipt.TaxRoundingMode
        Dim total = form.Receipt.Total
        Dim selectedMode = If(form.TaxRoundingHalfUpRadioButton.Checked, "half_up", "floor")
        If taxType = 0 Then
            form.TaxInclusiveRadioButton.PerformClick()
        Else
            form.TaxExclusiveRadioButton.PerformClick()
        End If
        AssertReceiptTaxState(form.Receipt, originalTaxType, originalMode, total)
        Equal(taxType = 0, form.TaxInclusiveRadioButton.Checked)
        Equal(taxType = 1, form.TaxExclusiveRadioButton.Checked)
        AssertRoundingSelection(form, selectedMode)
    End Sub

    Private Sub NewReceiptUi()
        Using fixture As New DatabaseFixture
            fixture.AddReceipt(2, "registered shop", "half_up", New Date(2026, 10, 17))
            fixture.AddReceipt(3, "free text store", "half_up", New Date(2026, 10, 17))
            fixture.AddReceipt(4, "history shop", "half_up", New Date(2026, 10, 17))
            Using form As New RegisterForm With {.DataAccess = fixture.Data}
                OpenForm(form)
                AssertReceiptTaxState(form.Receipt, 0, "floor", 0)
                AssertRoundingSelection(form, "floor")
                CommitShop(form, "registered shop")
                AssertRoundingSelection(form, "half_up")
                CommitShop(form, "history shop")
                Equal(0, DirectCast(form.ShopNameComboBox.SelectedItem, Shop).Code)
                AssertRoundingSelection(form, "half_up")
                ValidateTypedShop(form, "free text store")
                AssertRoundingSelection(form, "half_up")
                ValidateTypedShop(form, "never used store")
                AssertRoundingSelection(form, "floor")
                ' An explicit click on the already-selected option is still a manual choice.
                SelectRounding(form, "floor")
                CommitShop(form, "registered shop")
                AssertRoundingSelection(form, "floor")
                SelectRounding(form, "half_up")
                CommitShop(form, "history shop")
                ValidateTypedShop(form, "never used store")
                AssertRoundingSelection(form, "half_up")
                SelectRounding(form, "floor")
                CommitShop(form, "registered shop")
                AssertRoundingSelection(form, "floor")
                AssertReceiptTaxState(form.Receipt, 0, "floor", 0)
                form.Close()
                AssertReceiptTaxState(form.Receipt, 0, "floor", 0)
            End Using
            Using form As New RegisterForm With {.DataAccess = fixture.Data}
                OpenForm(form)
                CommitShop(form, "registered shop")
                AssertRoundingSelection(form, "half_up")
                SelectRounding(form, "half_up")
                ValidateTypedShop(form, "never used store")
                AssertRoundingSelection(form, "half_up")
                AssertReceiptTaxState(form.Receipt, 0, "floor", 0)
                form.Close()
                AssertReceiptTaxState(form.Receipt, 0, "floor", 0)
            End Using
            ' History choices are pending screen values even when the new receipt has details.
            Dim receipt = MakeReceipt("floor", Group(10, Detail(19)), Group(10, Detail(19)))
            receipt.TaxType = 0
            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                OpenForm(form)
                ValidateTypedShop(form, "registered shop")
                AssertTaxControls(form, 1, "half_up", 42)
                Equal(True, Object.ReferenceEquals(receipt, form.Receipt))
                AssertReceiptTaxState(receipt, 0, "floor", 38)
                form.ReceiptDateTextBox.Value = New Date(2026, 10, 16)
                form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                form.RegiserButton.PerformClick()
                Equal(DialogResult.OK, form.DialogResult)
                Equal(5, receipt.Seq)
                AssertReceiptTaxState(receipt, 1, "half_up", 42)
            End Using
            Dim saved = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = receipt.Seq)
            AssertReceiptTaxState(saved, 1, "half_up", 42)
            Equal(42L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=5")))
        End Using
    End Sub

    Private Sub EditUi()
        For Each originalTaxType In New Integer() {1, 0}
            Using fixture As New DatabaseFixture
                Dim originalMode = If(originalTaxType = 1, "floor", "half_up")
                Dim originalTotal = If(originalTaxType = 1, 41, 38)
                If originalTaxType = 0 Then
                    fixture.Execute("UPDATE t_receipts SET tax_type=0,tax_rounding_mode='half_up',total=38 WHERE seq=1")
                End If
                Dim changedTaxType = 1 - originalTaxType
                Dim changedMode = If(originalMode = "floor", "half_up", "floor")
                Dim changedTotal = If(changedTaxType = 1, 41, 38)
                fixture.AddReceipt(2, "registered shop", changedMode, New Date(2026, 10, 17))
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                    OpenForm(form)
                    AssertTaxControls(form, originalTaxType, originalMode, originalTotal)
                    CommitShop(form, "registered shop")
                    AssertTaxControls(form, originalTaxType, originalMode, originalTotal)
                    SelectTaxType(form, changedTaxType)
                    SelectRounding(form, changedMode)
                    AssertTaxControls(form, changedTaxType, changedMode, changedTotal)
                    Equal(True, Object.ReferenceEquals(receipt, form.Receipt))
                    AssertReceiptTaxState(receipt, originalTaxType, originalMode, originalTotal)
                    form.Close()
                    Equal(originalTaxType, receipt.TaxType)
                    Equal(originalMode, receipt.TaxRoundingMode)
                    Equal(originalTotal, receipt.Total)
                End Using
                ' Reuse the shared in-memory receipt, rather than masking a cancel bug by reloading it.
                Using reopened As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                    OpenForm(reopened)
                    AssertTaxControls(reopened, originalTaxType, originalMode, originalTotal)
                    reopened.Close()
                End Using
                Equal(originalTaxType, Convert.ToInt32(fixture.Scalar("SELECT tax_type FROM t_receipts WHERE seq=1")))
                Equal(originalMode, Convert.ToString(fixture.Scalar("SELECT tax_rounding_mode FROM t_receipts WHERE seq=1")))
                Equal(originalTotal, Convert.ToInt32(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                    Dim closed As Boolean = False
                    AddHandler form.FormClosed, Sub(sender, args) closed = True
                    OpenForm(form)
                    form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                    SelectTaxType(form, changedTaxType)
                    SelectRounding(form, changedMode)
                    AssertTaxControls(form, changedTaxType, changedMode, changedTotal)
                    AssertReceiptTaxState(receipt, originalTaxType, originalMode, originalTotal)
                    Equal(False, closed)
                    ClickAndAnswer(form.RegiserButton, DialogResult.No, "Decline register",
                                   Sub() AssertReceiptTaxState(receipt, originalTaxType, originalMode, originalTotal))
                    Equal(False, closed)
                    Equal(True, form.Visible)
                    AssertTaxControls(form, changedTaxType, changedMode, changedTotal)
                    AssertReceiptTaxState(receipt, originalTaxType, originalMode, originalTotal)
                    Equal(originalTaxType, Convert.ToInt32(fixture.Scalar("SELECT tax_type FROM t_receipts WHERE seq=1")))
                    Equal(originalMode, Convert.ToString(fixture.Scalar("SELECT tax_rounding_mode FROM t_receipts WHERE seq=1")))
                    Equal(originalTotal, Convert.ToInt32(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                    ClickAndAnswer(form.RegiserButton, DialogResult.Yes, "Register",
                                   Sub() AssertReceiptTaxState(receipt, originalTaxType, originalMode, originalTotal))
                    Equal(True, closed)
                    Equal(DialogResult.OK, form.DialogResult)
                    Equal(changedTaxType, receipt.TaxType)
                    Equal(changedMode, receipt.TaxRoundingMode)
                    Equal(changedTotal, receipt.Total)
                End Using
                Dim saved = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                Equal(changedTaxType, saved.TaxType)
                Equal(changedMode, saved.TaxRoundingMode)
                Equal(changedTotal, Convert.ToInt32(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                Using reopened As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = saved}
                    OpenForm(reopened)
                    AssertTaxControls(reopened, changedTaxType, changedMode, changedTotal)
                    reopened.Close()
                End Using
            End Using
        Next
    End Sub

    Private Sub CancelCopyUi()
        Using fixture As New DatabaseFixture
            Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
            Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                OpenForm(form)
                SelectTaxType(form, 0)
                SelectRounding(form, "half_up")
                AssertTaxControls(form, 0, "half_up", 38)
                AssertReceiptTaxState(receipt, 1, "floor", 41)
                ' The earliest confirmation must also leave pending choices off the model.
                form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16)
                ClickAndAnswer(form.CopyButton, DialogResult.No, "Unset time",
                               Sub() AssertReceiptTaxState(receipt, 1, "floor", 41))
                AssertReceiptTaxState(receipt, 1, "floor", 41)
                form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                ClickAndAnswer(form.CopyButton, DialogResult.No, "Copy",
                               Sub() AssertReceiptTaxState(receipt, 1, "floor", 41))
                Equal(1L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipts")))
                Equal(1, receipt.Seq)
                Equal(True, form.Visible)
                AssertTaxControls(form, 0, "half_up", 38)
                Equal(True, Object.ReferenceEquals(receipt, form.Receipt))
                AssertReceiptTaxState(receipt, 1, "floor", 41)
                Equal(1, Convert.ToInt32(fixture.Scalar("SELECT tax_type FROM t_receipts WHERE seq=1")))
                Equal("floor", Convert.ToString(fixture.Scalar("SELECT tax_rounding_mode FROM t_receipts WHERE seq=1")))
                Equal(41L, Convert.ToInt64(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=1")))
                form.Close()
                AssertReceiptTaxState(receipt, 1, "floor", 41)
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
                    Dim closed As Boolean = False
                    AddHandler form.FormClosed, Sub(sender, args) closed = True
                    OpenForm(form)
                    CommitShop(form, "registered shop")
                    form.ReceiptTimeTextBox.Value = New Date(2026, 10, 16, 12, 0, 0)
                    Dim visibleTaxType = If(visibleMode = "half_up", 1, 0)
                    Dim visibleTotal = If(visibleTaxType = 1, 42, 38)
                    SelectTaxType(form, visibleTaxType)
                    SelectRounding(form, visibleMode)
                    AssertTaxControls(form, visibleTaxType, visibleMode, visibleTotal)
                    Equal(True, Object.ReferenceEquals(receipt, form.Receipt))
                    AssertReceiptTaxState(receipt, 1, "floor", 41)
                    Equal(historyMode, fixture.Data.GetTaxRoundingMode("registered shop"))
                    ClickAndAnswer(form.CopyButton, DialogResult.Yes, "Copy",
                                   Sub() AssertReceiptTaxState(receipt, 1, "floor", 41))
                    ' No subsequent Register click: the actual Copy handler must save and close.
                    Equal(True, closed)
                    Equal(DialogResult.OK, form.DialogResult)
                    Equal(3L, Convert.ToInt64(fixture.Scalar("SELECT count(*) FROM t_receipts")))
                    Equal(3, receipt.Seq)
                    AssertReceiptTaxState(receipt, visibleTaxType, visibleMode, visibleTotal)
                    Dim persisted = fixture.Data.GetReceipts(2026, 10)
                    Dim source = persisted.Single(Function(r) r.Seq = 1)
                    AssertReceiptTaxState(source, 1, "floor", 41)
                    Equal("history shop", source.ShopName)
                    Dim copy = persisted.Single(Function(r) r.Seq = 3)
                    AssertReceiptTaxState(copy, visibleTaxType, visibleMode, visibleTotal)
                    Equal("registered shop", copy.ShopName)
                    Equal(copy.Total, Convert.ToInt32(fixture.Scalar("SELECT total FROM t_receipts WHERE seq=3")))
                    Equal(2, copy.Groups.Count)
                    Equal(1, copy.Groups(0).Details(0).Users.Count)
                End Using
            End Using
        Next
    End Sub

    ' 確認のYes/NoはIDで選び、競合警告は表示言語に依存せず単一ボタンを選ぶ。
    ' Only a confirmation dialog owned by this test process is eligible for interaction.
    Private Delegate Function EnumWindowCallback(window As IntPtr, parameter As IntPtr) As Boolean

    <DllImport("user32.dll")>
    Private Function EnumWindows(callback As EnumWindowCallback, parameter As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function EnumChildWindows(parent As IntPtr, callback As EnumWindowCallback, parameter As IntPtr) As Boolean
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

    <DllImport("user32.dll")>
    Private Function GetDlgCtrlID(control As IntPtr) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function PostMessage(window As IntPtr, message As UInteger, wParam As IntPtr, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function IsWindowVisible(window As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function IsWindowEnabled(window As IntPtr) As Boolean
    End Function

    Private Function FindOwnConfirmation(Optional expectedTitle As String = "確認") As IntPtr
        Dim result As IntPtr
        Dim ownProcessId = CUInt(Process.GetCurrentProcess().Id)
        EnumWindows(
            Function(window, parameter)
                Dim processId As UInteger
                GetWindowThreadProcessId(window, processId)
                If processId <> ownProcessId Then Return True
                If Not IsWindowVisible(window) OrElse Not IsWindowEnabled(window) Then Return True
                Dim title As New StringBuilder(128)
                Dim className As New StringBuilder(128)
                GetWindowText(window, title, title.Capacity)
                GetClassName(window, className, className.Capacity)
                If title.ToString() <> expectedTitle OrElse className.ToString() <> "#32770" Then Return True
                result = window
                Return False
            End Function, IntPtr.Zero)
        Return result
    End Function

    Private Function DialogButtons(dialog As IntPtr, Optional availableOnly As Boolean = False) As System.Collections.Generic.List(Of IntPtr)
        Dim result As New System.Collections.Generic.List(Of IntPtr)()
        EnumChildWindows(dialog,
            Function(window, parameter)
                Dim className As New StringBuilder(128)
                GetClassName(window, className, className.Capacity)
                If Not String.Equals(className.ToString(), "Button", StringComparison.OrdinalIgnoreCase) Then Return True
                If availableOnly AndAlso (Not IsWindowVisible(window) OrElse Not IsWindowEnabled(window)) Then Return True
                result.Add(window)
                Return True
            End Function, IntPtr.Zero)
        Return result
    End Function

    Private Function DialogButtonDiagnostics(dialog As IntPtr) As String
        Dim result As New StringBuilder()
        For Each button In DialogButtons(dialog)
            Dim caption As New StringBuilder(128)
            GetWindowText(button, caption, caption.Capacity)
            result.Append(" id=").Append(GetDlgCtrlID(button)).Append(" caption=[").Append(caption).
                Append("] visible=").Append(IsWindowVisible(button)).Append(" enabled=").Append(IsWindowEnabled(button)).Append(";")
        Next
        Return result.ToString()
    End Function

    Private Function OwnDialogDiagnostics() As String
        Dim result As New StringBuilder()
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
                If className.ToString() <> "#32770" Then Return True
                result.Append(" title=[").Append(title).Append("] visible=").Append(IsWindowVisible(window)).
                    Append(" enabled=").Append(IsWindowEnabled(window)).
                    Append(" buttons=[").Append(DialogButtonDiagnostics(window)).Append("];")
                Return True
            End Function, IntPtr.Zero)
        Return result.ToString()
    End Function

    Friend Sub ClickAndAnswer(buttonToClick As Button, answer As DialogResult, operation As String,
                              Optional beforeAnswer As Action = Nothing, Optional expectedConflict As Boolean = False)
        Dim answered As Integer
        Dim warnings As Integer
        Dim clickedDialogs As New System.Collections.Generic.HashSet(Of String)()
        Dim assertionFailure As Exception = Nothing
        Dim watch = Stopwatch.StartNew()
        Using responder As New System.Windows.Forms.Timer With {.Interval = 50}
            AddHandler responder.Tick,
                Sub(sender, args)
                    If watch.Elapsed > TimeSpan.FromSeconds(15) Then
                        Dim diagnostic = operation & " dialogs did not complete within 15 seconds; confirmations=" & answered.ToString() &
                            "; warnings=" & warnings.ToString() & "; dialogs:" & OwnDialogDiagnostics()
                        Console.Error.WriteLine("FAIL " & diagnostic)
                        Console.Error.Flush()
                        Environment.FailFast(diagnostic)
                    End If
                    Dim dialog = FindOwnConfirmation("更新の競合")
                    If dialog <> IntPtr.Zero Then
                        Dim buttons = DialogButtons(dialog, availableOnly:=True)
                        ' 作成途中や想定外の複数ボタンでは押下せず、期限時に実IDを報告する。
                        If buttons.Count <> 1 Then Return
                        Dim okButton = buttons.Single()
                        If Not clickedDialogs.Add(dialog.ToString() & "|conflict") Then Return
                        warnings += 1
                        Console.WriteLine("INFO " & operation & " conflict warning button id=" & GetDlgCtrlID(okButton).ToString())
                        ' モーダル画面の切替はタイマーを抜けてから処理する。
                        If Not PostMessage(okButton, &HF5UI, IntPtr.Zero, IntPtr.Zero) Then
                            assertionFailure = New Exception(operation & " could not post the conflict OK button: " & Marshal.GetLastWin32Error().ToString())
                        End If
                        Return
                    End If
                    dialog = FindOwnConfirmation()
                    If dialog = IntPtr.Zero Then Return
                    If answered = 0 AndAlso beforeAnswer IsNot Nothing Then
                        Try
                            beforeAnswer()
                        Catch ex As Exception
                            ' Decline and close the dialog before surfacing an assertion failure.
                            assertionFailure = ex
                        End Try
                    End If
                    ' An unexpected extra confirmation is declined, then reported after the handler returns.
                    Dim button = GetDlgItem(dialog, If(answered = 0 AndAlso answer = DialogResult.Yes AndAlso assertionFailure Is Nothing, 6, 7))
                    If button = IntPtr.Zero Then Return
                    If Not clickedDialogs.Add(dialog.ToString() & "|confirmation") Then Return
                    answered += 1
                    If Not PostMessage(button, &HF5UI, IntPtr.Zero, IntPtr.Zero) Then ' BM_CLICK
                        assertionFailure = New Exception(operation & " could not post the confirmation button: " & Marshal.GetLastWin32Error().ToString())
                    End If
                End Sub
            responder.Start()
            Try
                buttonToClick.PerformClick()
            Finally
                responder.Stop()
            End Try
        End Using
        If assertionFailure IsNot Nothing Then Throw assertionFailure
        Equal(1, answered, operation & " must show exactly one confirmation")
        Equal(If(expectedConflict, 1, 0), warnings, operation & " must show the expected conflict warning")
    End Sub
End Module
