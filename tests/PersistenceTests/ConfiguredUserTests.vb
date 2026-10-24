Imports System.Configuration
Imports System.Linq

Friend Module ConfiguredUserTests
    Public Sub ConfigurationAndRecovery()
        EachMode(
            Sub(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        For Each value In {Nothing, "", " ", "2", " +2 ", "0", "-1"}
                            Dim messages As New List(Of String)
                            Using data = DatabaseStartup.TryOpenConfiguredDatabase(Function() fixture.ConnectionString,
                                    AddressOf messages.Add, Function() value)
                                Equal(False, data Is Nothing)
                                Equal(0, messages.Count)
                                Dim expected = If(String.IsNullOrWhiteSpace(value), Environment.UserName,
                                    If(value.Trim() = "0" OrElse value.Trim() = "-1", "boundary", "configured'account"))
                                Equal(expected, data.CurrentAccountName)
                            End Using
                        Next
                        For Each value In {"abc", "2.5", "2147483648", "99"}
                            Dim messages As New List(Of String)
                            Equal(True, DatabaseStartup.TryOpenConfiguredDatabase(Function() fixture.ConnectionString,
                                AddressOf messages.Add, Function() value) Is Nothing)
                            Equal(1, messages.Count)
                            Equal(True, messages(0).Contains("UserCode"))
                            Equal(True, messages(0).Contains(DatabaseStartup.ConfigurationFilePath))
                        Next
                        Dim errors As New List(Of String)
                        Dim badSetting As Func(Of String) = Function()
                                                               Throw New ConfigurationErrorsException("private-setting")
                                                           End Function
                        Equal(True, DatabaseStartup.TryOpenConfiguredDatabase(Function() fixture.ConnectionString,
                            AddressOf errors.Add, badSetting) Is Nothing)
                        Equal(1, errors.Count)
                        Equal(False, errors(0).Contains("private-setting"))
                        Using recovered = DatabaseStartup.TryOpenConfiguredDatabase(Function() fixture.ConnectionString,
                                AddressOf errors.Add, Function() "2")
                            Equal(False, recovered Is Nothing)
                            Equal("configured'account", recovered.CurrentAccountName)
                            Equal(1, errors.Count)
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Public Sub ScopeAndHistory()
        EachMode(
            Sub(fixture)
                BalanceBoundaryTests.ReadOnlyCase(fixture,
                    Sub()
                        Using second As New DataAccess(fixture.ConnectionString, 2), third As New DataAccess(fixture.ConnectionString, 3)
                            Equal("configured'account", second.CurrentAccountName)
                            Equal(second.CurrentAccountName, third.CurrentAccountName)
                            Equal(2, second.UserList.First().Code)
                            Equal(3, third.UserList.First().Code)
                            Equal(2, second.PaymentClassificationList.First().Code)
                            Equal("second cash", second.PaymentClassificationList.First().Name)
                            Equal("（third）third cash", second.PaymentClassification(3).Name)
                            Equal(3, third.PaymentClassificationList.First().Code)
                            Equal("third cash", third.PaymentClassificationList.First().Name)
                            Equal("（second）second cash", third.PaymentClassification(2).Name)
                            Equal(1, fixture.Data.UserList.First().Code)
                            Equal(1, fixture.Data.PaymentClassificationList.First().Code)
                            Equal(2, second.ShopList.Single(Function(s) s.Name = "configured shop").Code)
                            Equal(0, second.ShopList.Single(Function(s) s.Name = "other shop").Code)
                            Equal(3, third.ShopList.Single(Function(s) s.Name = "other shop").Code)
                            Equal(False, third.ShopList.Any(Function(s) s.Name = "configured shop"))
                            Equal(True, second.ShopList.Any(Function(s) s.Name = "common shop"))
                            Equal(True, third.ShopList.Any(Function(s) s.Name = "common shop"))
                            Equal(False, fixture.Data.ShopList.Any(Function(s) s.Name = "shared history"))
                            Equal(2, second.GetPaymentClassification("shared history"))
                            Equal(3, third.GetPaymentClassification("shared history"))
                            Equal(0, fixture.Data.GetPaymentClassification("shared history"))
                            Equal("half_up", second.GetTaxRoundingMode("shared history"))
                            Equal("floor", third.GetTaxRoundingMode("shared history"))
                            Equal(2, second.GetDefaultGroupClassification("shared history"))
                            Equal(3, third.GetDefaultGroupClassification("shared history"))
                            Equal(2, second.GetTransferFromPaymentClassification("history transfer"))
                            Equal(3, third.GetTransferFromPaymentClassification("history transfer"))
                            Equal(2, second.GetPaymentMemoList().First().PaymentClassification.Code)
                            Equal(3, third.GetPaymentMemoList().First().PaymentClassification.Code)
                            Equal(1, fixture.Data.GetPaymentMemoList().First().PaymentClassification.Code)
                            Equal(Environment.UserName, fixture.Data.CurrentAccountName)
                        End Using
                    End Sub)
            End Sub)
    End Sub

    Public Sub AuditWrites()
        EachMode(
            Sub(fixture)
                Using data As New DataAccess(fixture.ConnectionString, 2)
                    For Each kind In {ConcurrencyTests.EntityKind.Receipt, ConcurrencyTests.EntityKind.Transfer, ConcurrencyTests.EntityKind.Balance}
                        Dim item = ConcurrencyTests.MakeItem(kind, "configured", 1)
                        ConcurrencyTests.Save(data, kind, item, True)
                        Dim seq = ConcurrencyTests.Sequence(kind, item)
                        Dim query = "SELECT update_user FROM " & ConcurrencyTests.ParentTable(kind) & " WHERE seq=" & seq.ToString()
                        Equal("configured'account", CStr(fixture.Scalar(query)))
                        Dim untouched = ConcurrencyTests.Snapshot(fixture, kind, seq)
                        ConcurrencyTests.MakeItem(kind, "configured update", 2, item)
                        ConcurrencyTests.Save(data, kind, item, False)
                        Equal("configured'account", CStr(fixture.Scalar(query)))
                        Equal(untouched, ConcurrencyTests.Snapshot(fixture, kind, seq))
                        Dim legacy = ConcurrencyTests.MakeItem(kind, "legacy", 1)
                        ConcurrencyTests.Save(fixture.Data, kind, legacy, True)
                        Dim legacySeq = ConcurrencyTests.Sequence(kind, legacy)
                        Equal(Environment.UserName, CStr(fixture.Scalar("SELECT update_user FROM " &
                            ConcurrencyTests.ParentTable(kind) & " WHERE seq=" & legacySeq.ToString())))
                    Next
                End Using
            End Sub)
    End Sub

    Private Sub EachMode(action As Action(Of DatabaseFixture))
        For Each foreignKeys In {False, True}
            Using fixture As New DatabaseFixture(foreignKeys)
                fixture.Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES" &
                    "(2,'second','configured''account',2),(3,'third','configured''account',0),(0,'zero','boundary',3),(-1,'negative','boundary',4);" &
                    "UPDATE m_payment_classification SET seq=9 WHERE code=1;" &
                    "INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(2,'second cash',2,2),(3,'third cash',1,3);" &
                    "INSERT INTO m_shop(code,name,seq,user_code) VALUES(2,'configured shop',2,2),(3,'other shop',3,3),(4,'common shop',4,NULL);" &
                    "INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(2,'second group',2,1,0),(3,'third group',3,1,0);")
                fixture.AddReceipt(2, "shared history", "half_up", New Date(2026, 10, 20), 2)
                fixture.AddReceipt(3, "shared history", "floor", New Date(2026, 10, 21), 3)
                fixture.AddReceipt(4, "other shop", "floor", New Date(2026, 10, 22), 2)
                fixture.Execute("UPDATE t_receipt_groups SET group_classification_code=receipt_seq WHERE receipt_seq IN (2,3);" &
                    "INSERT INTO t_transfers(seq,date,name,fee,from_payment_classification_code,to_payment_classification_code,tag,update_user,update_datetime,revision_token) VALUES" &
                    "(1,'2026-10-20','history transfer',1,2,1,'','test','2026-10-20','first')," &
                    "(2,'2026-10-21','history transfer',1,3,1,'','test','2026-10-21','second');")
                action(fixture)
            End Using
        Next
    End Sub
End Module
