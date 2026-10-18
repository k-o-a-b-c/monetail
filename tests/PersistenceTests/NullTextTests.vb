Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

Friend Module NullTextTests
    Private ReadOnly TextValues As String() = {Nothing, String.Empty, "plain text", "場所・タグ '引用'", " " & vbTab & "　 "}

    Public Sub HeaderText()
        Dim failures As New List(Of String)
        Using fixture As New DatabaseFixture
            ReportNullProviderBehavior(fixture)
        End Using
        For Each field In New String() {"location", "receipt tag"}
            For Each mode In New String() {"floor", "half_up"}
                For Each value In TextValues
                    Using fixture As New DatabaseFixture
                        SeedAmounts(fixture, mode)
                        SetText(fixture, field, value)
                        For Each paymentPath In New Boolean() {False, True}
                            RunCase(Context(field, value, mode, paymentPath), failures,
                                Sub()
                                    Dim before = ReceiptRows(fixture)
                                    Try
                                        AssertReceipt(ReadReceipt(fixture, paymentPath), mode, field, value)
                                    Finally
                                        Equal(before, ReceiptRows(fixture), "Reading must leave SQL NULLs and every saved receipt row unchanged")
                                    End Try
                                End Sub)
                        Next
                        RunCase("location history " & Context(field, value, mode, False), failures,
                            Sub()
                                Dim locations = fixture.Data.GetShopLocationList("history shop")
                                Dim expected = If(field = "location", value, String.Empty)
                                Equal(If(String.IsNullOrEmpty(expected), 0, 1), locations.Count)
                                If locations.Count > 0 Then Equal(expected, locations.Single())
                            End Sub)
                    End Using
                Next
            Next
        Next
        ReportFailures(failures)
    End Sub

    Public Sub ChildText()
        Dim failures As New List(Of String)
        For Each field In New String() {"group tag", "detail tag"}
            For Each mode In New String() {"floor", "half_up"}
                For Each value In TextValues
                    Using fixture As New DatabaseFixture
                        SeedAmounts(fixture, mode)
                        SetText(fixture, field, value)
                        For Each paymentPath In New Boolean() {False, True}
                            RunCase(Context(field, value, mode, paymentPath), failures,
                                Sub()
                                    Dim before = ReceiptRows(fixture)
                                    Try
                                        Dim receipt = ReadReceipt(fixture, paymentPath)
                                        AssertReceipt(receipt, mode, field, value)
                                        receipt.Groups(0).Tag = "unsaved group tag"
                                        receipt.Groups(0).Details(0).Tag = "unsaved detail tag"
                                        receipt.Groups(0).Details(0).Users.Clear()
                                        fixture.Data.LoadReceiptChildData(receipt)
                                        AssertReceipt(receipt, mode, field, value)
                                    Finally
                                        Equal(before, ReceiptRows(fixture), "Reading and child reload must leave every saved row unchanged")
                                    End Try
                                End Sub)
                        Next
                    End Using
                Next
            Next
        Next
        ReportFailures(failures)
    End Sub

    Public Sub OpenAndCancel()
        Dim failures As New List(Of String)
        For Each mode In New String() {"floor", "half_up"}
            RunCase("open/cancel all SQL NULL text " & mode, failures,
                Sub()
                    Using fixture As New DatabaseFixture
                        SeedAmounts(fixture, mode)
                        fixture.Execute("UPDATE t_receipts SET shop_location=NULL,tag=NULL WHERE seq=1;" &
                            "UPDATE t_receipt_groups SET tag=NULL WHERE receipt_seq=1;" &
                            "UPDATE t_receipt_details SET tag=NULL WHERE receipt_seq=1;")
                        Dim before = ReceiptRows(fixture)
                        Try
                            Dim receipt = ReadReceipt(fixture, False)
                            AssertReceipt(receipt, mode, "all", Nothing)
                            ' Reopen the same shared receipt; a fresh read must not mask a cancel bug.
                            For openNumber As Integer = 1 To 2
                                Using form As New RegisterForm With {.DataAccess = fixture.Data, .Receipt = receipt}
                                    form.Show()
                                    Application.DoEvents()
                                    Equal(True, Object.ReferenceEquals(receipt, form.Receipt))
                                    Equal(String.Empty, form.ShopLocationComboBox.Text)
                                    Equal(String.Empty, form.ReceiptTagTextBox.Text)
                                    Equal(True, form.TaxExclusiveRadioButton.Checked)
                                    Equal(mode = "floor", form.TaxRoundingFloorRadioButton.Checked)
                                    Equal(mode = "half_up", form.TaxRoundingHalfUpRadioButton.Checked)
                                    Equal(ExpectedTotal(mode), Convert.ToInt32(form.TotalTextBox.Text))
                                    Equal(ExpectedTotal(mode), Convert.ToInt32(form.TotalTextBox2.Text))
                                    Equal(2, form.GroupListView.Items.Count)
                                    Equal(String.Empty, form.GroupListView.Items(0).SubItems(2).Text)
                                    form.GroupListView.Items(0).Selected = True
                                    Application.DoEvents()
                                    Equal(1, form.DetailListView.Items.Count)
                                    Equal(String.Empty, form.DetailListView.Items(0).SubItems(2).Text)
                                    form.ShopLocationComboBox.Text = "unsaved location"
                                    form.ReceiptTagTextBox.Text = "unsaved tag"
                                    form.Close()
                                    Equal(False, form.DialogResult = DialogResult.OK)
                                End Using
                                AssertReceipt(receipt, mode, "all", Nothing)
                                Equal(before, ReceiptRows(fixture), "Cancelling must not save normalized text or screen edits")
                            Next
                            AssertReceipt(ReadReceipt(fixture, True), mode, "all", Nothing)
                        Finally
                            Equal(before, ReceiptRows(fixture), "Opening, cancelling and reopening must preserve SQL NULLs and saved rows")
                        End Try
                    End Using
                End Sub)
        Next
        ReportFailures(failures)
    End Sub

    Private Sub SeedAmounts(fixture As DatabaseFixture, mode As String)
        fixture.Execute("INSERT INTO t_receipt_details(receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_target,tag) " &
            "VALUES(1,2,2,'refund',-7,1,0,0,0,'');" &
            "INSERT INTO t_receipt_detail_users(receipt_seq,group_seq,detail_seq,user_code) VALUES(1,2,2,1);" &
            "UPDATE t_receipts SET tax_rounding_mode='" & mode & "',total=" & ExpectedTotal(mode).ToString(CultureInfo.InvariantCulture) & " WHERE seq=1;")
    End Sub

    Private Sub SetText(fixture As DatabaseFixture, field As String, value As String)
        Dim sql As String
        Select Case field
            Case "location" : sql = "UPDATE t_receipts SET shop_location=@value WHERE seq=1"
            Case "receipt tag" : sql = "UPDATE t_receipts SET tag=@value WHERE seq=1"
            Case "group tag" : sql = "UPDATE t_receipt_groups SET tag=@value WHERE receipt_seq=1 AND seq=1"
            Case "detail tag" : sql = "UPDATE t_receipt_details SET tag=@value WHERE receipt_seq=1 AND group_seq=1 AND seq=1"
            Case Else : Throw New ArgumentException("Unknown nullable field: " & field)
        End Select
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = sql
            command.Parameters.AddWithValue("value", If(value Is Nothing, DirectCast(DBNull.Value, Object), value))
            command.ExecuteNonQuery()
        End Using
    End Sub

    Private Function ReadReceipt(fixture As DatabaseFixture, paymentPath As Boolean) As Receipt
        If paymentPath Then
            Return fixture.Data.GetPaymentReceipts(New Date(2026, 10, 1), New Date(2026, 11, 1), fixture.Data.PaymentClassificationList.Single()).Single()
        End If
        Return fixture.Data.GetReceipts(2026, 10).Single()
    End Function

    Private Sub AssertReceipt(receipt As Receipt, mode As String, field As String, value As String)
        Equal(1, receipt.Seq)
        Equal(New Date(2026, 10, 16, 12, 0, 0), receipt.Date)
        Equal("history shop", receipt.ShopName)
        Equal(1, receipt.PaymentClassification.Code)
        Equal(1, receipt.TaxType)
        Equal(mode, receipt.TaxRoundingMode)
        Equal(ExpectedTotal(mode), receipt.Total)
        Equal(-7, receipt.Incomes)
        Equal(If(mode = "half_up", 42, 41), receipt.Expenses)
        Equal(If(field = "location", If(value, String.Empty), String.Empty), receipt.ShopLocation, "ShopLocation")
        Equal(If(field = "receipt tag", If(value, String.Empty), String.Empty), receipt.Tag, "Receipt.Tag")
        Equal(2, receipt.Groups.Count)
        Equal(If(field = "group tag", If(value, String.Empty), String.Empty), receipt.Groups(0).Tag, "ReceiptGroup.Tag")
        Equal(String.Empty, receipt.Groups(1).Tag, "Neighbor group tag")
        Equal(1, receipt.Groups(0).Details.Count)
        Equal(2, receipt.Groups(1).Details.Count)
        Equal(If(field = "detail tag", If(value, String.Empty), String.Empty), receipt.Groups(0).Details(0).Tag, "ReceiptDetail.Tag")
        Equal(String.Empty, receipt.Groups(1).Details(0).Tag, "Neighbor detail tag")
        Equal(String.Empty, receipt.Groups(1).Details(1).Tag, "Refund detail tag")
        Equal("1", String.Join(",", receipt.Groups(0).Details(0).Users.Select(Function(user) user.Code)))
        Equal(0, receipt.Groups(1).Details(0).Users.Count)
        Equal("1", String.Join(",", receipt.Groups(1).Details(1).Users.Select(Function(user) user.Code)))
    End Sub

    Private Function ExpectedTotal(mode As String) As Integer
        Return If(mode = "half_up", 35, 34)
    End Function

    Private Sub ReportNullProviderBehavior(fixture As DatabaseFixture)
        Using command = fixture.ActualDataConnection.CreateCommand()
            command.CommandText = "SELECT CAST(NULL AS TEXT)"
            Using reader = command.ExecuteReader()
                reader.Read()
                Equal(True, reader.IsDBNull(0))
                Try
                    Dim value = reader.GetString(0)
                    Console.WriteLine("INFO System.Data.SQLite GetString(SQL NULL): " & If(value Is Nothing, "Nothing", "String length " & value.Length.ToString(CultureInfo.InvariantCulture)))
                Catch ex As Exception
                    Console.WriteLine("INFO System.Data.SQLite GetString(SQL NULL): " & ex.GetType().FullName & ": " & ex.Message)
                End Try
            End Using
        End Using
    End Sub

    ' Include every stored column, with an explicit NULL marker and length-prefixed values.
    Private Function ReceiptRows(fixture As DatabaseFixture) As String
        Dim result As New StringBuilder()
        For Each table In New String() {"t_receipts", "t_receipt_groups", "t_receipt_details", "t_receipt_detail_users"}
            result.AppendLine(table)
            Using command = fixture.ActualDataConnection.CreateCommand()
                command.CommandText = "SELECT * FROM " & table & " ORDER BY rowid"
                Using reader = command.ExecuteReader()
                    While reader.Read()
                        For column As Integer = 0 To reader.FieldCount - 1
                            If reader.IsDBNull(column) Then
                                result.Append("null;")
                            Else
                                Dim value = Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture)
                                result.Append(value.Length).Append(":"c).Append(value).Append(";"c)
                            End If
                        Next
                        result.AppendLine()
                    End While
                End Using
            End Using
        Next
        Return result.ToString()
    End Function

    Private Function Context(field As String, value As String, mode As String, paymentPath As Boolean) As String
        Return field & ", value=" & If(value Is Nothing, "SQL NULL", "[" & value & "]") & ", " & mode & ", " & If(paymentPath, "GetPaymentReceipts", "GetReceipts")
    End Function

    Private Sub RunCase(context As String, failures As List(Of String), action As Action)
        Try
            action()
        Catch ex As Exception
            failures.Add(context & ": " & ex.GetType().Name & ": " & ex.Message)
        End Try
    End Sub

    Private Sub ReportFailures(failures As List(Of String))
        If failures.Count > 0 Then Throw New Exception(String.Join(Environment.NewLine, failures))
    End Sub
End Module
