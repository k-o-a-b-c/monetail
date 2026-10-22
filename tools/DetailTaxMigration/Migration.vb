Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.Numerics

Public NotInheritable Class DetailTaxMigration
    Public Const MigrationId As String = "20261022_detail_tax_decimal"
    Private Shared ReadOnly Rebuilt As String() = {"m_tax_rate", "m_group_classification", "t_receipt_groups", "t_receipt_details", "t_receipt_detail_users"}
    Private Shared ReadOnly ExpectedColumns As String() = {
        "code,rate,start_date", "code,name,seq,default_tax_target,reduced_tax_target",
        "receipt_seq,seq,group_classification_code,tag,tax",
        "receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_target,tag",
        "receipt_seq,group_seq,detail_seq,user_code"}
    Private Shared ReadOnly ExpectedForeignKeys As String()() = {
        New String() {}, New String() {},
        New String() {"m_group_classification:group_classification_code>code", "t_receipts:receipt_seq>seq"},
        New String() {"t_receipt_groups:receipt_seq>receipt_seq,group_seq>seq"},
        New String() {"m_user:user_code>code", "t_receipt_details:receipt_seq>receipt_seq,group_seq>group_seq,detail_seq>seq"}}

    Private Sub New()
    End Sub

    ' 呼出元は閉じた元DBのコピーを渡す。アプリ起動時には呼び出さない。
    ' checkpointはテストの失敗注入用。どの段階の例外もDDLを含めて戻す。
    Public Shared Sub Run(connection As DbConnection, Optional checkpoint As Action(Of String) = Nothing)
        If connection.State <> ConnectionState.Open Then Throw New InvalidOperationException("開いたコピーDB接続が必要です。")
        Dim foreignKeys = CInt(Scalar(connection, "PRAGMA foreign_keys"))
        Execute(connection, "PRAGMA foreign_keys=OFF")
        If CInt(Scalar(connection, "PRAGMA foreign_keys")) <> 0 Then
            Throw New InvalidOperationException("トランザクション外の接続が必要です。")
        End If
        Dim begun = False
        Try
            Execute(connection, "BEGIN IMMEDIATE")
            begun = True
            ValidateSource(connection)
            Dim tables As New Dictionary(Of String, DataTable)
            For Each name In Rebuilt.Concat({"t_receipts"})
                tables.Add(name, ReadSource(connection, name))
            Next
            Dim indexes = Read(connection, "SELECT sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL AND tbl_name IN ('m_tax_rate','m_group_classification','t_receipt_groups','t_receipt_details','t_receipt_detail_users')")
            CreateTables(connection)
            If checkpoint IsNot Nothing Then checkpoint("created")

            Dim taxes = tables("m_tax_rate").Rows.Cast(Of DataRow)().ToDictionary(Function(r) Int(r, "code"), Function(r) Int(r, "rate"))
            ' 既存の0%を対象外へ転用しない。全コードを見て新しいコードを割り当てる。
            Dim excludedCode As Integer = 0
            While taxes.ContainsKey(excludedCode)
                excludedCode = checkedNext(excludedCode)
            End While
            For Each row As DataRow In tables("m_tax_rate").Rows
                Execute(connection, "INSERT INTO _dt_m_tax_rate VALUES(@p0,@p1,@p2,'taxable')", row("code"), row("rate"), row("start_date"))
            Next
            Execute(connection, "INSERT INTO _dt_m_tax_rate VALUES(@p0,0,NULL,'out_of_scope')", excludedCode)
            Execute(connection, "INSERT INTO m_tax_category VALUES(1,'通常'),(2,'既存軽減'),(3,'対象外')")
            For Each row As DataRow In tables("m_group_classification").Rows
                Dim category = If(Int(row, "default_tax_target") = 0, 3, If(Int(row, "reduced_tax_target") = 0, 1, 2))
                Execute(connection, "INSERT INTO _dt_m_group_classification VALUES(@p0,@p1,@p2,@p3)", row("code"), row("name"), row("seq"), category)
            Next

            Dim groupMap As New Dictionary(Of String, Integer)
            Dim nextDetail As New Dictionary(Of String, Integer)
            For Each batch In tables("t_receipt_groups").Rows.Cast(Of DataRow)().GroupBy(Function(r) Key(Int(r, "receipt_seq"), Int(r, "group_classification_code")))
                Dim ordered = batch.OrderBy(Function(r) Int(r, "seq")).ToList()
                Dim first = ordered(0)
                Dim receipt = Int(first, "receipt_seq")
                Dim target = Int(first, "seq")
                Dim note As Object = first("tag")
                If ordered.Count > 1 Then
                    Dim notes = ordered.Where(Function(r) Not r.IsNull("tag") AndAlso CStr(r("tag")) <> "").Select(Function(r) CStr(r("tag"))).ToList()
                    note = If(notes.Count > 0, CObj(String.Join(vbLf, notes.Select(Function(s, i) "【統合元 " & (i + 1).ToString(CultureInfo.InvariantCulture) & "】" & vbLf & s))),
                              If(ordered.All(Function(r) r.IsNull("tag")), CObj(DBNull.Value), CObj("")))
                End If
                Execute(connection, "INSERT INTO _dt_t_receipt_groups VALUES(@p0,@p1,@p2,@p3)", receipt, target, first("group_classification_code"), note)
                nextDetail.Add(Key(receipt, target), 0)
                For Each row In ordered
                    groupMap.Add(Key(receipt, Int(row, "seq")), target)
                    Execute(connection, "INSERT INTO detail_tax_group_map VALUES(@p0,@p1,@p2,@p3,@p4)", receipt, row("seq"), target, row("tag"), row("tax"))
                Next
            Next

            Dim oldGroups = tables("t_receipt_groups").Rows.Cast(Of DataRow)().ToDictionary(Function(r) Key(Int(r, "receipt_seq"), Int(r, "seq")))
            Dim detailMap As New Dictionary(Of String, Tuple(Of Integer, Integer))
            For Each row In tables("t_receipt_details").Rows.Cast(Of DataRow)().OrderBy(Function(r) Int(r, "receipt_seq")).ThenBy(Function(r) Int(r, "group_seq")).ThenBy(Function(r) Int(r, "seq"))
                Dim receipt = Int(row, "receipt_seq")
                Dim oldGroupKey = Key(receipt, Int(row, "group_seq"))
                Dim target = groupMap(oldGroupKey)
                Dim newGroupKey = Key(receipt, target)
                Dim newSeq = checkedNext(nextDetail(newGroupKey))
                nextDetail(newGroupKey) = newSeq
                Dim oldTax = Int(oldGroups(oldGroupKey), "tax")
                Dim tax = If(Int(row, "tax_target") = 0, excludedCode, oldTax)
                If Not taxes.ContainsKey(oldTax) Then Throw New InvalidOperationException("親グループの税率コードがありません: " & oldGroupKey)
                Dim rate = ReadRate(row)
                Execute(connection, "INSERT INTO _dt_t_receipt_details VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                        receipt, target, newSeq, row("name"), row("fee"), row("quantity"), row("discount_fee"), ExactMoney.RateText(rate), tax, row("tag"))
                detailMap.Add(Key(receipt, Int(row, "group_seq"), Int(row, "seq")), Tuple.Create(target, newSeq))
                Execute(connection, "INSERT INTO detail_tax_detail_map VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", receipt, row("group_seq"), row("seq"), target, newSeq, Int(row, "tax_target"))
            Next
            For Each row As DataRow In tables("t_receipt_detail_users").Rows
                Dim target = detailMap(Key(Int(row, "receipt_seq"), Int(row, "group_seq"), Int(row, "detail_seq")))
                Execute(connection, "INSERT INTO _dt_t_receipt_detail_users VALUES(@p0,@p1,@p2,@p3)", row("receipt_seq"), target.Item1, target.Item2, row("user_code"))
            Next
            If checkpoint IsNot Nothing Then checkpoint("copied")

            ' FKは元の最終テーブル名を参照する。子から削除し、親から最終名にする。
            For Each name In Rebuilt.Reverse()
                Execute(connection, "DROP TABLE " & Quote(name))
            Next
            For Each name In Rebuilt
                Execute(connection, "ALTER TABLE " & Quote("_dt_" & name) & " RENAME TO " & Quote(name))
            Next
            For Each row As DataRow In indexes.Rows
                Execute(connection, CStr(row("sql")))
            Next
            If checkpoint IsNot Nothing Then checkpoint("replaced")

            Dim linesByReceipt = tables("t_receipt_details").Rows.Cast(Of DataRow)().ToLookup(Function(r) Int(r, "receipt_seq"))
            For Each header As DataRow In tables("t_receipts").Rows
                Dim receipt = Int(header, "seq")
                If Not header.IsNull("total") Then Int(header, "total")
                Dim type = Int(header, "tax_type")
                Dim mode = CStr(header("tax_rounding_mode"))
                Dim oldLines = linesByReceipt(receipt).ToList()
                Dim expected = New Integer(2) {}
                Dim oldAmounts = New Integer(2) {}
                Dim migrated = Read(connection, "SELECT d.*,t.rate,t.tax_treatment FROM t_receipt_details d JOIN m_tax_rate t ON t.code=d.tax_code WHERE receipt_seq=@p0 ORDER BY group_seq,seq", receipt)
                For i = 0 To 2
                    Dim sign = If(i = 0, 0, If(i = 1, -1, 1))
                    expected(i) = ExpectedTotal(oldLines, oldGroups, taxes, type, mode, sign)
                    Dim actual = MigratedTotal(migrated, type, mode, sign)
                    If expected(i) <> actual Then Throw New InvalidOperationException("移行後の金額が期待値と一致しません: " & receipt.ToString(CultureInfo.InvariantCulture))
                    oldAmounts(i) = LegacyTotal(oldLines, oldGroups, taxes, type, mode, sign, receipt)
                Next
                Execute(connection, "INSERT INTO detail_tax_receipt_audit VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)",
                        receipt, header("total"), oldAmounts(0), expected(0), oldAmounts(1), expected(1), oldAmounts(2), expected(2), header("revision_token"), Guid.NewGuid().ToString("N"))
                Execute(connection, "UPDATE t_receipts SET total=@p0,revision_token=(SELECT new_revision FROM detail_tax_receipt_audit WHERE receipt_seq=@p1) WHERE seq=@p1", expected(0), receipt)
            Next
            If Read(connection, "PRAGMA foreign_key_check").Rows.Count <> 0 Then Throw New InvalidOperationException("移行後の外部キー違反です。")
            If CStr(Scalar(connection, "PRAGMA integrity_check")) <> "ok" Then Throw New InvalidOperationException("移行後の整合性検査に失敗しました。")
            If CLng(Scalar(connection, "SELECT COUNT(*) FROM t_receipt_details")) <> tables("t_receipt_details").Rows.Count OrElse
               CLng(Scalar(connection, "SELECT COUNT(*) FROM t_receipt_detail_users")) <> tables("t_receipt_detail_users").Rows.Count Then
                Throw New InvalidOperationException("明細または対象者の件数が一致しません。")
            End If
            Execute(connection, "INSERT INTO detail_tax_migration VALUES(@p0,@p1)", MigrationId, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
            If checkpoint IsNot Nothing Then checkpoint("validated")
            Execute(connection, "COMMIT")
            begun = False
        Catch
            If begun Then Execute(connection, "ROLLBACK")
            Throw
        Finally
            ' SQLiteではトランザクション内のPRAGMA変更は無効。終了後に必ず復元する。
            Execute(connection, "PRAGMA foreign_keys=" & foreignKeys.ToString(CultureInfo.InvariantCulture))
        End Try
    End Sub

    Private Shared Sub ValidateSource(connection As DbConnection)
        If CLng(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name GLOB '_dt_*' OR name GLOB 'detail_tax_*' OR name IN ('m_tax_category','m_tax_category_rule')")) <> 0 Then
            Throw New InvalidOperationException("移行済み、または移行用テーブル名が使用されています。")
        End If
        ' 再構築対象の未知の列やトリガー・ビューを黙って失わない。
        If CLng(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view')")) <> 0 Then
            Throw New InvalidOperationException("独自トリガー／ビューがあるDBは事前確認が必要です。")
        End If
        For i = 0 To Rebuilt.Length - 1
            Dim columns = Read(connection, "PRAGMA table_info(" & Quote(Rebuilt(i)) & ")").Rows.Cast(Of DataRow)().Select(Function(r) CStr(r("name")))
            If Not columns.SequenceEqual(ExpectedColumns(i).Split(","c)) Then Throw New InvalidOperationException("未対応の列構成です: " & Rebuilt(i))
            Dim ddl = CStr(Scalar(connection, "SELECT sql FROM sqlite_master WHERE type='table' AND name='" & Rebuilt(i) & "'"))
            If System.Text.RegularExpressions.Regex.IsMatch(ddl, "\b(CHECK|UNIQUE|COLLATE|DEFERRABLE|AUTOINCREMENT|GENERATED)\b|\bON\s+(UPDATE|DELETE|CONFLICT)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) Then
                Throw New InvalidOperationException("独自の制約がある表は事前確認が必要です: " & Rebuilt(i))
            End If
            Dim keys = Read(connection, "PRAGMA foreign_key_list(" & Quote(Rebuilt(i)) & ")").Rows.Cast(Of DataRow)().GroupBy(Function(r) CInt(r("id"))).Select(
                Function(batch) CStr(batch.First()("table")) & ":" & String.Join(",", batch.OrderBy(Function(r) CInt(r("seq"))).Select(Function(r) CStr(r("from")) & ">" & CStr(r("to")))))
            If Not keys.OrderBy(Function(s) s, StringComparer.Ordinal).SequenceEqual(ExpectedForeignKeys(i).OrderBy(Function(s) s, StringComparer.Ordinal)) Then
                Throw New InvalidOperationException("未対応の外部キー構成です: " & Rebuilt(i))
            End If
        Next
        Dim headers = Read(connection, "PRAGMA table_info(t_receipts)").Rows.Cast(Of DataRow)().Select(Function(r) CStr(r("name"))).ToList()
        If Not {"tax_rounding_mode", "revision_token", "total"}.All(Function(s) headers.Contains(s)) Then Throw New InvalidOperationException("税端数・更新版の既存移行が必要です。")
        For Each name In {"m_group_classification.default_tax_target", "m_group_classification.reduced_tax_target", "t_receipt_details.tax_target"}
            Dim parts = name.Split("."c)
            If CLng(Scalar(connection, "SELECT COUNT(*) FROM " & Quote(parts(0)) & " WHERE " & Quote(parts(1)) & " NOT IN (0,1) OR " & Quote(parts(1)) & " IS NULL")) <> 0 Then Throw New InvalidOperationException("不正な課税フラグです: " & name)
        Next
        If Read(connection, "PRAGMA foreign_key_check").Rows.Count <> 0 Then Throw New InvalidOperationException("元DBに外部キー違反があります。")
        For Each table As DataRow In Read(connection, "SELECT name FROM sqlite_master WHERE type='table'").Rows
            Dim name = CStr(table("name"))
            For Each foreignKey As DataRow In Read(connection, "PRAGMA foreign_key_list(" & Quote(name) & ")").Rows
                If {"t_receipt_groups", "t_receipt_details"}.Contains(CStr(foreignKey("table"))) AndAlso
                   Not {"t_receipt_details", "t_receipt_detail_users"}.Contains(name) Then
                    Throw New InvalidOperationException("未知の明細／グループ参照があります: " & name)
                End If
            Next
        Next
        If CLng(Scalar(connection, "SELECT COUNT(*) FROM t_receipt_groups g LEFT JOIN m_tax_rate t ON t.code=g.tax WHERE t.code IS NULL")) <> 0 Then Throw New InvalidOperationException("元DBの税率コードがありません。")
    End Sub

    Private Shared Sub CreateTables(connection As DbConnection)
        Execute(connection,
            "CREATE TABLE m_tax_category(code INTEGER NOT NULL PRIMARY KEY,name TEXT NOT NULL);" &
            "CREATE TABLE m_tax_category_rule(tax_category_code INTEGER NOT NULL REFERENCES m_tax_category(code),start_date TEXT NOT NULL,tax_code INTEGER NOT NULL REFERENCES m_tax_rate(code),PRIMARY KEY(tax_category_code,start_date),CHECK(length(start_date)=10 AND date(start_date,'+0 days') IS NOT NULL AND date(start_date,'+0 days')=start_date));" &
            "CREATE TABLE _dt_m_tax_rate(code INTEGER NOT NULL PRIMARY KEY,rate INTEGER NOT NULL,start_date DATE,tax_treatment TEXT NOT NULL CHECK(tax_treatment IN ('taxable','out_of_scope')),CHECK(tax_treatment<>'out_of_scope' OR rate=0));" &
            "CREATE TABLE _dt_m_group_classification(code INTEGER NOT NULL PRIMARY KEY,name TEXT NOT NULL,seq INTEGER,default_tax_category_code INTEGER NOT NULL REFERENCES m_tax_category(code));" &
            "CREATE TABLE _dt_t_receipt_groups(receipt_seq INTEGER NOT NULL REFERENCES t_receipts(seq),seq INTEGER NOT NULL,group_classification_code INTEGER NOT NULL REFERENCES m_group_classification(code),tag TEXT,PRIMARY KEY(receipt_seq,seq),UNIQUE(receipt_seq,group_classification_code));" &
            "CREATE TABLE _dt_t_receipt_details(receipt_seq INTEGER NOT NULL,group_seq INTEGER NOT NULL,seq INTEGER NOT NULL,name TEXT NOT NULL,fee INTEGER NOT NULL,quantity INTEGER NOT NULL,discount_fee INTEGER NOT NULL,discount_rate TEXT NOT NULL,tax_code INTEGER NOT NULL REFERENCES m_tax_rate(code),tag TEXT,PRIMARY KEY(receipt_seq,group_seq,seq),FOREIGN KEY(receipt_seq,group_seq) REFERENCES t_receipt_groups(receipt_seq,seq));" &
            "CREATE TABLE _dt_t_receipt_detail_users(receipt_seq INTEGER NOT NULL,group_seq INTEGER NOT NULL,detail_seq INTEGER NOT NULL,user_code INTEGER NOT NULL REFERENCES m_user(code),PRIMARY KEY(receipt_seq,group_seq,detail_seq,user_code),FOREIGN KEY(receipt_seq,group_seq,detail_seq) REFERENCES t_receipt_details(receipt_seq,group_seq,seq));" &
            "CREATE TABLE detail_tax_group_map(receipt_seq INTEGER NOT NULL,old_seq INTEGER NOT NULL,new_seq INTEGER NOT NULL,old_tag TEXT,old_tax INTEGER NOT NULL,PRIMARY KEY(receipt_seq,old_seq));" &
            "CREATE TABLE detail_tax_detail_map(receipt_seq INTEGER NOT NULL,old_group_seq INTEGER NOT NULL,old_seq INTEGER NOT NULL,new_group_seq INTEGER NOT NULL,new_seq INTEGER NOT NULL,old_tax_target INTEGER NOT NULL,PRIMARY KEY(receipt_seq,old_group_seq,old_seq));" &
            "CREATE TABLE detail_tax_receipt_audit(receipt_seq INTEGER NOT NULL PRIMARY KEY,stored_total INTEGER,legacy_total INTEGER NOT NULL,new_total INTEGER NOT NULL,legacy_incomes INTEGER NOT NULL,new_incomes INTEGER NOT NULL,legacy_expenses INTEGER NOT NULL,new_expenses INTEGER NOT NULL,old_revision TEXT NOT NULL,new_revision TEXT NOT NULL);" &
            "CREATE TABLE detail_tax_migration(id TEXT NOT NULL PRIMARY KEY,completed_utc TEXT NOT NULL);")
    End Sub

    Private Shared Function ExpectedTotal(lines As List(Of DataRow), groups As Dictionary(Of String, DataRow), taxes As Dictionary(Of Integer, Integer), type As Integer, mode As String, sign As Integer) As Integer
        Dim totals As New Dictionary(Of Integer, BigInteger)
        Dim excluded = BigInteger.Zero
        For Each line In lines
            Dim amount = ExactMoney.Net(Int(line, "fee"), Int(line, "discount_fee"), Int(line, "quantity"), ReadRate(line))
            If sign <> 0 AndAlso amount.Sign <> sign Then Continue For
            If Int(line, "tax_target") = 0 Then
                excluded += amount
            Else
                Dim rate = taxes(Int(groups(Key(Int(line, "receipt_seq"), Int(line, "group_seq"))), "tax"))
                If Not totals.ContainsKey(rate) Then totals.Add(rate, BigInteger.Zero)
                totals(rate) += amount
            End If
        Next
        For Each pair In totals
            excluded += ExactMoney.Taxed(pair.Value, pair.Key, type, mode)
        Next
        ' 空レシートでも税区分と端数方式を検証する。
        ExactMoney.Taxed(BigInteger.Zero, 0, type, mode)
        Return CInt(excluded)
    End Function

    Private Shared Function MigratedTotal(lines As DataTable, type As Integer, mode As String, sign As Integer) As Integer
        Dim excluded = BigInteger.Zero
        Dim taxable As New Dictionary(Of Integer, BigInteger)
        For Each line As DataRow In lines.Rows
            Dim amount = ExactMoney.Net(Int(line, "fee"), Int(line, "discount_fee"), Int(line, "quantity"), ExactMoney.ParseRate(CStr(line("discount_rate"))))
            If sign <> 0 AndAlso amount.Sign <> sign Then Continue For
            If CStr(line("tax_treatment")) = "out_of_scope" Then
                excluded += amount
            Else
                Dim rate = Int(line, "rate")
                If Not taxable.ContainsKey(rate) Then taxable.Add(rate, BigInteger.Zero)
                taxable(rate) += amount
            End If
        Next
        For Each pair In taxable
            excluded += ExactMoney.Taxed(pair.Value, pair.Key, type, mode)
        Next
        Return CInt(excluded)
    End Function

    ' 移行前の製品式の記録専用。新しい金額の計算経路へは使用しない。
    Private Shared Function LegacyTotal(lines As List(Of DataRow), groups As Dictionary(Of String, DataRow), taxes As Dictionary(Of Integer, Integer), type As Integer, mode As String, sign As Integer, receipt As Integer) As Integer
        Dim taxable As New Dictionary(Of Integer, Double)
        Dim excluded As New Dictionary(Of Integer, Double)
        Dim grouped = lines.ToLookup(Function(r) Int(r, "group_seq"))
        For Each group As DataRow In groups.Values.Where(Function(r) Int(r, "receipt_seq") = receipt).OrderBy(Function(r) Int(r, "seq"))
            Dim taxFee As Double = 0
            Dim nonTaxFee As Double = 0
            ' 旧製品と同じくグループ内で合算してから税率別へ加える。
            For Each line In grouped(Int(group, "seq")).OrderBy(Function(r) Int(r, "seq"))
                Dim amount As Double = CDbl(line("fee"))
                amount -= CDbl(line("discount_fee"))
                amount *= CDbl(line("quantity"))
                amount -= Math.Ceiling(amount * CDbl(line("discount_rate")) / 100)
                If (sign < 0 AndAlso amount >= 0) OrElse (sign > 0 AndAlso amount <= 0) Then Continue For
                If Int(line, "tax_target") = 0 Then
                    nonTaxFee += amount
                Else
                    taxFee += amount
                End If
            Next
            Dim rate = taxes(Int(group, "tax"))
            If Not taxable.ContainsKey(rate) Then
                taxable.Add(rate, 0)
                excluded.Add(rate, 0)
            End If
            excluded(rate) += nonTaxFee
            taxable(rate) += taxFee
        Next
        Dim result As Double
        For Each rate As Integer In taxable.Keys
            If type <> 1 Then
                result += Math.Floor(taxable(rate) + excluded(rate))
            ElseIf mode = "floor" Then
                result += Math.Floor(taxable(rate) * (1 + rate / 100) + excluded(rate))
            Else
                result += Decimal.ToDouble(CDec(taxable(rate)) + Math.Round(CDec(taxable(rate)) * rate / 100D, 0, MidpointRounding.AwayFromZero) + CDec(excluded(rate)))
            End If
        Next
        Return Convert.ToInt32(result)
    End Function

    Private Shared Function ReadRate(row As DataRow) As Decimal
        Dim value = row("discount_rate")
        Dim text As String
        If TypeOf value Is Double OrElse TypeOf value Is Single Then
            text = Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)
        Else
            text = Convert.ToString(value, CultureInfo.InvariantCulture)
        End If
        Return ExactMoney.ParseRate(text)
    End Function

    Private Shared Function Int(row As DataRow, name As String) As Integer
        Dim value = row(name)
        If Not (TypeOf value Is Long OrElse TypeOf value Is Integer) Then Throw New InvalidOperationException("整数の格納型が不正です: " & name)
        Return Convert.ToInt32(value, CultureInfo.InvariantCulture)
    End Function

    Private Shared Function checkedNext(value As Integer) As Integer
        Return Convert.ToInt32(CLng(value) + 1)
    End Function

    Private Shared Function Key(ParamArray values As Integer()) As String
        Return String.Join("/", values.Select(Function(n) n.ToString(CultureInfo.InvariantCulture)))
    End Function

    Private Shared Function Quote(name As String) As String
        Return """" & name.Replace("""", """""") & """"
    End Function

    Private Shared Function ReadSource(connection As DbConnection, name As String) As DataTable
        Dim columns = Read(connection, "PRAGMA table_info(" & Quote(name) & ")").Rows.Cast(Of DataRow)().Select(Function(r) CStr(r("name"))).ToArray()
        Dim storage = columns.Select(Function(c, i) "typeof(" & Quote(c) & ") AS _storage_" & i.ToString(CultureInfo.InvariantCulture))
        Dim table = Read(connection, "SELECT *," & String.Join(",", storage) & " FROM " & Quote(name) & " ORDER BY rowid")
        For i = 0 To columns.Length - 1
            table.Columns.Remove("_storage_" & i.ToString(CultureInfo.InvariantCulture))
        Next
        Return table
    End Function

    Friend Shared Function Read(connection As DbConnection, sql As String, ParamArray values As Object()) As DataTable
        Using command = CreateQuery(connection, sql, values), reader = command.ExecuteReader()
            Dim table As New DataTable()
            For i = 0 To reader.FieldCount - 1
                table.Columns.Add(reader.GetName(i), GetType(Object))
            Next
            While reader.Read()
                Dim row = table.NewRow()
                For i = 0 To reader.FieldCount - 1
                    Dim value As Object
                    ' 宣言型によるINTEGERの小数切捨やDATEのDateTime変換を避ける。
                    Dim storageName = "_storage_" & i.ToString(CultureInfo.InvariantCulture)
                    If table.Columns.Contains(storageName) Then
                        Select Case reader.GetString(reader.GetOrdinal(storageName))
                            Case "real" : value = reader.GetDouble(i)
                            Case "integer" : value = reader.GetInt64(i)
                            Case "text" : value = reader.GetString(i)
                            Case "null" : value = DBNull.Value
                            Case Else : Throw New FormatException("未対応の格納型です: " & reader.GetName(i))
                        End Select
                    Else
                        value = reader.GetValue(i)
                    End If
                    row(i) = value
                Next
                table.Rows.Add(row)
            End While
            Return table
        End Using
    End Function

    Friend Shared Function Scalar(connection As DbConnection, sql As String) As Object
        Using query = CreateQuery(connection, sql, {})
            Return query.ExecuteScalar()
        End Using
    End Function

    Friend Shared Sub Execute(connection As DbConnection, sql As String, ParamArray values As Object())
        Using query = CreateQuery(connection, sql, values)
            query.ExecuteNonQuery()
        End Using
    End Sub

    Private Shared Function CreateQuery(connection As DbConnection, sql As String, values As Object()) As DbCommand
        Dim query = connection.CreateCommand()
        query.CommandText = sql
        For i = 0 To values.Length - 1
            Dim parameter = query.CreateParameter()
            parameter.ParameterName = "@p" & i.ToString(CultureInfo.InvariantCulture)
            parameter.Value = If(values(i), DBNull.Value)
            query.Parameters.Add(parameter)
        Next
        Return query
    End Function
End Class
