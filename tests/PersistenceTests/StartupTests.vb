Imports System.Configuration
Imports System.Data.SQLite
Imports System.IO
Imports System.Linq
Imports System.Xml.Linq

Friend Module StartupTests
    Public Sub ConfigurationDefaults()
        Dim source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-source.config")
        Dim mapped = ConfigurationManager.OpenMappedExeConfiguration(
            New ExeConfigurationFileMap With {.ExeConfigFilename = source}, ConfigurationUserLevel.None)
        Dim section = DirectCast(mapped.GetSection("applicationSettings/HouseholdAccountsBeta.My.MySettings"), ClientSettingsSection)
        Dim deployed = section.Settings.Cast(Of SettingElement)().Single(Function(s) s.Name = "ConnectionString").Value.ValueXml.InnerText
        Dim settingsXml = XDocument.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-source.settings"))
        Dim defaults = settingsXml.Descendants().Single(Function(e) e.Name.LocalName = "Setting" AndAlso CStr(e.Attribute("Name")) = "ConnectionString").Elements().Single().Value
        Equal(deployed, defaults)
        Dim designer = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-settings-source.txt"))
        Equal(True, designer.Contains("DefaultSettingValueAttribute(""" & deployed & """)"))
        Dim parsed As New SQLiteConnectionStringBuilder(deployed)
        Equal("E:\家計簿フォルダ\家計簿.sqlite", parsed.DataSource)
        Equal(True, parsed.FailIfMissing)
    End Sub

    Public Sub RelativePathsAndOptions()
        Using fixture As New DatabaseFixture
            Dim filename = "startup-店舗 'quote';-" & Guid.NewGuid().ToString("N") & ".sqlite"
            Dim absolute = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename)
            Dim originalDirectory = Environment.CurrentDirectory
            Using area As New TemporaryArea
                Try
                    File.Copy(New SQLiteConnectionStringBuilder(fixture.ConnectionString).DataSource, absolute)
                    Dim before = FileBytes(absolute)
                    Environment.CurrentDirectory = area.DirectoryPath
                    For Each foreignKeys In New Boolean() {False, True}
                        Dim settings As New SQLiteConnectionStringBuilder With {
                            .DataSource = filename, .Pooling = False, .ForeignKeys = foreignKeys,
                            .FailIfMissing = False, .DefaultTimeout = 2}
                        Using data As New DataAccess(settings.ConnectionString)
                            Dim actual = GetConnection(data)
                            Dim options As New SQLiteConnectionStringBuilder(actual.ConnectionString)
                            Equal(absolute, options.DataSource)
                            Equal(True, options.FailIfMissing)
                            Equal(False, options.Pooling)
                            Equal(2, options.DefaultTimeout)
                            Using command = actual.CreateCommand()
                                command.CommandText = "PRAGMA foreign_keys"
                                Equal(If(foreignKeys, 1L, 0L), Convert.ToInt64(command.ExecuteScalar()))
                            End Using
                            Equal(41, data.GetReceipts(2026, 10).Single().Total)
                        End Using
                    Next
                    Equal(before, FileBytes(absolute), "Opening a valid database must preserve its saved contents")
                    Equal(False, File.Exists(Path.Combine(area.DirectoryPath, filename)), "The working directory must not receive a database")
                Finally
                    Environment.CurrentDirectory = originalDirectory
                    File.Delete(absolute)
                End Try
            End Using
        End Using
    End Sub

    Public Sub MissingOrInvalidSettings()
        For Each value In New String() {Nothing, "", " ", "Version=3", "Data Source=""", "Data Source=:memory:",
                                        "Data Source=C:relative.sqlite", "Data Source=\relative.sqlite", "Data Source=" & ChrW(0)}
            Reject(value, "ConnectionString")
        Next
        Using area As New TemporaryArea
            Dim missing = area.FilePath("missing.sqlite")
            Dim settings As New SQLiteConnectionStringBuilder With {.DataSource = missing, .FailIfMissing = False, .Pooling = False}
            Reject(settings.ConnectionString, "DBファイルが見つかりません")
            Equal(False, File.Exists(missing), "FailIfMissing=False must not permit automatic creation")
            settings.DataSource = area.FilePath("missing-folder\nested.sqlite")
            Reject(settings.ConnectionString, "DBファイルが見つかりません")
            Equal(False, Directory.Exists(area.FilePath("missing-folder")))
            settings.DataSource = area.DirectoryPath
            Reject(settings.ConnectionString, "DBファイルが見つかりません")
        End Using
    End Sub

    Public Sub InvalidDatabases()
        Using area As New TemporaryArea
            Dim empty = area.FilePath("empty.sqlite")
            File.WriteAllBytes(empty, New Byte() {})
            UnchangedRejection(empty, "必要なテーブル")
            Dim corrupt = area.FilePath("corrupt.sqlite")
            File.WriteAllText(corrupt, "not a SQLite database")
            UnchangedRejection(corrupt, "SQLiteのDBを開けません")
            Dim unrelated = area.FilePath("other.sqlite")
            Execute(unrelated, "CREATE TABLE unrelated(id INTEGER);")
            UnchangedRejection(unrelated, "必要なテーブル")
        End Using
    End Sub

    Public Sub RequiredSchema()
        Using area As New TemporaryArea
            Dim legacy = area.FilePath("legacy.sqlite")
            Execute(legacy, Script("monetail_schema.sql"))
            UnchangedRejection(legacy, "41-detail-tax-copy-migration.md")
            Execute(legacy, Script("tax_rounding.sql"))
            UnchangedRejection(legacy, "41-detail-tax-copy-migration.md")
            Execute(legacy, Script("revision_tokens.sql"))
            Migrate(legacy)
            Execute(legacy, "ALTER TABLE m_user ADD COLUMN extra TEXT; CREATE INDEX extra_index ON m_user(extra);")
            Dim complete = FileBytes(legacy)
            Using data As New DataAccess(ConnectionString(legacy))
                Equal(0, data.GetReceipts(2026, 10).Count)
            End Using
            Equal(complete, FileBytes(legacy), "Extra columns and indexes must remain acceptable and unchanged")

            For Each column In New String() {"t_receipts.tax_rounding_mode", "m_shop.tax_rounding_mode",
                                             "t_receipts.revision_token", "t_transfers.revision_token", "t_balances.revision_token",
                                             "m_user.account_name"}
                Dim parts = column.Split("."c)
                Dim incomplete = area.FilePath(column & ".sqlite")
                File.Copy(legacy, incomplete)
                Execute(incomplete, "ALTER TABLE " & parts(0) & " RENAME COLUMN " & parts(1) & " TO missing_column;")
                Dim message = UnchangedRejection(incomplete, column)
                If parts(1) = "tax_rounding_mode" Then Equal(True, message.Contains("20261017_01_shop_tax_rounding.sql"))
                If parts(1) = "revision_token" Then Equal(True, message.Contains("20261019_01_revision_tokens.sql"))
            Next
            Dim missingTable = area.FilePath("missing-table.sqlite")
            File.Copy(legacy, missingTable)
            Execute(missingTable, "DROP TABLE t_exchange_rates; CREATE VIEW t_exchange_rates AS SELECT 'x' AS rate_date, 'x' AS base_currency, 'x' AS quote_currency, 1 AS middle_rate;")
            UnchangedRejection(missingTable, "必要なテーブルがありません: t_exchange_rates")
        End Using
    End Sub

    Public Sub ErrorReportingAndRecovery()
        Using area As New TemporaryArea
            Dim databasePath = area.FilePath("recover.sqlite")
            Execute(databasePath, Script("monetail_schema.sql"))
            Dim messages As New List(Of String)()
            Dim reads As Integer = 0
            Dim readSetting As Func(Of String) = Function()
                                                    reads += 1
                                                    Return ConnectionString(databasePath)
                                                End Function
            Equal(True, DatabaseStartup.TryOpenConfiguredDatabase(readSetting, AddressOf messages.Add) Is Nothing)
            Equal(1, reads)
            Equal(1, messages.Count)
            Equal(True, messages(0).Contains("41-detail-tax-copy-migration.md"))
            Execute(databasePath, Script("tax_rounding.sql") & Environment.NewLine & Script("revision_tokens.sql"))
            Migrate(databasePath)
            messages.Clear()
            Using data = DatabaseStartup.TryOpenConfiguredDatabase(readSetting, AddressOf messages.Add)
                Equal(False, data Is Nothing)
                Equal(0, data.GetReceipts(2026, 10).Count)
            End Using
            Equal(2, reads)
            Equal(0, messages.Count)
            For Each wrapped In New Boolean() {False, True}
                messages.Clear()
                Dim badSetting As Func(Of String) = Function()
                                                       Dim errorValue As New ConfigurationErrorsException("Password=do-not-display")
                                                       If wrapped Then Throw New TypeInitializationException("settings", errorValue)
                                                       Throw errorValue
                                                   End Function
                Equal(True, DatabaseStartup.TryOpenConfiguredDatabase(badSetting, AddressOf messages.Add) Is Nothing)
                Equal(1, messages.Count)
                Equal(True, messages(0).Contains("XML設定"))
                Equal(True, messages(0).Contains(DatabaseStartup.ConfigurationFilePath))
                Equal(False, messages(0).Contains("do-not-display"))
            Next
            Dim unexpected As Func(Of String) = Function()
                                                   Throw New InvalidOperationException("unrelated failure")
                                               End Function
            messages.Clear()
            Throws(Of InvalidOperationException)(Sub() DatabaseStartup.TryOpenConfiguredDatabase(unexpected, AddressOf messages.Add))
            Equal(0, messages.Count, "Unrelated errors must propagate instead of being presented as settings errors")
        End Using
    End Sub

    Private Function Reject(value As String, expected As String) As String
        Dim failure As DatabaseSetupException = Nothing
        Try
            Using data As New DataAccess(value)
                Throw New Exception("An invalid database unexpectedly opened")
            End Using
        Catch ex As DatabaseSetupException
            failure = ex
        End Try
        Equal(False, failure Is Nothing)
        Equal(True, failure.Message.Contains(expected), failure.Message)
        Equal(True, failure.Message.Contains(DatabaseStartup.ConfigurationFilePath))
        Return failure.Message
    End Function

    Private Function UnchangedRejection(databasePath As String, expected As String) As String
        Dim before = FileBytes(databasePath)
        ' 失敗した接続は、Pooling=Trueでもハンドルを保持してはいけない。
        Dim settings As New SQLiteConnectionStringBuilder With {.DataSource = databasePath, .Pooling = True}
        Dim message = Reject(settings.ConnectionString, expected)
        Equal(before, FileBytes(databasePath), "Rejected databases must retain their original bytes")
        Using exclusive = File.Open(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            Equal(True, exclusive.CanRead)
        End Using
        Return message
    End Function

    Private Function ConnectionString(databasePath As String) As String
        Return New SQLiteConnectionStringBuilder With {.DataSource = databasePath, .Pooling = False}.ConnectionString
    End Function

    Private Function GetConnection(data As DataAccess) As SQLiteConnection
        Return DirectCast(GetType(DataAccess).GetField("Connection", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).GetValue(data), SQLiteConnection)
    End Function

    Private Sub Execute(databasePath As String, sql As String)
        Using connection As New SQLiteConnection(ConnectionString(databasePath))
            connection.Open()
            Using command = connection.CreateCommand()
                command.CommandText = sql
                command.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Function Script(name As String) As String
        Return File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name))
    End Function

    Private Function FileBytes(databasePath As String) As String
        Return Convert.ToBase64String(File.ReadAllBytes(databasePath))
    End Function

    Private NotInheritable Class TemporaryArea
        Implements IDisposable
        Public ReadOnly DirectoryPath As String = Path.Combine(Path.GetTempPath(), "monetail-startup-" & Guid.NewGuid().ToString("N"))
        Public Sub New()
            Directory.CreateDirectory(DirectoryPath)
        End Sub
        Public Function FilePath(name As String) As String
            Return Path.Combine(DirectoryPath, name)
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            Directory.Delete(DirectoryPath, recursive:=True)
        End Sub
    End Class
    Private Sub Migrate(path As String)
        Using connection As New SQLiteConnection(ConnectionString(path))
            connection.Open()
            DetailTaxMigration.Run(connection)
        End Using
    End Sub
End Module
