Imports System.Data.SQLite
Imports System.IO
Imports System.Reflection

Friend Class DatabaseFixture
    Implements IDisposable

    Private ReadOnly databasePath As String = Path.Combine(Path.GetTempPath(), "monetail-test-" & Guid.NewGuid().ToString("N") & ".db")
    Public ReadOnly ConnectionString As String
    Public ReadOnly Data As DataAccess

    Public Sub New(Optional foreignKeys As Boolean? = Nothing)
        ConnectionString = "Data Source=" & databasePath & ";Version=3;Pooling=False;"
        If foreignKeys.HasValue Then ConnectionString &= "Foreign Keys=" & foreignKeys.Value.ToString() & ";"
        Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "monetail_schema.sql")))
        Execute("INSERT INTO m_user(code,name,account_name,seq) VALUES(1,'test',@account,1);", Environment.UserName)
        Execute("INSERT INTO m_payment_classification(code,name,seq,user_code) VALUES(1,'cash',1,1);" &
                "INSERT INTO m_group_classification(code,name,seq,default_tax_target,reduced_tax_target) VALUES(1,'food',1,1,0);" &
                "INSERT INTO m_tax_rate(code,rate,start_date) VALUES(4,8,'2014-04-01'),(5,10,'2019-10-01');" &
                "INSERT INTO m_shop(code,name,seq,user_code) VALUES(1,'registered shop',1,1);" &
                "INSERT INTO t_receipts(seq,date,shop_name,shop_location,tax_type,payment_classification_code,tag,update_user,update_datetime,total) " &
                "VALUES(1,'2026-10-16 12:00:00','history shop','',1,1,'','test','2026-10-16',41);" &
                "INSERT INTO t_receipt_groups(receipt_seq,seq,group_classification_code,tax,tag) VALUES(1,1,1,5,''),(1,2,1,5,'');" &
                "INSERT INTO t_receipt_details(receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_target,tag) " &
                "VALUES(1,1,1,'item',19,1,0,0,1,''),(1,2,1,'item',19,1,0,0,1,'');" &
                "INSERT INTO t_receipt_detail_users(receipt_seq,group_seq,detail_seq,user_code) VALUES(1,1,1,1);")
        Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tax_rounding.sql")))
        Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "revision_tokens.sql")))
        Data = New DataAccess(ConnectionString)
    End Sub

    ' PRAGMA settings are connection-specific: inspect the connection used by production methods.
    Public ReadOnly Property ActualDataConnection As SQLiteConnection
        Get
            Return DirectCast(GetType(DataAccess).GetField("Connection", BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(Data), SQLiteConnection)
        End Get
    End Property

    Public Sub Execute(sql As String, Optional account As String = Nothing)
        Using connection As New SQLiteConnection(ConnectionString)
            connection.Open()
            Using command = connection.CreateCommand()
                command.CommandText = sql
                If account IsNot Nothing Then command.Parameters.AddWithValue("account", account)
                command.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Public Function Scalar(sql As String) As Object
        Using connection As New SQLiteConnection(ConnectionString)
            connection.Open()
            Using command = connection.CreateCommand()
                command.CommandText = sql
                Return command.ExecuteScalar()
            End Using
        End Using
    End Function

    Public Sub AddReceipt(seq As Integer, shopName As String, mode As String, receiptDate As DateTime,
                          Optional paymentCode As Integer = 1)
        Using connection As New SQLiteConnection(ConnectionString)
            connection.Open()
            Using command = connection.CreateCommand()
                command.CommandText = "INSERT INTO t_receipts " &
                    "(seq,date,shop_name,shop_location,tax_type,tax_rounding_mode,payment_classification_code,tag,update_user,update_datetime,total) " &
                    "VALUES (@seq,@date,@shop,'',1,@mode,@payment,'','test',@date,0);" &
                    "INSERT INTO t_receipt_groups(receipt_seq,seq,group_classification_code,tax,tag) VALUES(@seq,1,1,5,'');" &
                    "INSERT INTO t_receipt_details(receipt_seq,group_seq,seq,name,fee,quantity,discount_fee,discount_rate,tax_target,tag) " &
                    "VALUES(@seq,1,1,'history item',0,1,0,0,1,'')"
                command.Parameters.AddWithValue("seq", seq)
                command.Parameters.AddWithValue("date", receiptDate)
                command.Parameters.AddWithValue("shop", shopName)
                command.Parameters.AddWithValue("mode", mode)
                command.Parameters.AddWithValue("payment", paymentCode)
                command.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Data.Dispose()
        File.Delete(databasePath)
    End Sub
End Class
