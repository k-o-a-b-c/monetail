Imports System.Configuration
Imports System.Data.SQLite
Imports System.IO

Friend NotInheritable Class DatabaseSetupException
    Inherits Exception

    Public Sub New(message As String, Optional inner As Exception = Nothing)
        MyBase.New(message, inner)
    End Sub
End Class

Friend NotInheritable Class DatabaseStartup
    ' 元DDLの必要列。列の型・値・追加列・索引・FK設定はこの検証では変更しない。
    Private Shared ReadOnly RequiredColumns As String() = {
        "t_receipts:seq,date,shop_name,shop_location,tax_type,payment_classification_code,tag,update_user,update_datetime,total",
        "m_payment_classification:code,name,seq,default_point_code,user_code,valid_from,valid_to",
        "m_point_classification:code,name,seq,default_rate",
        "t_receipt_points:receipt_seq,seq,code,rate,fixed_points,total_points",
        "t_transfers:seq,date,name,fee,from_payment_classification_code,to_payment_classification_code,tag,update_user,update_datetime",
        "m_group_classification:code,name,seq,default_tax_category_code",
        "t_receipt_groups:receipt_seq,seq,group_classification_code,tag",
        "t_receipt_details:receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_code,tag",
        "m_shop:code,name,seq,default_point_code,default_group_classification_code,user_code",
        "m_tax_rate:code,rate,start_date,tax_treatment",
        "t_balances:seq,date,tag,update_user,update_datetime",
        "t_balance_groups:balance_seq,payment_classification_code,balance,tag,valuation_amount",
        "m_user:code,name,account_name,seq",
        "t_receipt_detail_users:receipt_seq,group_seq,detail_seq,user_code",
        "t_payment_memo:payment_classification_code,check_date,tag",
        "t_exchange_rates:rate_date,base_currency,quote_currency,middle_rate",
        "m_tax_category:code,name",
        "m_tax_category_rule:tax_category_code,start_date,tax_code",
        "detail_tax_migration:id,completed_utc"
    }

    Friend Shared ReadOnly Property ConfigurationFilePath As String
        Get
            Return AppDomain.CurrentDomain.SetupInformation.ConfigurationFile
        End Get
    End Property

    Private Shared Function SetupError(message As String, Optional databasePath As String = Nothing,
                                      Optional inner As Exception = Nothing) As DatabaseSetupException
        Return New DatabaseSetupException(message & Environment.NewLine &
            "設定ファイル: " & ConfigurationFilePath &
            If(databasePath Is Nothing, String.Empty, Environment.NewLine & "DB: " & databasePath), inner)
    End Function

    Friend Shared Function OpenConnection(connectionString As String) As SQLiteConnection
        Dim settings As SQLiteConnectionStringBuilder
        Try
            If String.IsNullOrWhiteSpace(connectionString) Then Throw New ArgumentException()
            settings = New SQLiteConnectionStringBuilder(connectionString)
            Dim source = settings.DataSource
            If String.IsNullOrWhiteSpace(source) OrElse source.Equals(":memory:", StringComparison.OrdinalIgnoreCase) Then
                Throw New ArgumentException()
            End If
            ' ドライブ相対・ルート相対は作業ディレクトリに依存するため受け付けない。
            If (source.Length >= 2 AndAlso source(1) = ":"c AndAlso
                (source.Length < 3 OrElse (source(2) <> "\"c AndAlso source(2) <> "/"c))) OrElse
                (source.StartsWith("\", StringComparison.Ordinal) AndAlso Not source.StartsWith("\\", StringComparison.Ordinal)) OrElse
                (source.StartsWith("/", StringComparison.Ordinal) AndAlso Not source.StartsWith("//", StringComparison.Ordinal)) Then
                Throw New ArgumentException()
            End If
            settings.DataSource = Path.GetFullPath(If(Path.IsPathRooted(source), source,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, source)))
            ' 設定にFalseが含まれても、利用者のDBを自動作成しない。
            settings.FailIfMissing = True
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is FormatException OrElse
            TypeOf ex Is OverflowException OrElse TypeOf ex Is NotSupportedException OrElse
            TypeOf ex Is IOException OrElse TypeOf ex Is System.Security.SecurityException
            Throw SetupError("ConnectionStringのData Sourceに、既存のSQLiteファイルを指定してください。", inner:=ex)
        End Try

        If Not File.Exists(settings.DataSource) Then
            Throw SetupError("DBファイルが見つかりません。Data Sourceのパスとアクセス権を確認してください。", settings.DataSource)
        End If

        Dim connection As SQLiteConnection = Nothing
        Dim ready As Boolean = False
        Try
            connection = New SQLiteConnection(settings.ConnectionString)
            connection.Open()
            ValidateSchema(connection, settings.DataSource)
            ready = True
            Return connection
        Catch ex As Exception When TypeOf ex Is SQLiteException OrElse TypeOf ex Is ArgumentException OrElse
            TypeOf ex Is FormatException OrElse TypeOf ex Is OverflowException
            Throw SetupError("SQLiteのDBを開けません。ファイル形式・アクセス権・接続設定を確認してください。", settings.DataSource, ex)
        Finally
            If Not ready AndAlso connection IsNot Nothing Then
                ' 失敗した接続をプールに残さず、設定修正・手動移行後に開き直せるようにする。
                If connection.State = System.Data.ConnectionState.Open Then SQLiteConnection.ClearPool(connection)
                connection.Dispose()
            End If
        End Try
    End Function

    Private Shared Sub ValidateSchema(connection As SQLiteConnection, databasePath As String)
        Dim tables As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Using command = connection.CreateCommand()
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'"
            Using reader = command.ExecuteReader()
                While reader.Read()
                    tables.Add(reader.GetString(0))
                End While
            End Using
        End Using
        If Not tables.Contains("t_receipts") Then Throw SetupError("Monetailの必要なテーブルがありません: t_receipts", databasePath)
        If Not tables.Contains("detail_tax_migration") Then
            Throw SetupError("明細税率のコピーDB移行が必要です。docs/41-detail-tax-copy-migration.mdの手順を確認してください。", databasePath)
        End If
        Dim columns As New Dictionary(Of String, HashSet(Of String))(StringComparer.OrdinalIgnoreCase)
        For Each requirement In RequiredColumns
            Dim parts = requirement.Split(":"c)
            Dim table = parts(0)
            If Not tables.Contains(table) Then
                Throw SetupError("Monetailの必要なテーブルがありません: " & table, databasePath)
            End If
            Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Using command = connection.CreateCommand()
                ' テーブル名は上記の固定定義だけを使う。
                command.CommandText = "PRAGMA table_info(" & table & ")"
                Using reader = command.ExecuteReader()
                    While reader.Read()
                        names.Add(reader.GetString(1))
                    End While
                End Using
            End Using
            columns.Add(table, names)
            For Each name In parts(1).Split(","c)
                RequireColumn(columns, table, name, "Monetailの必要な列がありません。", databasePath)
            Next
        Next
        Using command = connection.CreateCommand()
            command.CommandText = "SELECT COUNT(*) FROM detail_tax_migration WHERE id='20261022_detail_tax_decimal'"
            If Convert.ToInt32(command.ExecuteScalar()) <> 1 Then Throw SetupError("明細税率の移行が完了していません。", databasePath)
        End Using
        For Each table In New String() {"t_receipts", "m_shop"}
            RequireColumn(columns, table, "tax_rounding_mode",
                "税端数のDB移行が必要です。バックアップ後、20261017_01_shop_tax_rounding.sqlの手順を確認してください。", databasePath)
        Next
        For Each table In New String() {"t_receipts", "t_transfers", "t_balances"}
            RequireColumn(columns, table, "revision_token",
                "更新版のDB移行が必要です。バックアップ後、20261019_01_revision_tokens.sqlの手順を確認してください。", databasePath)
        Next
    End Sub

    Private Shared Sub RequireColumn(columns As Dictionary(Of String, HashSet(Of String)), table As String,
                                     name As String, message As String, databasePath As String)
        If Not columns(table).Contains(name) Then
            Throw SetupError(message & Environment.NewLine & "対象: " & table & "." & name, databasePath)
        End If
    End Sub

    Friend Shared Function TryOpenConfiguredDatabase(readSetting As Func(Of String), reportError As Action(Of String)) As DataAccess
        Try
            Return New DataAccess(readSetting())
        Catch ex As DatabaseSetupException
            reportError(ex.Message)
        Catch ex As Exception When IsConfigurationError(ex)
            ' 内部例外には接続文字列が含まれ得るため、画面には表示しない。
            reportError("XML設定を読み込めません。設定ファイルの構文とConnectionStringを確認してください。" &
                Environment.NewLine & "設定ファイル: " & ConfigurationFilePath)
        End Try
        Return Nothing
    End Function

    Private Shared Function IsConfigurationError(ex As Exception) As Boolean
        While ex IsNot Nothing
            If TypeOf ex Is ConfigurationErrorsException Then Return True
            ex = ex.InnerException
        End While
        Return False
    End Function
End Class
