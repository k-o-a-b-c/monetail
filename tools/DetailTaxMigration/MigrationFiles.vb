Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Text
#If NETFRAMEWORK Then
Imports System.Data.SQLite
#Else
Imports Microsoft.Data.Sqlite
#End If

Public NotInheritable Class MigrationFiles
    Private Sub New()
    End Sub

    ' 元DBは読取専用。SQLiteのバックアップAPIでWALを含む一貫したコピーを作る。
    Public Shared Sub MigrateCopy(sourcePath As String, copyPath As String, reportPath As String)
        sourcePath = Path.GetFullPath(sourcePath)
        copyPath = Path.GetFullPath(copyPath)
        reportPath = Path.GetFullPath(reportPath)
        If Not File.Exists(sourcePath) Then Throw New FileNotFoundException("元DBがありません。", sourcePath)
        If {sourcePath, copyPath, reportPath}.Distinct(StringComparer.OrdinalIgnoreCase).Count() <> 3 Then Throw New ArgumentException("元DB・コピー・レポートには別のパスを指定してください。")
        ' CreateNewで既存ファイルを保護する。失敗時は今回作った出力だけを消す。
        Dim copyCreated = False
        Dim reportCreated = False
        Try
            Using reserved = New FileStream(copyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                copyCreated = True
            End Using
            Using reserved = New FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                reportCreated = True
            End Using
            Using source = Open(sourcePath, True), target = Open(copyPath, False)
#If NETFRAMEWORK Then
                DirectCast(source, SQLiteConnection).BackupDatabase(DirectCast(target, SQLiteConnection), "main", "main", -1, Nothing, 0)
#Else
                DirectCast(source, SqliteConnection).BackupDatabase(DirectCast(target, SqliteConnection))
#End If
                DetailTaxMigration.Run(target)
                ExportReport(target, reportPath)
            End Using
        Catch
            If reportCreated Then File.Delete(reportPath)
            If copyCreated Then File.Delete(copyPath)
            Throw
        End Try
    End Sub

    Private Shared Function Open(path As String, readOnlyMode As Boolean) As DbConnection
#If NETFRAMEWORK Then
        Dim settings As New SQLiteConnectionStringBuilder With {.DataSource = path, .ReadOnly = readOnlyMode, .FailIfMissing = True, .Pooling = False}
        Dim connection As DbConnection = New SQLiteConnection(settings.ConnectionString)
#Else
        Dim settings As New SqliteConnectionStringBuilder With {.DataSource = path, .Mode = If(readOnlyMode, SqliteOpenMode.ReadOnly, SqliteOpenMode.ReadWrite), .Pooling = False}
        Dim connection As DbConnection = New SqliteConnection(settings.ConnectionString)
#End If
        Try
            connection.Open()
            Return connection
        Catch
            connection.Dispose()
            Throw
        End Try
    End Function

    Private Shared Sub ExportReport(connection As DbConnection, path As String)
        Dim rows = DetailTaxMigration.Read(connection,
            "SELECT receipt_seq,stored_total,legacy_total,new_total,new_total-legacy_total AS calculation_delta," &
            "new_total-stored_total AS stored_delta,legacy_incomes,new_incomes,legacy_expenses,new_expenses,old_revision,new_revision " &
            "FROM detail_tax_receipt_audit ORDER BY receipt_seq")
        Using writer As New StreamWriter(path, False, New UTF8Encoding(True))
            writer.WriteLine(String.Join(",", rows.Columns.Cast(Of System.Data.DataColumn)().Select(Function(c) c.ColumnName)))
            For Each row As System.Data.DataRow In rows.Rows
                writer.WriteLine(String.Join(",", row.ItemArray.Select(Function(v) CsvValue(v))))
            Next
        End Using
    End Sub

    Private Shared Function CsvValue(value As Object) As String
        Dim text = Convert.ToString(value, CultureInfo.InvariantCulture)
        If text.IndexOfAny(New Char() {","c, """"c, ChrW(10), ChrW(13)}) >= 0 Then Return """" & text.Replace("""", """""") & """"
        Return text
    End Function
End Class
