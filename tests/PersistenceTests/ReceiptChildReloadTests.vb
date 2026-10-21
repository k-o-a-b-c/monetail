Imports System.Linq
Imports System.Reflection

Friend Module ReceiptChildReloadTests
    Public Sub NumericFailureAndRetry()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
                Dim legacy = fixture.Data.GetReceipts(2026, 10).Single()
                Dim assertOriginal = Capture(fixture, receipt)
                Dim groups = receipt.Groups
                Dim oldDetail = groups(1).Details.Single()
                ' INTEGER宣言でもREALを保存できる。2つ目のグループで実GetInt32を失敗させる。
                fixture.Execute("UPDATE t_receipt_details SET quantity=1.5 WHERE receipt_seq=1 AND group_seq=2;")
                Equal("real", CStr(fixture.Scalar("SELECT typeof(quantity) FROM t_receipt_details WHERE receipt_seq=1 AND group_seq=2")))
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        LegacyFailure(Of InvalidCastException)(fixture, legacy)
                        For attempt = 1 To 2
                            Throws(Of InvalidCastException)(Sub() fixture.Data.LoadReceiptChildData(receipt))
                            assertOriginal()
                        Next
                    End Sub)
                fixture.Execute("UPDATE t_receipt_details SET quantity=2 WHERE receipt_seq=1 AND group_seq=2;")
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        fixture.Data.LoadReceiptChildData(receipt)
                        Equal(True, Object.ReferenceEquals(groups, receipt.Groups))
                        ReceiptReadTests.AssertSame(fixture.Data.GetReceipts(2026, 10).Single(), receipt)
                        Equal(2, receipt.Groups(1).Details.Single().Quantity)
                        Equal(62, receipt.Total)
                        Equal(1, oldDetail.Quantity, "Previously held child objects must remain unchanged")
                    End Sub)
            End Using
        Next
    End Sub

    Public Sub MissingUserAndRetry()
        ' FK OFFでのみ作れる孤児割当を使い、数値変換以外の途中失敗も確認する。
        Using fixture As New DatabaseFixture(False)
            Dim receipt = fixture.Data.GetReceipts(2026, 10).Single()
            Dim legacy = fixture.Data.GetReceipts(2026, 10).Single()
            Dim assertOriginal = Capture(fixture, receipt)
            Dim groups = receipt.Groups
            fixture.Execute("INSERT INTO t_receipt_detail_users VALUES(1,2,1,999);")
            BalanceBoundaryTests.ReadOnlyCase(fixture,
                Sub()
                    LegacyFailure(Of InvalidOperationException)(fixture, legacy)
                    Throws(Of InvalidOperationException)(Sub() fixture.Data.LoadReceiptChildData(receipt))
                    assertOriginal()
                End Sub)
            fixture.Execute("DELETE FROM t_receipt_detail_users WHERE user_code=999;INSERT INTO t_receipt_detail_users VALUES(1,2,1,1);")
            BalanceBoundaryTests.ReadOnlyCase(fixture,
                Sub()
                    fixture.Data.LoadReceiptChildData(receipt)
                    Equal(True, Object.ReferenceEquals(groups, receipt.Groups))
                    ReceiptReadTests.AssertSame(fixture.Data.GetReceipts(2026, 10).Single(), receipt)
                    Equal(True, Object.ReferenceEquals(fixture.Data.UserList.Single(), receipt.Groups(1).Details.Single().Users.Single()))
                End Sub)
        End Using
    End Sub

    Public Sub SuccessAndEmptyChildren()
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                Dim receipt As New Receipt With {.Seq = 1, .Date = New Date(2025, 4, 5, 6, 7, 8),
                    .ShopName = "pending shop", .ShopLocation = "pending location", .TaxType = 0,
                    .TaxRoundingMode = "half_up", .PaymentClassification = fixture.Data.PaymentClassification(1),
                    .Tag = "pending tag", .RevisionToken = "pending token"}
                receipt.Points.Add(New ReceiptPoint With {.Seq = 7, .FixedPoints = 3})
                receipt.Groups.Add(New ReceiptGroup With {.Seq = 99, .Tag = "unsaved child"})
                Dim groups = receipt.Groups
                Dim assertHeader = CaptureHeader(receipt)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        For attempt = 1 To 2
                            fixture.Data.LoadReceiptChildData(receipt)
                            assertHeader()
                            Equal(True, Object.ReferenceEquals(groups, receipt.Groups))
                            Equal("1,2", String.Join(",", receipt.Groups.Select(Function(g) g.Seq)))
                            Equal(38, receipt.Total, "Child reload must retain the caller's tax settings")
                            Equal(19, receipt.Groups(0).Details.Single().Fee)
                            Equal(True, Object.ReferenceEquals(fixture.Data.UserList.Single(), receipt.Groups(0).Details.Single().Users.Single()))
                        Next
                    End Sub)
                receipt.Seq = 999
                Dim assertEmptyHeader = CaptureHeader(receipt)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        fixture.Data.LoadReceiptChildData(receipt)
                        assertEmptyHeader()
                        Equal(True, Object.ReferenceEquals(groups, receipt.Groups))
                        Equal(0, receipt.Groups.Count, "A successful empty read must clear previous children")
                    End Sub)
            End Using
        Next
    End Sub

    Private Function Capture(fixture As DatabaseFixture, receipt As Receipt) As Action
        Dim expected = fixture.Data.GetReceipts(2026, 10).Single()
        Dim collection = receipt.Groups
        Dim groups = collection.ToArray()
        Dim detailLists = groups.Select(Function(g) g.Details).ToArray()
        Dim details = groups.SelectMany(Function(g) g.Details).ToArray()
        Dim userLists = details.Select(Function(d) d.Users).ToArray()
        Dim users = details.Select(Function(d) d.Users.ToArray()).ToArray()
        Dim assertHeader = CaptureHeader(receipt)
        Return Sub()
                   assertHeader()
                   ReceiptReadTests.AssertSame(expected, receipt)
                   Equal(True, Object.ReferenceEquals(collection, receipt.Groups))
                   For index = 0 To groups.Length - 1
                       Equal(True, Object.ReferenceEquals(groups(index), receipt.Groups(index)))
                       Equal(True, Object.ReferenceEquals(detailLists(index), groups(index).Details))
                   Next
                   Dim current = receipt.Groups.SelectMany(Function(g) g.Details).ToArray()
                   For index = 0 To details.Length - 1
                       Equal(True, Object.ReferenceEquals(details(index), current(index)))
                       Equal(True, Object.ReferenceEquals(userLists(index), current(index).Users))
                       For userIndex = 0 To users(index).Length - 1
                           Equal(True, Object.ReferenceEquals(users(index)(userIndex), current(index).Users(userIndex)))
                       Next
                   Next
               End Sub
    End Function

    Private Function CaptureHeader(receipt As Receipt) As Action
        Dim header = New With {receipt.Seq, receipt.Date, receipt.ShopName, receipt.ShopLocation, receipt.TaxType,
            receipt.TaxRoundingMode, receipt.PaymentClassification, receipt.Tag, receipt.RevisionToken}
        Dim points = receipt.Points
        Dim pointRefs = points.ToArray()
        Return Sub()
                   Equal(header.Seq, receipt.Seq)
                   Equal(header.Date, receipt.Date)
                   Equal(header.ShopName, receipt.ShopName)
                   Equal(header.ShopLocation, receipt.ShopLocation)
                   Equal(header.TaxType, receipt.TaxType)
                   Equal(header.TaxRoundingMode, receipt.TaxRoundingMode)
                   Equal(True, Object.ReferenceEquals(header.PaymentClassification, receipt.PaymentClassification))
                   Equal(header.Tag, receipt.Tag)
                   Equal(header.RevisionToken, receipt.RevisionToken)
                   Equal(True, Object.ReferenceEquals(points, receipt.Points))
                   Equal(pointRefs.Length, receipt.Points.Count)
                   For index = 0 To pointRefs.Length - 1
                       Equal(True, Object.ReferenceEquals(pointRefs(index), receipt.Points(index)))
                   Next
               End Sub
    End Function

    Private Sub LegacyFailure(Of T As Exception)(fixture As DatabaseFixture, receipt As Receipt)
        Dim method = GetType(DataAccess).GetMethod("LoadReceiptGroupData", BindingFlags.Instance Or BindingFlags.NonPublic)
        Try
            ' 従来の内部処理を直接呼び、2グループが1グループだけになることを再現する。
            method.Invoke(fixture.Data, New Object() {receipt, -1})
        Catch ex As TargetInvocationException When GetType(T).IsInstanceOfType(ex.InnerException)
            Equal(1, receipt.Groups.Count, "Legacy reload must expose a partially replaced model")
            Return
        End Try
        Throw New Exception("Expected legacy child reload failure: " & GetType(T).Name)
    End Sub
End Module
