Imports System.Data.SQLite
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text

' 製品の読込を一時DBで計測する。SQL数・時間の固定しきい値は設けない。
Friend Module ReceiptReadMeasurements
    Private Const Samples As Integer = 5

    Friend Function Run(outputPath As String) As Integer
        Try
            Dim lines As New List(Of String) From {
                "os,framework,runtime,provider,sqlite,processors,reader,receipts_per_month,scope,receipts,groups,details,assignments,empty_cache_selects,warm_selects,warm_parent_selects,warm_group_selects,warm_detail_selects,warm_user_selects,warm_other_selects,samples,min_ms,median_ms,max_ms"}
            For Each monthly In {10, 100}
                For Each selectedMonth As Integer In {10, 0}
                    For Each readerName In {"GetReceipts", "LegacyPaymentReceipts", "GetPaymentReceipts"}
                        lines.Add(Measure(monthly, selectedMonth, readerName))
                    Next
                Next
            Next
            File.WriteAllLines(outputPath, lines, New UTF8Encoding(False))
            For Each line In lines
                Console.WriteLine("MEASUREMENT " & line)
            Next
            Return 0
        Catch ex As Exception
            Console.Error.WriteLine("Receipt read measurement failed: " & ex.ToString())
            Return 1
        End Try
    End Function

    Private Function Measure(monthly As Integer, month As Integer, readerName As String) As String
        Using fixture As New DatabaseFixture(True)
            Seed(fixture, monthly)
            Dim receiptCount = monthly * If(month = 0, 12, 1)
            Dim emptyCounts As Integer() = Nothing
            Dim warmCounts As Integer() = Nothing
            Dim elapsed As New List(Of Double)
            BalanceBoundaryTests.ReadOnlyCase(fixture,
                Sub()
                    ' 各ケースで新規DataAccessを使い、マスタキャッシュの有無を分ける。
                    emptyCounts = CountSelects(fixture, monthly, month, readerName)
                    warmCounts = CountSelects(fixture, monthly, month, readerName)
                    For sample = 1 To Samples
                        ' Trace・結果検証・DBスナップショットは時間計測に含めない。
                        Dim timer = Stopwatch.StartNew()
                        Dim receipts = ReadRows(fixture, month, readerName)
                        timer.Stop()
                        elapsed.Add(timer.Elapsed.TotalMilliseconds)
                        AssertLoaded(receipts, monthly, month, readerName)
                    Next
                End Sub)
            elapsed.Sort()
            Dim fields As New List(Of String) From {
                Environment.OSVersion.VersionString, ".NET Framework 4.8", Environment.Version.ToString(),
                GetType(SQLiteConnection).Assembly.GetName().Version.ToString(), SQLiteConnection.SQLiteVersion,
                Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture), readerName,
                monthly.ToString(CultureInfo.InvariantCulture), If(month = 0, "year", "month"),
                receiptCount.ToString(CultureInfo.InvariantCulture), (receiptCount * 2).ToString(CultureInfo.InvariantCulture),
                (receiptCount * 8).ToString(CultureInfo.InvariantCulture), (receiptCount * 16).ToString(CultureInfo.InvariantCulture),
                emptyCounts.Sum().ToString(CultureInfo.InvariantCulture), warmCounts.Sum().ToString(CultureInfo.InvariantCulture)}
            fields.AddRange(warmCounts.Select(Function(n) n.ToString(CultureInfo.InvariantCulture)))
            fields.Add(Samples.ToString(CultureInfo.InvariantCulture))
            fields.AddRange({elapsed.First(), elapsed(Samples \ 2), elapsed.Last()}.
                Select(Function(ms) ms.ToString("F3", CultureInfo.InvariantCulture)))
            Return String.Join(",", fields.Select(Function(value) """" & value.Replace("""", """""") & """"))
        End Using
    End Function

    Private Function CountSelects(fixture As DatabaseFixture, monthly As Integer, month As Integer, readerName As String) As Integer()
        Dim counts(4) As Integer
        Dim tables = {"t_receipts", "t_receipt_groups", "t_receipt_details", "t_receipt_detail_users"}
        Dim observe As SQLiteTraceEventHandler =
            Sub(sender, args)
                Dim sql = args.Statement.Trim().ToLowerInvariant()
                If Not sql.StartsWith("select ", StringComparison.Ordinal) Then Return
                Dim bucket = Array.FindIndex(tables, Function(table) sql.Contains(" from " & table & " "))
                counts(If(bucket < 0, 4, bucket)) += 1
            End Sub
        Dim receipts As List(Of Receipt)
        AddHandler fixture.ActualDataConnection.Trace, observe
        Try
            receipts = ReadRows(fixture, month, readerName)
        Finally
            RemoveHandler fixture.ActualDataConnection.Trace, observe
        End Try
        AssertLoaded(receipts, monthly, month, readerName)
        If counts(0) = 0 Then Throw New InvalidOperationException("The product SELECT trace was not observed.")
        Return counts
    End Function

    Private Function ReadRows(fixture As DatabaseFixture, month As Integer, readerName As String) As List(Of Receipt)
        If readerName = "GetReceipts" Then Return fixture.Data.GetReceipts(2026, month)
        Dim dateFrom As New Date(2026, If(month = 0, 1, month), 1)
        Dim dateTo = If(month = 0, New Date(2027, 1, 1), New Date(2026, month, 1).AddMonths(1))
        ' コードだけの引数で、引数の準備によるマスタキャッシュの先読みを避ける。
        Dim payment As New PaymentClassification With {.Code = 1}
        If readerName = "LegacyPaymentReceipts" Then
            Return PaymentReceiptReadTests.ReadLegacy(fixture, dateFrom, dateTo, payment, endExclusive:=True)
        End If
        Return fixture.Data.GetPaymentReceipts(dateFrom, dateTo, payment, endExclusive:=True)
    End Function

    Private Sub AssertLoaded(receipts As List(Of Receipt), monthly As Integer, month As Integer, readerName As String)
        Dim expected As IEnumerable(Of Integer) = Enumerable.Range(1, monthly * 12).
            Where(Function(seq) month = 0 OrElse ((seq - 1) \ monthly) + 1 = month)
        If readerName = "GetReceipts" Then
            expected = expected.OrderByDescending(Function(seq) ReceiptDate(seq, monthly)).ThenByDescending(Function(seq) seq)
        End If
        Equal(String.Join(",", expected), String.Join(",", receipts.Select(Function(r) r.Seq)), "Receipt membership and order")
        For Each receipt In receipts
            Equal(ReceiptDate(receipt.Seq, monthly), receipt.Date)
            Equal(1, receipt.PaymentClassification.Code)
            Equal(1, receipt.TaxType)
            Equal(If(receipt.Seq Mod 2 = 0, "half_up", "floor"), receipt.TaxRoundingMode)
            Equal(If(receipt.Seq Mod 2 = 0, 889, 888), receipt.Total, "808 plus tax aggregated across both groups")
            Equal(2, receipt.Groups.Count)
            For groupIndex = 0 To 1
                Dim group = receipt.Groups(groupIndex)
                Equal(receipt.Seq, group.ReceiptSeq)
                Equal(groupIndex + 1, group.Seq)
                Equal(groupIndex + 1, group.GroupClassification.Code)
                Equal(4, group.Details.Count)
                For detailIndex = 0 To 3
                    Dim detail = group.Details(detailIndex)
                    Equal(receipt.Seq, detail.ReceiptSeq)
                    Equal(group.Seq, detail.GroupSeq)
                    Equal(detailIndex + 1, detail.Seq)
                    Equal(101, detail.Fee)
                    Equal(1, detail.Quantity)
                    Equal(0, detail.DiscountFee)
                    Equal(0D, detail.DiscountRate)
                    Equal(True, detail.TaxRate.IsTaxable)
                    Equal("1,2", String.Join(",", detail.Users.Select(Function(u) u.Code)))
                Next
            Next
        Next
    End Sub

    Private Function ReceiptDate(seq As Integer, monthly As Integer) As Date
        Return New Date(2026, ((seq - 1) \ monthly) + 1, ((seq - 1) Mod monthly) Mod 28 + 1, 12, 0, 0)
    End Function

    Private Sub Seed(fixture As DatabaseFixture, monthly As Integer)
        ' 実データや製品の保存APIを使わず、キャッシュが空のまま固定データを作る。
        Using connection As New SQLiteConnection(fixture.ConnectionString)
            connection.Open()
            Using transaction = connection.BeginTransaction()
                Using command = connection.CreateCommand()
                    command.Transaction = transaction
                    command.CommandText =
                        "DELETE FROM t_receipt_detail_users; DELETE FROM t_receipt_details; " &
                        "DELETE FROM t_receipt_groups; DELETE FROM t_receipts; " &
                        "INSERT INTO m_user(code,name,account_name,seq) VALUES(2,'synthetic user','synthetic-account',2); " &
                        "INSERT INTO m_group_classification(code,name,seq,default_tax_category_code) VALUES(2,'synthetic group',2,1); " &
                        "WITH RECURSIVE months(m) AS (VALUES(1) UNION ALL SELECT m+1 FROM months WHERE m<12), " &
                        "items(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM items WHERE n<@monthly) " &
                        "INSERT INTO t_receipts(seq,date,shop_name,shop_location,tax_type,payment_classification_code,tag,update_user,update_datetime,total,tax_rounding_mode) " &
                        "SELECT (m-1)*@monthly+n,printf('2026-%02d-%02d 12:00:00',m,(n-1)%28+1),'synthetic shop','',1,1,'','synthetic','2026-01-01', " &
                        "CASE WHEN ((m-1)*@monthly+n)%2=0 THEN 889 ELSE 888 END, " &
                        "CASE WHEN ((m-1)*@monthly+n)%2=0 THEN 'half_up' ELSE 'floor' END FROM months CROSS JOIN items; " &
                        "INSERT INTO t_receipt_groups(receipt_seq,seq,group_classification_code,tag) " &
                        "SELECT r.seq,g.n,g.n,'' FROM t_receipts r CROSS JOIN (SELECT 1 n UNION ALL SELECT 2) g; " &
                        "INSERT INTO t_receipt_details(receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_code,tag) " &
                        "SELECT g.receipt_seq,g.seq,d.n,'synthetic item',101,1,0,'0',5,'' FROM t_receipt_groups g " &
                        "CROSS JOIN (SELECT 1 n UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4) d; " &
                        "INSERT INTO t_receipt_detail_users(receipt_seq,group_seq,detail_seq,user_code) " &
                        "SELECT d.receipt_seq,d.group_seq,d.seq,u.code FROM t_receipt_details d CROSS JOIN m_user u;"
                    command.Parameters.AddWithValue("monthly", monthly)
                    command.ExecuteNonQuery()
                End Using
                transaction.Commit()
            End Using
        End Using
    End Sub
End Module
