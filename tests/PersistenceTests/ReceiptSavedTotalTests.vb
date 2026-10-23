Imports System.Globalization
Imports System.Linq

Friend Module ReceiptSavedTotalTests
    Public Sub StoredValuesAndReadPaths()
        EachMode(
            Sub(fixture, mode)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Equal(False, (New Receipt()).SavedTotal.HasValue)
                        Dim reads = {fixture.Data.GetReceipts(2026, 10), fixture.Data.GetReceipts(New Date(2026, 10, 1)),
                                     fixture.Data.GetReceipts(2026), fixture.Data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1), fixture.Data.PaymentClassification(1))}
                        For Each receipts In reads
                            Equal(8, receipts.Count)
                            Equal(CLng(If(mode = "floor", 41, 42)), receipts.Single(Function(r) r.Seq = 1).SavedTotal.Value)
                            Equal(100L, receipts.Single(Function(r) r.Seq = 2).SavedTotal.Value)
                            Equal(101, receipts.Single(Function(r) r.Seq = 2).Total)
                            Equal(False, receipts.Single(Function(r) r.Seq = 4).SavedTotal.HasValue)
                            Equal(Long.MinValue, receipts.Single(Function(r) r.Seq = 5).SavedTotal.Value)
                            Equal(Long.MaxValue, receipts.Single(Function(r) r.Seq = 6).SavedTotal.Value)
                            Equal(0L, receipts.Single(Function(r) r.Seq = 8).SavedTotal.Value)
                        Next
                        Dim receipt = reads(0).Single(Function(r) r.Seq = 2)
                        fixture.Data.LoadReceiptChildData(receipt)
                        Equal(100L, receipt.SavedTotal.Value)
                        Equal(101, receipt.Total)
                    End Sub)
            End Sub)
    End Sub

    Public Sub ColorsAndTooltips()
        EachMode(
            Sub(fixture, mode)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New ViewForm(fixture.Data)
                            Program.OpenForm(form)
                            form.InitReceiptList()
                            Dim previousCulture = CultureInfo.CurrentCulture
                            Try
                                For Each culture In {"en-US", "de-DE"}
                                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture)
                                    AssertMismatch(form, 2, 100, 101, 1)
                                    AssertMismatch(form, 3, 100, 99, -1)
                                    AssertMismatch(form, 5, Long.MinValue, Integer.MaxValue, 9223372039002259455D)
                                    AssertMismatch(form, 6, Long.MaxValue, Integer.MinValue, -9223372039002259455D)
                                Next
                            Finally
                                CultureInfo.CurrentCulture = previousCulture
                            End Try
                            Dim equalCell = TotalCell(form, 1)
                            Equal(Color.MistyRose, FormatTotal(form, 1).BackColor)
                            Equal("", equalCell.ToolTipText)
                            Equal(Color.MistyRose, FormatTotal(form, 8).BackColor)
                            Equal("", TotalCell(form, 8).ToolTipText)
                            Dim missing = TotalCell(form, 4)
                            Equal(Color.MistyRose, FormatTotal(form, 4).BackColor)
                            Equal("保存時の合計が未記録のため比較できません。", missing.ToolTipText)
                            Dim overflow = TotalCell(form, 7)
                            Equal(Color.MistyRose, FormatTotal(form, 7).BackColor)
                            Equal("範囲超過", CStr(overflow.Value))
                            Equal(True, overflow.ToolTipText.Contains("扱える範囲を超えています"))
                            Equal(0, form.ReceiptListNotifications)
                            form.Close()
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Public Sub RefreshAndFilters()
        EachMode(
            Sub(fixture, mode)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using form As New ViewForm(fixture.Data)
                            Program.OpenForm(form)
                            form.InitReceiptList()
                            Dim receipt = form.ReceiptModelForTest(2)
                            Equal(Color.LightYellow, FormatTotal(form, 2).BackColor)
                            receipt.Groups(0).Details(0).Fee = 100
                            Equal(Color.MistyRose, FormatTotal(form, 2).BackColor)
                            Equal("", TotalCell(form, 2).ToolTipText)
                            Equal(100L, receipt.SavedTotal.Value)
                            receipt.Groups(0).Details(0).Fee = 102
                            AssertMismatch(form, 2, 100, 102, 2)
                            form.PaymentComboBox.SelectedValue = 2
                            Equal(0, form.HouseholdAccountDataGridView.Rows.Count)
                            form.PaymentComboBox.SelectedIndex = 0
                            Equal(8, form.HouseholdAccountDataGridView.Rows.Count)
                            AssertMismatch(form, 2, 100, 102, 2)
                            form.RefreshReceiptList(New Date(2026, 11, 1))
                            Equal(0, form.HouseholdAccountDataGridView.Rows.Count)
                            form.RefreshReceiptList(New Date(2026, 10, 1))
                            AssertMismatch(form, 2, 100, 101, 1)
                            Equal(False, form.ReceiptModelForTest(4).SavedTotal.HasValue)
                            Equal(0, form.ReceiptListNotifications)
                            form.Close()
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Private Sub AssertMismatch(form As ViewForm, seq As Integer, saved As Long, calculated As Integer, difference As Decimal)
        Dim cell = TotalCell(form, seq)
        Dim style = FormatTotal(form, seq)
        Equal(Color.LightYellow, style.BackColor)
        Equal(Color.Goldenrod, style.SelectionBackColor)
        Equal(Color.Black, style.SelectionForeColor)
        Equal(Color.MistyRose, cell.OwningRow.DefaultCellStyle.BackColor)
        Equal(calculated, CInt(cell.Value))
        Equal("保存値: " & saved.ToString("N0") & " 円" & Environment.NewLine &
              "再計算値: " & calculated.ToString("N0") & " 円" & Environment.NewLine &
              "差額（再計算値－保存値）: " & difference.ToString("+#,0;-#,0;0") & " 円", cell.ToolTipText)
    End Sub

    Private Function TotalCell(form As ViewForm, seq As Integer) As DataGridViewCell
        Return form.HouseholdAccountDataGridView.Rows.Cast(Of DataGridViewRow)().Single(
            Function(row) CInt(row.Cells(form.ReceiptSeqColumn.Index).Value) = seq).Cells(form.ReceiptTotalColumn.Index)
    End Function

    ' 製品のCellFormattingイベント後の表示色と、実際の書式適用を確認する。
    Private Function FormatTotal(form As ViewForm, seq As Integer) As DataGridViewCellStyle
        Dim cell = TotalCell(form, seq)
        Dim result As DataGridViewCellStyle = Nothing
        Dim capture As DataGridViewCellFormattingEventHandler =
            Sub(sender, args)
                If args.RowIndex = cell.RowIndex AndAlso args.ColumnIndex = cell.ColumnIndex Then result = args.CellStyle.Clone()
            End Sub
        AddHandler form.HouseholdAccountDataGridView.CellFormatting, capture
        Try
            Dim formatted = cell.FormattedValue
            If TypeOf cell.Value Is Integer Then Equal(CInt(cell.Value).ToString("N0"), CStr(formatted))
        Finally
            RemoveHandler form.HouseholdAccountDataGridView.CellFormatting, capture
        End Try
        Equal(True, result IsNot Nothing)
        Return result
    End Function

    Private Sub EachMode(action As Action(Of DatabaseFixture, String))
        For Each foreignKeys In {False, True}
            For Each mode In {"floor", "half_up"}
                Using fixture As New DatabaseFixture(foreignKeys)
                    fixture.Execute("UPDATE t_receipts SET tax_rounding_mode='" & mode & "',total=" & If(mode = "floor", "41", "42") & " WHERE seq=1;" &
                                    "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'unused bank',2,1);")
                    For seq = 2 To 8
                        fixture.AddReceipt(seq, "comparison " & seq, mode, New Date(2026, 10, 16, 12, 0, seq))
                    Next
                    fixture.Execute("UPDATE t_receipts SET tax_type=0,total=100 WHERE seq BETWEEN 2 AND 7;" &
                        "UPDATE t_receipt_details SET tax_target=0,fee=100 WHERE receipt_seq BETWEEN 2 AND 7;" &
                        "UPDATE t_receipt_details SET fee=101 WHERE receipt_seq=2;" &
                        "UPDATE t_receipt_details SET fee=99 WHERE receipt_seq=3;" &
                        "UPDATE t_receipts SET total=NULL WHERE seq=4;" &
                        "UPDATE t_receipts SET total=-9223372036854775808 WHERE seq=5;" &
                        "UPDATE t_receipt_details SET fee=2147483647 WHERE receipt_seq=5;" &
                        "UPDATE t_receipts SET total=9223372036854775807 WHERE seq=6;" &
                        "UPDATE t_receipt_details SET fee=-2147483648 WHERE receipt_seq=6;" &
                        "UPDATE t_receipt_details SET fee=2000000000,quantity=2 WHERE receipt_seq=7;")
                    action(fixture, mode)
                End Using
            Next
        Next
    End Sub
End Module
