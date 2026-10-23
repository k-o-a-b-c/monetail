Imports System.Data.SQLite
Imports System.Linq

Friend Module ReceiptReadTests
    Public Sub HierarchyAndReferences()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Dim receipts = fixture.Data.GetReceipts(2026, 10)
                        AssertIds("5,3,2,1,0", receipts)
                        Dim receipt = receipts.Single(Function(r) r.Seq = 1)
                        Equal("history shop", receipt.ShopName)
                        Equal(String.Empty, receipt.ShopLocation)
                        Equal(String.Empty, receipt.Tag)
                        Equal("reader-token", receipt.RevisionToken)
                        Equal("half_up", receipt.TaxRoundingMode)
                        Equal(225, receipt.Total, "19 at 10% and 195 at 8%, half_up, less 7 non-taxable")
                        Equal("0,1,2", String.Join(",", receipt.Groups.Select(Function(g) g.Seq)))
                        Equal(0, receipt.Groups(0).Details.Single().Seq)
                        Equal(0, receipt.Groups(0).Details.Single().Users.Count)
                        Equal("1,3", String.Join(",", receipt.Groups(1).Details.Select(Function(d) d.Seq)))
                        Equal("1,2", String.Join(",", receipt.Groups(1).Details(0).Users.Select(Function(u) u.Code)))
                        Equal("2", String.Join(",", receipt.Groups(1).Details(1).Users.Select(Function(u) u.Code)))
                        Equal(2.5D, receipt.Groups(1).Details(0).DiscountRate)
                        Equal(0, receipt.Groups(2).Details.Count)
                        Equal(0, receipts.Single(Function(r) r.Seq = 2).Groups.Count)
                        Equal(0, receipts.Single(Function(r) r.Seq = 0).Total)
                        Equal(20, receipts.Single(Function(r) r.Seq = 3).Total)

                        ' 改修前の参照読込と、全ヘッダー・子・マスタ参照を比較する。
                        For Each payment In fixture.Data.PaymentClassificationList
                            Dim legacy = PaymentReceiptReadTests.ReadLegacy(fixture, New Date(2026, 10, 1), New Date(2026, 10, 31, 23, 59, 59), payment)
                            For Each original In legacy
                                AssertSame(original, receipts.Single(Function(r) r.Seq = original.Seq))
                            Next
                            Equal(legacy.Count, receipts.Where(Function(r) r.PaymentClassification Is payment).Count())
                        Next
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub FiltersAndBoundaries()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        AssertIds("5,3,2,1,0", fixture.Data.GetReceipts(New Date(2026, 10, 29)))
                        AssertIds("9,7,6,5,3,2,1,0,4", fixture.Data.GetReceipts(2026))
                        AssertIds("", fixture.Data.GetReceipts(2025))
                        AssertIds("5,3,1,0", fixture.Data.GetReceipts(2026, 10, PaymentCode:=1))
                        AssertIds("2", fixture.Data.GetReceipts(2026, 10, PaymentCode:=2))
                        AssertIds("", fixture.Data.GetReceipts(2026, 10, PaymentCode:=Integer.MinValue))
                        For Each classification In {1, 2, 999}
                            Dim receipts = fixture.Data.GetReceipts(2026, 10, GroupCode:=classification)
                            ' 分類がなくても親を除外せず、対象のグループだけを返す。
                            AssertIds("5,3,2,1,0", receipts)
                            Equal(True, receipts.SelectMany(Function(r) r.Groups).All(Function(g) g.GroupClassification.Code = classification))
                            Dim receipt = receipts.Single(Function(r) r.Seq = 1)
                            Equal(If(classification = 1, "0,1", If(classification = 2, "2", "")),
                                String.Join(",", receipt.Groups.Select(Function(g) g.Seq)))
                            Equal(If(classification = 1, 225, 0), receipt.Total)
                        Next
                        Dim combined = fixture.Data.GetReceipts(2026, 10, GroupCode:=2, PaymentCode:=1)
                        AssertIds("5,3,1,0", combined)
                        Equal(20, combined.Single(Function(r) r.Seq = 3).Total)
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub SingleSelectAndReload()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Seed(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        For Each selectedMonth In {10, 0}
                            Dim reads = 0
                            Dim observe As SQLiteTraceEventHandler =
                                Sub(sender, args)
                                    Dim sql = args.Statement.Trim().ToLowerInvariant()
                                    If sql.StartsWith("select ", StringComparison.Ordinal) AndAlso sql.Contains("from t_receipt") Then reads += 1
                                End Sub
                            AddHandler fixture.ActualDataConnection.Trace, observe
                            Try
                                fixture.Data.GetReceipts(2026, selectedMonth)
                            Finally
                                RemoveHandler fixture.ActualDataConnection.Trace, observe
                            End Try
                            Equal(1, reads, "The receipt hierarchy must use one SELECT, excluding master reads")
                        Next
                        Dim receipt = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                        Dim fresh = fixture.Data.GetReceipts(2026, 10).Single(Function(r) r.Seq = 1)
                        Equal(False, Object.ReferenceEquals(receipt, fresh))
                        Equal(False, Object.ReferenceEquals(receipt.Groups(1).Details(0), fresh.Groups(1).Details(0)))
                        receipt.Groups(1).Details(0).Fee = 0
                        receipt.Groups(1).Details(0).Users.Clear()
                        fixture.Data.LoadReceiptChildData(receipt)
                        AssertSame(fresh, receipt)
                    End Sub)
            End Using
        Next
    End Sub

    Private Sub AssertIds(expected As String, receipts As List(Of Receipt))
        Equal(expected, String.Join(",", receipts.Select(Function(r) r.Seq)))
    End Sub

    Friend Sub AssertSame(expected As Receipt, actual As Receipt)
        Equal(expected.Seq, actual.Seq)
        Equal(expected.Date, actual.Date)
        Equal(expected.ShopName, actual.ShopName)
        Equal(expected.ShopLocation, actual.ShopLocation)
        Equal(expected.TaxType, actual.TaxType)
        Equal(expected.TaxRoundingMode, actual.TaxRoundingMode)
        Equal(expected.Tag, actual.Tag)
        Equal(expected.RevisionToken, actual.RevisionToken)
        Equal(True, Object.ReferenceEquals(expected.PaymentClassification, actual.PaymentClassification))
        Equal(expected.Total, actual.Total)
        Equal(expected.Incomes, actual.Incomes)
        Equal(expected.Expenses, actual.Expenses)
        Equal(0, actual.Points.Count)
        Equal(expected.Groups.Count, actual.Groups.Count)
        For index = 0 To expected.Groups.Count - 1
            Dim before = expected.Groups(index)
            Dim after = actual.Groups(index)
            Equal(before.ReceiptSeq, after.ReceiptSeq)
            Equal(before.Seq, after.Seq)
            Equal(before.Tag, after.Tag)
            Equal(True, Object.ReferenceEquals(before.GroupClassification, after.GroupClassification))
            Equal(True, Object.ReferenceEquals(before.TaxRate, after.TaxRate))
            Equal(before.Details.Count, after.Details.Count)
            For detailIndex = 0 To before.Details.Count - 1
                Dim original = before.Details(detailIndex)
                Dim loaded = after.Details(detailIndex)
                Equal(original.ReceiptSeq, loaded.ReceiptSeq)
                Equal(original.GroupSeq, loaded.GroupSeq)
                Equal(original.Seq, loaded.Seq)
                Equal(original.Name, loaded.Name)
                Equal(original.Fee, loaded.Fee)
                Equal(original.Quantity, loaded.Quantity)
                Equal(original.DiscountFee, loaded.DiscountFee)
                Equal(original.DiscountRate, loaded.DiscountRate)
                Equal(original.TaxTarget, loaded.TaxTarget)
                Equal(original.Tag, loaded.Tag)
                Equal(original.Users.Count, loaded.Users.Count)
                For userIndex = 0 To original.Users.Count - 1
                    Equal(True, Object.ReferenceEquals(original.Users(userIndex), loaded.Users(userIndex)))
                Next
            Next
        Next
    End Sub

    Friend Sub Seed(fixture As DatabaseFixture)
        fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'second user','synthetic-account',2);" &
            "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'second payment',2,1);" &
            "INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(2,'second group',2,1,0);" &
            "UPDATE t_receipts SET shop_location=NULL,tag=NULL,tax_rounding_mode='half_up',revision_token='reader-token' WHERE seq=1;" &
            "DELETE FROM t_receipt_details WHERE receipt_seq=1 AND group_seq=2;" &
            "UPDATE t_receipt_groups SET tax=4,tag='group ''tag''' WHERE receipt_seq=1 AND seq=1;" &
            "UPDATE t_receipt_groups SET group_classification_code=2,tag=NULL WHERE receipt_seq=1 AND seq=2;" &
            "UPDATE t_receipt_details SET fee=103,quantity=2,discount_fee=3,discount_rate=2.5,tag=NULL WHERE receipt_seq=1 AND group_seq=1;" &
            "INSERT INTO t_receipt_detail_users VALUES(1,1,1,2);" &
            "INSERT INTO t_receipt_details VALUES(1,1,3,'negative',-7,1,0,0,0,'detail ''tag''');" &
            "INSERT INTO t_receipt_detail_users VALUES(1,1,3,2);" &
            "INSERT INTO t_receipt_groups VALUES(1,0,1,NULL,5);" &
            "INSERT INTO t_receipt_details VALUES(1,0,0,'zero sequence',19,1,0,0,1,NULL);")
        fixture.AddReceipt(0, "same date", "floor", New Date(2026, 10, 16, 12, 0, 0))
        fixture.AddReceipt(2, "no groups", "floor", New Date(2026, 10, 16, 12, 0, 0), paymentCode:=2)
        fixture.Execute("DELETE FROM t_receipt_details WHERE receipt_seq=2;DELETE FROM t_receipt_groups WHERE receipt_seq=2;")
        fixture.AddReceipt(3, "second classification", "floor", New Date(2026, 10, 20))
        fixture.Execute("UPDATE t_receipt_groups SET group_classification_code=2 WHERE receipt_seq=3;" &
            "UPDATE t_receipt_details SET fee=19 WHERE receipt_seq=3;")
        fixture.AddReceipt(4, "previous month", "floor", New Date(2026, 9, 30, 23, 59, 59))
        fixture.AddReceipt(5, "last second", "floor", New Date(2026, 10, 31, 23, 59, 59))
        fixture.AddReceipt(6, "fractional last second", "floor", New Date(2026, 10, 31, 23, 59, 59).AddMilliseconds(500))
        fixture.AddReceipt(7, "next month", "floor", New Date(2026, 11, 1))
        fixture.AddReceipt(8, "fractional year end", "floor", New Date(2026, 12, 31, 23, 59, 59).AddMilliseconds(500))
        fixture.AddReceipt(9, "last year second", "floor", New Date(2026, 12, 31, 23, 59, 59))
    End Sub
End Module
