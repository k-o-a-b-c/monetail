Imports System.Data.SQLite

Public Class DataAccess
    Implements IDisposable

    Private ReadOnly Connection As SQLiteConnection

    Public Sub New(ConnectionString As String)
        Connection = New SQLiteConnection(ConnectionString)
        Connection.Open()
    End Sub

#Region "Master"

    Private _UserList As List(Of User)
    Public ReadOnly Property UserList As List(Of User)
        Get
            ReadUsers()
            Return _UserList
        End Get
    End Property
    Private Sub ReadUsers()
        If _UserList IsNot Nothing Then
            Return
        End If

        _UserList = New List(Of User)()
        Dim myUser As User = Nothing

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select code, name, account_name from m_user order by seq, code"

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim user As New User() With {
                        .Code = reader.GetInt32(0),
                        .Name = reader.GetString(1),
                        .AccountName = reader.GetString(2)
                    }
                    _UserList.Add(user)
                    If user.AccountName = Environment.UserName Then
                        myUser = user
                    End If
                End While
            End Using
        End Using

        If myUser IsNot Nothing Then
            _UserList.Remove(myUser)
            _UserList.Insert(0, myUser)
        End If

    End Sub

    Private _GroupClassificationList As List(Of GroupClassification)
    Private ReadOnly Property GroupClassification(ByVal code As Integer) As GroupClassification
        Get
            ReadGroupClassification()
            Return _GroupClassificationList.Where(Function(m) m.Code = code).FirstOrDefault()
        End Get
    End Property
    Public ReadOnly Property GroupClassificationList() As List(Of GroupClassification)
        Get
            ReadGroupClassification()
            Return _GroupClassificationList
        End Get
    End Property
    Private Sub ReadGroupClassification()
        If _GroupClassificationList IsNot Nothing Then
            Return
        End If
        _GroupClassificationList = New List(Of GroupClassification)()
        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select code, name, default_tax_target, reduced_tax_target from m_group_classification order by seq, code"
            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim groupClassification As New GroupClassification() With {
                        .Code = reader.GetInt32(0),
                        .Name = reader.GetString(1),
                        .DefaultTaxTarget = (reader.GetInt32(2) = 1),
                        .ReducedTaxTarget = (reader.GetInt32(3) = 1)
                    }
                    _GroupClassificationList.Add(groupClassification)
                End While
            End Using
        End Using
    End Sub

    Private _PaymentClassificationList As List(Of PaymentClassification)
    'Private ReadOnly Property PaymentClassification(ByVal code As Integer) As PaymentClassification
    '    Get
    '        ReadPaymentClassification()
    '        Return _PaymentClassificationList.Where(Function(m) m.Code = code).FirstOrDefault()
    '    End Get
    'End Property
    Public ReadOnly Property PaymentClassificationList() As List(Of PaymentClassification)
        Get
            ReadPaymentClassification()
            Return _PaymentClassificationList
        End Get
    End Property
    Private Sub ReadPaymentClassification()
        If _PaymentClassificationList IsNot Nothing Then
            Return
        End If
        _PaymentClassificationList = New List(Of PaymentClassification)()
        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select p.code, p.name, p.default_point_code, p.user_code from m_payment_classification p order by p.seq, p.code"
            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim paymentClassification As New PaymentClassification() With {
                        .Code = reader.GetInt32(0),
                        .Name = reader.GetString(1),
                        .DefaultPointCode = If(reader.IsDBNull(2), 0, reader.GetInt32(2)),
                        .User = UserList.FirstOrDefault(Function(u) u.Code = reader.GetInt32(3))
                    }
                    _PaymentClassificationList.Add(paymentClassification)
                End While
            End Using
        End Using
        Dim myPayments As List(Of PaymentClassification)
        myPayments = _PaymentClassificationList.Where(Function(p) p.User.AccountName = Environment.UserName).ToList()
        _PaymentClassificationList.RemoveAll(Function(p) p.User.AccountName = Environment.UserName)
        _PaymentClassificationList.InsertRange(0, myPayments)
        _PaymentClassificationList.ForEach(Sub(p) If p.User.AccountName <> Environment.UserName Then p.Name = "（" & p.User.Name & "）" & p.Name)

    End Sub
    Public ReadOnly Property PaymentClassification(ByVal code As Integer) As PaymentClassification
        Get
            Return PaymentClassificationList.Where(Function(p) p.Code = code).FirstOrDefault()
        End Get
    End Property

    Private _PointClassificationList As List(Of PointClassification)
    'Private ReadOnly Property PointClassification(ByVal code As Integer) As Point
    '    Get
    '        ReadPointClassification()
    '        Return _PointClassificationList.Where(Function(m) m.Code = code).FirstOrDefault()
    '    End Get
    'End Property
    Public ReadOnly Property PointClassificationList() As List(Of PointClassification)
        Get
            ReadPointClassification()
            Return _PointClassificationList
        End Get
    End Property
    Private Sub ReadPointClassification()
        If _PointClassificationList IsNot Nothing Then
            Return
        End If
        _PointClassificationList = New List(Of PointClassification)()
        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select code, name, default_rate from m_point_classification order by seq, code"
            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim point As New PointClassification() With {
                        .Code = reader.GetInt32(0),
                        .Name = reader.GetString(1),
                        .DefaultRate = If(reader.IsDBNull(2), 0.0, reader.GetDouble(2))
                    }
                    _PointClassificationList.Add(point)
                End While
            End Using
        End Using
    End Sub

    Private _ShopList As List(Of Shop)
    'Private ReadOnly Property Shop(ByVal code As Integer) As Shop
    '    Get
    '        ReadShop()
    '        Return _ShopList.Where(Function(m) m.Code = code).FirstOrDefault()
    '    End Get
    'End Property
    Public ReadOnly Property ShopList() As List(Of Shop)
        Get
            ReadShop()
            Return _ShopList
        End Get
    End Property
    Private Sub ReadShop()
        If _ShopList IsNot Nothing Then
            'Return
        End If
        _ShopList = New List(Of Shop)()
        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select s.code, s.name, s.default_point_code, s.default_group_classification_code, s.tax_rounding_mode from m_shop s left join m_user u on u.code = s.user_code where u.account_name = :account_name or s.user_code is null order by s.seq, s.code"
            command.Parameters.AddWithValue("account_name", Environment.UserName)
            Using reader As SQLiteDataReader = command.ExecuteReader()
                While reader.Read()
                    Dim shop As New Shop() With {
                        .Code = reader.GetInt32(0),
                        .Name = reader.GetString(1),
                        .DefaultPointCode = If(reader.IsDBNull(2), 0, reader.GetInt32(2)),
                        .DefaultGroupClassificationCode = If(reader.IsDBNull(3), 0, reader.GetInt32(3)),
                        .TaxRoundingMode = reader.GetString(4)
                    }
                    _ShopList.Add(shop)
                End While
            End Using
        End Using

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select 0, r.shop_name, null, g.group_classification_code, s.user_code, u1.account_name, u2.account_name, s.user_code is not null and u1.account_name <> :account_name, s.code is null and u2.account_name = :account_name from t_receipts r left join t_receipt_groups g on r.seq = g.receipt_seq and g.seq = 1 left join m_shop s on r.shop_name = s.name left join m_user u1 on u1.code = s.user_code left join m_payment_classification p on r.payment_classification_code = p.code left join m_user u2 on u2.code = p.user_code where ((s.user_code is not null and u1.account_name <> :account_name) or s.code is null) and u2.account_name = :account_name group by r.shop_name order by max(r.date) desc limit 200"
            command.Parameters.AddWithValue("account_name", Environment.UserName)
            Using reader As SQLiteDataReader = command.ExecuteReader()
                While reader.Read()
                    Dim shop As New Shop() With {
                        .Code = reader.GetInt32(0),
                        .Name = reader.GetString(1),
                        .DefaultPointCode = If(reader.IsDBNull(2), 0, reader.GetInt32(2)),
                        .DefaultGroupClassificationCode = If(reader.IsDBNull(3), 0, reader.GetInt32(3)),
                        .TaxRoundingMode = "floor"
                    }
                    _ShopList.Add(shop)
                End While
            End Using
        End Using

    End Sub

    Private _TaxRateList As List(Of TaxRate)
    Private ReadOnly Property TaxRate(ByVal code As Integer) As TaxRate
        Get
            ReadTaxRate()
            Return _TaxRateList.Where(Function(m) m.Code = code).FirstOrDefault()
        End Get
    End Property
    Public ReadOnly Property TaxRateList() As List(Of TaxRate)
        Get
            ReadTaxRate()
            Return _TaxRateList
        End Get
    End Property
    Private Sub ReadTaxRate()
        If _TaxRateList IsNot Nothing Then
            Return
        End If
        _TaxRateList = New List(Of TaxRate)()
        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select code, rate from m_tax_rate where code >= '4' order by start_date desc"
            Using reader As SQLiteDataReader = command.ExecuteReader()
                While reader.Read()
                    Dim taxRate As New TaxRate() With {
                        .Code = reader.GetInt32(0),
                        .Name = reader.GetInt32(1).ToString()
                    }
                    _TaxRateList.Add(taxRate)
                End While
            End Using
        End Using

    End Sub

#End Region

#Region "入力支援"

    'Public Function GetShopList(ByVal search As String) As List(Of Shop)

    '    Dim shopList As New List(Of Shop)()

    '    Using command As SQLiteCommand = Connection.CreateCommand()
    '        command.CommandText = "select 0, r.shop_name, null, g.group_classification_code from t_receipts r left join t_receipt_groups g on r.seq = g.receipt_seq and g.seq = 1 left join m_shop s on r.shop_name = s.name where r.shop_name like '%' || :search || '%' group by r.shop_name order by max(r.date) desc limit 30"
    '        command.Parameters.AddWithValue("search", search)

    '        Using reader As SQLiteDataReader = command.ExecuteReader
    '            While reader.Read()
    '                Dim shop As New Shop() With {
    '                    .Code = reader.GetInt32(0),
    '                    .Name = reader.GetString(1),
    '                    .DefaultPointCode = If(reader.IsDBNull(2), 0, reader.GetInt32(2)),
    '                    .DefaultGroupClassificationCode = If(reader.IsDBNull(3), 0, reader.GetInt32(3))
    '                }
    '                shopList.Add(shop)
    '            End While
    '        End Using
    '    End Using

    '    Return shopList

    'End Function

    Public Function GetShopLocationList(ByVal shopName As String) As List(Of String)

        Dim shopLocationList As New List(Of String)()

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select shop_location from t_receipts where shop_name = :shop_name and shop_location <> '' group by shop_location order by max(date) desc"
            command.Parameters.AddWithValue("shop_name", shopName)

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    shopLocationList.Add(reader.GetString(0))
                End While
            End Using
        End Using

        Return shopLocationList

    End Function

    Public Function GetTaxType(ByVal shopName As String) As Integer

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select tax_type from t_receipts where shop_name = :shop_name order by date desc limit 1"
            command.Parameters.AddWithValue("shop_name", shopName)

            Dim result As Object = command.ExecuteScalar()

            Return If(result Is Nothing, -1, Convert.ToInt32(result))
        End Using

    End Function

    Public Function GetPaymentClassification(ByVal shopName As String) As Integer

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select r.payment_classification_code from t_receipts r left join m_payment_classification p on p.code = r.payment_classification_code left join m_user u on u.code = p.user_code where shop_name = :shop_name and u.account_name = :account_name order by date desc limit 1"
            command.Parameters.AddWithValue("shop_name", shopName)
            command.Parameters.AddWithValue("account_name", Environment.UserName)
            Return Convert.ToInt32(command.ExecuteScalar())
        End Using

    End Function

    Public Function GetTaxRoundingMode(ByVal shopName As String) As String

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select r.tax_rounding_mode from t_receipts r left join m_payment_classification p on p.code = r.payment_classification_code left join m_user u on u.code = p.user_code where r.shop_name = :shop_name and u.account_name = :account_name order by r.date desc, r.seq desc limit 1"
            command.Parameters.AddWithValue("shop_name", shopName)
            command.Parameters.AddWithValue("account_name", Environment.UserName)

            Dim result As Object = command.ExecuteScalar()
            Return If(result Is Nothing, "floor", Convert.ToString(result))
        End Using

    End Function

    Public Function GetDefaultGroupClassification(ByVal shopName As String) As Integer

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select g.group_classification_code from t_receipts r left join t_receipt_groups g on r.seq = g.receipt_seq left join m_payment_classification p on r.payment_classification_code = p.code left join m_user u on u.code = p.user_code where shop_name = :shop_name and u.account_name = :account_name order by r.date desc, g.seq limit 1"
            command.Parameters.AddWithValue("shop_name", shopName)
            command.Parameters.AddWithValue("account_name", Environment.UserName)
            Return Convert.ToInt32(command.ExecuteScalar())
        End Using

    End Function

    Public Function GetItemNameList(ByVal shopName As String, ByVal groupClassificationCode As Integer) As List(Of String)

        Dim itemNameList As New List(Of String)()

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select name, max(r.date) from t_receipts r join t_receipt_groups g on r.seq = g.receipt_seq join t_receipt_details d on g.receipt_seq = d.receipt_seq and g.seq = d.group_seq where r.shop_name = :shop_name and g.group_classification_code = :group_classification_code group by name order by 2 desc"
            command.Parameters.AddWithValue("shop_name", shopName)
            command.Parameters.AddWithValue("group_classification_code", groupClassificationCode)

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    itemNameList.Add(reader.GetString(0))
                End While
            End Using
        End Using

        Return itemNameList

    End Function

    Public Function GetTransferNameList() As List(Of String)

        Dim transferNameList As New List(Of String)()

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select distinct name from t_transfers order by update_datetime desc"

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    transferNameList.Add(reader.GetString(0))
                End While
            End Using
        End Using

        Return transferNameList

    End Function

    Public Function GetTransferFromPaymentClassification(ByVal name As String) As Integer

        Dim transferNameList As New List(Of PaymentClassification)()

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select from_payment_classification_code from t_transfers t left join m_payment_classification p1 on p1.code = t.from_payment_classification_code left join m_user u1 on u1.code = p1.user_code left join m_payment_classification p2 on p2.code = t.to_payment_classification_code left join m_user u2 on u2.code = p2.user_code where t.name = :name and (u1.account_name = :account_name or u2.account_name = :account_name) order by update_datetime desc limit 1"
            command.Parameters.AddWithValue("name", name)
            command.Parameters.AddWithValue("account_name", Environment.UserName)
            Return Convert.ToInt32(command.ExecuteScalar())
        End Using

    End Function

    Public Function GetTransferToPaymentClassification(ByVal fromPaymentClassificationCode As Integer, ByVal name As String) As Integer

        Dim transferNameList As New List(Of PaymentClassification)()

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "select to_payment_classification_code from t_transfers where name = :name and from_payment_classification_code = :from_payment_classification_code order by update_datetime desc limit 1"
            command.Parameters.AddWithValue("name", name)
            command.Parameters.AddWithValue("from_payment_classification_code", fromPaymentClassificationCode)
            Return Convert.ToInt32(command.ExecuteScalar())
        End Using

    End Function

    Public Function GetAutoBalance(ByVal TargetDate As Date, ByVal PaymentClassification As PaymentClassification) As Nullable(Of Integer)

        Dim balance As Integer = 0
        Dim dateFrom As DateTime = DateTime.MinValue
        Dim dateTo As DateTime = TargetDate.Date + New TimeSpan(23, 59, 59)

        Dim LastBalance As Balance = Me.GetLastBalance(TargetDate.Date, PaymentClassification)
        If LastBalance IsNot Nothing Then
            dateFrom = LastBalance.Date + New TimeSpan(23, 59, 59)
            balance = LastBalance.Groups.FirstOrDefault(Function(g) g.PaymentClassification.Code = PaymentClassification.Code).Balance
        End If

        Dim receipts As List(Of Receipt) = Me.GetPaymentReceipts(dateFrom, dateTo, PaymentClassification)
        For Each receipt As Receipt In receipts
            balance -= receipt.Total
        Next

        Dim transfers As List(Of Transfer) = Me.GetPaymentTransfers(dateFrom, dateTo, PaymentClassification)
        For Each transfer As Transfer In transfers
            If transfer.FromPaymentClassification.Code = PaymentClassification.Code Then
                balance -= transfer.Fee
            Else
                balance += transfer.Fee
            End If
        Next

        If LastBalance IsNot Nothing OrElse receipts.Count > 0 OrElse transfers.Count > 0 Then
            Return balance
        Else
            Return Nothing
        End If

    End Function

#End Region

#Region "取得"

    Public Function GetReceipts(ByVal year As Integer) As List(Of Receipt)

        Return GetReceipts(year, 0)

    End Function

    Public Function GetReceipts(ByVal YearMonth As Date) As List(Of Receipt)

        Return GetReceipts(YearMonth.Year, YearMonth.Month)

    End Function

    Public Function GetReceipts(ByVal Year As Integer, ByVal Month As Integer, Optional ByVal GroupCode As Integer = -1, Optional ByVal PaymentCode As Integer = -1) As List(Of Receipt)

        Dim receiptList As New List(Of Receipt)()

        Dim MonthFrom As Integer = Month
        Dim MonthTo As Integer = Month
        If Month = 0 Then
            MonthFrom = 1
            MonthTo = 12
        End If

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select seq, date, shop_name, shop_location, tax_type, payment_classification_code, tag, tax_rounding_mode from t_receipts where date between :date_from and :date_to and (payment_classification_code = :payment_classification_code or :payment_classification_code = -1) order by date desc, seq desc"
            command.Parameters.AddWithValue("date_from", New Date(Year, MonthFrom, 1))
            command.Parameters.AddWithValue("date_to", New Date(Year, MonthTo, Date.DaysInMonth(Year, MonthTo), 23, 59, 59))
            command.Parameters.AddWithValue("payment_classification_code", PaymentCode)

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim receipt As New Receipt() With {
                        .Seq = reader.GetInt32(0),
                        .Date = reader.GetDateTime(1),
                        .ShopName = reader.GetString(2),
                        .ShopLocation = If(reader.IsDBNull(3), String.Empty, reader.GetString(3)),
                        .TaxType = reader.GetInt32(4),
                        .PaymentClassification = PaymentClassification(reader.GetInt32(5)),
                        .Tag = If(reader.IsDBNull(6), String.Empty, reader.GetString(6)),
                        .TaxRoundingMode = reader.GetString(7)
                    }
                    LoadReceiptGroupData(receipt, GroupCode)
                    receiptList.Add(receipt)
                End While
            End Using
        End Using

        Return receiptList

    End Function

    Public Sub LoadReceiptChildData(ByVal receipt As Receipt)
        LoadReceiptGroupData(receipt)
    End Sub

    Private Sub LoadReceiptGroupData(ByVal receipt As Receipt, Optional ByVal GroupCode As Integer = -1)
        receipt.Groups.Clear()

        Using groupCommand As SQLiteCommand = Connection.CreateCommand
            groupCommand.CommandText = "select receipt_seq, seq, group_classification_code, tax, tag from t_receipt_groups where receipt_seq = :receipt_seq and (group_classification_code = :group_classification_code or :group_classification_code = -1) order by seq"
            groupCommand.Parameters.AddWithValue("receipt_seq", receipt.Seq)
            groupCommand.Parameters.AddWithValue("group_classification_code", GroupCode)

            Using groupReader As SQLiteDataReader = groupCommand.ExecuteReader()
                While groupReader.Read()
                    Dim receiptGroup As New ReceiptGroup()
                    receiptGroup.ReceiptSeq = groupReader.GetInt32(0)
                    receiptGroup.Seq = groupReader.GetInt32(1)
                    receiptGroup.GroupClassification = GroupClassification(groupReader.GetInt32(2))
                    receiptGroup.TaxRate = TaxRate(groupReader.GetInt32(3))
                    receiptGroup.Tag = If(groupReader.IsDBNull(4), String.Empty, groupReader.GetString(4))
                    LoadReceiptDetailData(receiptGroup)
                    receipt.Groups.Add(receiptGroup)
                End While
            End Using
        End Using

    End Sub

    Private Sub LoadReceiptDetailData(ByVal receiptGroup As ReceiptGroup)

        Using detailCommand As SQLiteCommand = Connection.CreateCommand
            detailCommand.CommandText = "select receipt_seq, group_seq, seq, name, fee, quantity, discount_fee, discount_rate, tax_target, tag from t_receipt_details where receipt_seq = :receipt_seq and group_seq = :group_seq order by seq"
            detailCommand.Parameters.AddWithValue("receipt_seq", receiptGroup.ReceiptSeq)
            detailCommand.Parameters.AddWithValue("group_seq", receiptGroup.Seq)

            Using detailReader As SQLiteDataReader = detailCommand.ExecuteReader()
                While detailReader.Read()
                    Dim receiptDetail As New ReceiptDetail()
                    receiptDetail.ReceiptSeq = detailReader.GetInt32(0)
                    receiptDetail.GroupSeq = detailReader.GetInt32(1)
                    receiptDetail.Seq = detailReader.GetInt32(2)
                    receiptDetail.Name = detailReader.GetString(3)
                    receiptDetail.Fee = detailReader.GetInt32(4)
                    receiptDetail.Quantity = detailReader.GetInt32(5)
                    receiptDetail.DiscountFee = detailReader.GetInt32(6)
                    receiptDetail.DiscountRate = detailReader.GetDouble(7)
                    receiptDetail.TaxTarget = (detailReader.GetInt32(8) = 1)
                    receiptDetail.Tag = If(detailReader.IsDBNull(9), String.Empty, detailReader.GetString(9))
                    LoadReceiptDetailUsersData(receiptDetail)
                    receiptGroup.Details.Add(receiptDetail)
                End While
            End Using
        End Using

    End Sub

    Private Sub LoadReceiptDetailUsersData(ByVal receiptDetail As ReceiptDetail)

        Using detailUserCommand As SQLiteCommand = Connection.CreateCommand
            detailUserCommand.CommandText = "select user_code from t_receipt_detail_users where receipt_seq = :receipt_seq and group_seq = :group_seq and detail_seq = :detail_seq order by user_code"
            detailUserCommand.Parameters.AddWithValue("receipt_seq", receiptDetail.ReceiptSeq)
            detailUserCommand.Parameters.AddWithValue("group_seq", receiptDetail.GroupSeq)
            detailUserCommand.Parameters.AddWithValue("detail_seq", receiptDetail.Seq)

            Using detailUserReader As SQLiteDataReader = detailUserCommand.ExecuteReader()
                While detailUserReader.Read()
                    receiptDetail.Users.Add(UserList.First(Function(u) u.Code = detailUserReader.GetInt32(0)))
                End While
            End Using
        End Using

    End Sub

    Public Function GetTransfers(ByVal YearMonth As Date) As List(Of Transfer)

        Dim transferList As New List(Of Transfer)()

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select seq, date, name, fee, from_payment_classification_code, to_payment_classification_code, tag from t_transfers where date between :date_from and :date_to order by date desc, seq desc"
            command.Parameters.AddWithValue("date_from", New Date(YearMonth.Year, YearMonth.Month, 1))
            command.Parameters.AddWithValue("date_to", New Date(YearMonth.Year, YearMonth.Month, Date.DaysInMonth(YearMonth.Year, YearMonth.Month), 23, 59, 59))

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim transfer As New Transfer() With {
                        .Seq = reader.GetInt32(0),
                        .Date = reader.GetDateTime(1),
                        .Name = reader.GetString(2),
                        .Fee = reader.GetInt32(3),
                        .FromPaymentClassification = PaymentClassification(reader.GetInt32(4)),
                        .ToPaymentClassification = PaymentClassification(reader.GetInt32(5)),
                        .Tag = reader.GetString(6)
                    }
                    transferList.Add(transfer)
                End While
            End Using
        End Using

        Return transferList

    End Function

    Public Function GetBalances(ByVal YearMonth As Date) As List(Of Balance)

        Dim balanceList As New List(Of Balance)()

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select seq, date, tag from t_balances where date between :date_from and :date_to order by date desc, seq desc"
            command.Parameters.AddWithValue("date_from", New Date(YearMonth.Year, YearMonth.Month, 1))
            command.Parameters.AddWithValue("date_to", New Date(YearMonth.Year, YearMonth.Month, Date.DaysInMonth(YearMonth.Year, YearMonth.Month), 23, 59, 59))

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim balance As New Balance() With {
                        .Seq = reader.GetInt32(0),
                        .Date = reader.GetDateTime(1),
                        .Tag = reader.GetString(2)
                    }
                    LoadBalanceGroupData(balance)
                    balanceList.Add(balance)
                End While
            End Using
        End Using

        Return balanceList

    End Function

    Private Sub LoadBalanceGroupData(ByVal balance As Balance)
        balance.Groups.Clear()

        Using groupCommand As SQLiteCommand = Connection.CreateCommand
            groupCommand.CommandText = "select balance_seq, payment_classification_code, balance, valuation_amount, tag from t_balance_groups g left join m_payment_classification p on p.code = g.payment_classification_code where balance_seq = :balance_seq order by p.seq, p.code"
            groupCommand.Parameters.AddWithValue("balance_seq", balance.Seq)

            Using groupReader As SQLiteDataReader = groupCommand.ExecuteReader()
                While groupReader.Read()
                    Dim balanceGroup As New BalanceGroup() With {
                        .BalanceSeq = groupReader.GetInt32(0),
                        .PaymentClassification = PaymentClassification(groupReader.GetInt32(1)),
                        .Balance = groupReader.GetInt32(2),
                        .Tag = groupReader.GetString(4)
                    }
                    If groupReader.IsDBNull(3) Then
                        balanceGroup.ValuationAmount = Nothing
                    Else
                        balanceGroup.ValuationAmount = groupReader.GetInt32(3)
                    End If
                    balance.Groups.Add(balanceGroup)
                End While
            End Using
        End Using

    End Sub

    Public Function ExistsBalance(ByVal targetDate As DateTime, ByVal seq As Integer) As Boolean

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select count(*) from t_balances where date = :date and seq <> :seq"
            command.Parameters.AddWithValue("date", targetDate)
            command.Parameters.AddWithValue("seq", seq)

            Return Convert.ToInt32(command.ExecuteScalar) > 0
        End Using

    End Function

    Public Function GetLastBalance(ByVal targetDate As DateTime, ByVal payment As PaymentClassification) As Balance

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select b.seq, b.date, b.tag from t_balances b inner join t_balance_groups bg on bg.balance_seq = b.seq where b.date < :date and bg.payment_classification_code = :payment_classification_code order by b.date desc limit 1"
            command.Parameters.AddWithValue("date", targetDate)
            command.Parameters.AddWithValue("payment_classification_code", payment.Code)

            Dim balance As Balance = Nothing
            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    balance = New Balance() With {
                        .Seq = reader.GetInt32(0),
                        .Date = reader.GetDateTime(1),
                        .Tag = reader.GetString(2)
                    }
                    LoadBalanceGroupData(balance)
                End While
            End Using

            Return balance
        End Using

    End Function

    Public Function GetLastValuationAmount(ByVal targetDate As DateTime, ByVal payment As PaymentClassification) As Balance

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select b.seq, b.date, b.tag from t_balances b inner join t_balance_groups bg on bg.balance_seq = b.seq where b.date < :date and bg.payment_classification_code = :payment_classification_code and bg.valuation_amount is not null order by b.date desc limit 1"
            command.Parameters.AddWithValue("date", targetDate)
            command.Parameters.AddWithValue("payment_classification_code", payment.Code)

            Dim balance As Balance = Nothing
            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    balance = New Balance() With {
                        .Seq = reader.GetInt32(0),
                        .Date = reader.GetDateTime(1),
                        .Tag = reader.GetString(2)
                    }
                    LoadBalanceGroupData(balance)
                End While
            End Using

            Return balance
        End Using

    End Function

    Public Function GetPaymentReceipts(ByVal dateFrom As DateTime, ByVal dateTo As DateTime, ByVal payment As PaymentClassification) As List(Of Receipt)

        Dim receiptList As New List(Of Receipt)()

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select seq, date, shop_name, shop_location, tax_type, payment_classification_code, tag, tax_rounding_mode from t_receipts where date between :date_from and :date_to and payment_classification_code = :payment_classification_code"
            command.Parameters.AddWithValue("date_from", dateFrom)
            command.Parameters.AddWithValue("date_to", dateTo)
            command.Parameters.AddWithValue("payment_classification_code", payment.Code)

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim receipt As New Receipt() With {
                        .Seq = reader.GetInt32(0),
                        .Date = reader.GetDateTime(1),
                        .ShopName = reader.GetString(2),
                        .ShopLocation = If(reader.IsDBNull(3), String.Empty, reader.GetString(3)),
                        .TaxType = reader.GetInt32(4),
                        .PaymentClassification = PaymentClassification(reader.GetInt32(5)),
                        .Tag = If(reader.IsDBNull(6), String.Empty, reader.GetString(6)),
                        .TaxRoundingMode = reader.GetString(7)
                    }
                    LoadReceiptGroupData(receipt)
                    receiptList.Add(receipt)
                End While
            End Using
        End Using

        Return receiptList

    End Function

    Public Function GetPaymentTransfers(ByVal dateFrom As DateTime, ByVal dateTo As DateTime, ByVal payment As PaymentClassification) As List(Of Transfer)

        Dim transferList As New List(Of Transfer)()

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select seq, date, name, fee, from_payment_classification_code, to_payment_classification_code, tag from t_transfers where date between :date_from and :date_to and (from_payment_classification_code = :payment_classification_code or to_payment_classification_code = :payment_classification_code)"
            command.Parameters.AddWithValue("date_from", dateFrom)
            command.Parameters.AddWithValue("date_to", dateTo)
            command.Parameters.AddWithValue("payment_classification_code", payment.Code)

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim transfer As New Transfer() With {
                        .Seq = reader.GetInt32(0),
                        .Date = reader.GetDateTime(1),
                        .Name = reader.GetString(2),
                        .Fee = reader.GetInt32(3),
                        .FromPaymentClassification = PaymentClassification(reader.GetInt32(4)),
                        .ToPaymentClassification = PaymentClassification(reader.GetInt32(5)),
                        .Tag = reader.GetString(6)
                    }
                    transferList.Add(transfer)
                End While
            End Using
        End Using

        Return transferList

    End Function

    Public Function GetPaymentMemoList() As List(Of PaymentMemo)

        Dim PaymentMemoList As New List(Of PaymentMemo)()

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select p.code, m.check_date, m.tag from m_payment_classification p left join t_payment_memo m on m.payment_classification_code = p.code left join m_user u on p.user_code = u.code order by case when u.name = :name then 0 else 1 end, u.seq, u.code, p.seq, p.code"
            command.Parameters.AddWithValue("name", Environment.UserName)

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim PaymentMemo As New PaymentMemo() With {
                        .PaymentClassification = PaymentClassification(reader.GetInt32(0))
                    }
                    If Not reader.IsDBNull(1) Then PaymentMemo.CheckDate = reader.GetDateTime(1)
                    If Not reader.IsDBNull(2) Then PaymentMemo.Tag = reader.GetString(2)
                    PaymentMemoList.Add(PaymentMemo)
                End While
            End Using
        End Using

        Return PaymentMemoList

    End Function

#End Region

#Region "登録"

    Public Sub RegisterReceipt(ByVal receipt As Receipt)

        Calculation.ValidateTaxRoundingMode(receipt.TaxRoundingMode)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            'シーケンス設定
            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "select case when max(seq) is null then 0 else max(seq) end from t_receipts"
                receipt.Seq = Convert.ToInt32(command.ExecuteScalar()) + 1
            End Using
            SetChildSequence(receipt)

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "insert into t_receipts (seq, date, shop_name, shop_location, tax_type, tax_rounding_mode, payment_classification_code, tag, update_user, update_datetime, total) values (:seq, :date, :shop_name, :shop_location, :tax_type, :tax_rounding_mode, :payment_classification_code, :tag, :update_user, :update_datetime, :total)"
                command.Parameters.AddWithValue("seq", receipt.Seq)
                command.Parameters.AddWithValue("date", receipt.Date)
                command.Parameters.AddWithValue("shop_name", receipt.ShopName)
                command.Parameters.AddWithValue("shop_location", receipt.ShopLocation)
                command.Parameters.AddWithValue("tax_type", receipt.TaxType)
                command.Parameters.AddWithValue("tax_rounding_mode", receipt.TaxRoundingMode)
                command.Parameters.AddWithValue("payment_classification_code", receipt.PaymentClassification.Code)
                command.Parameters.AddWithValue("tag", receipt.Tag)
                command.Parameters.AddWithValue("update_user", Environment.UserName)
                command.Parameters.AddWithValue("update_datetime", DateTime.Now)
                command.Parameters.AddWithValue("total", receipt.Total)
                command.ExecuteNonQuery()
            End Using

            InsertReceiptGroups(receipt)

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Private Sub SetChildSequence(ByVal receipt As Receipt)
        For i As Integer = 0 To receipt.Groups.Count - 1
            receipt.Groups(i).ReceiptSeq = receipt.Seq
            receipt.Groups(i).Seq = i + 1
            For j As Integer = 0 To receipt.Groups(i).Details.Count - 1
                receipt.Groups(i).Details(j).ReceiptSeq = receipt.Seq
                receipt.Groups(i).Details(j).GroupSeq = receipt.Groups(i).Seq
                receipt.Groups(i).Details(j).Seq = j + 1
            Next
        Next
    End Sub

    Private Sub InsertReceiptGroups(ByVal receipt As Receipt)

        Using groupCommand As SQLiteCommand = Connection.CreateCommand()
            groupCommand.CommandText = "insert into t_receipt_groups (receipt_seq, seq, group_classification_code, tax, tag) values (:receipt_seq, :seq, :group_classification_code, :tax, :tag)"
            For Each group As ReceiptGroup In receipt.Groups
                groupCommand.Parameters.Clear()
                groupCommand.Parameters.AddWithValue("receipt_seq", group.ReceiptSeq)
                groupCommand.Parameters.AddWithValue("seq", group.Seq)
                groupCommand.Parameters.AddWithValue("group_classification_code", group.GroupClassification.Code)
                groupCommand.Parameters.AddWithValue("tax", group.TaxRate.Code)
                groupCommand.Parameters.AddWithValue("tag", group.Tag)
                groupCommand.ExecuteNonQuery()
                Using detailCommand As SQLiteCommand = Connection.CreateCommand()
                    detailCommand.CommandText = "insert into t_receipt_details (receipt_seq, group_seq, seq, name, fee, quantity, discount_fee, discount_rate, tax_target, tag) values (:receipt_seq, :group_seq, :seq, :name, :fee, :quantity, :discount_fee, :discount_rate, :tax_target, :tag)"
                    For Each detail As ReceiptDetail In group.Details
                        detailCommand.Parameters.Clear()
                        detailCommand.Parameters.AddWithValue("receipt_seq", detail.ReceiptSeq)
                        detailCommand.Parameters.AddWithValue("group_seq", detail.GroupSeq)
                        detailCommand.Parameters.AddWithValue("seq", detail.Seq)
                        detailCommand.Parameters.AddWithValue("name", detail.Name)
                        detailCommand.Parameters.AddWithValue("fee", detail.Fee)
                        detailCommand.Parameters.AddWithValue("quantity", detail.Quantity)
                        detailCommand.Parameters.AddWithValue("discount_fee", detail.DiscountFee)
                        detailCommand.Parameters.AddWithValue("discount_rate", detail.DiscountRate)
                        detailCommand.Parameters.AddWithValue("tax_target", detail.TaxTarget)
                        detailCommand.Parameters.AddWithValue("tag", detail.Tag)
                        detailCommand.ExecuteNonQuery()
                        Using detailUserCommand As SQLiteCommand = Connection.CreateCommand()
                            detailUserCommand.CommandText = "insert into t_receipt_detail_users(receipt_seq, group_seq, detail_seq, user_code) values (:receipt_seq, :group_seq, :detail_seq, :user_code)"
                            For Each user As User In detail.Users
                                detailUserCommand.Parameters.Clear()
                                detailUserCommand.Parameters.AddWithValue("receipt_seq", detail.ReceiptSeq)
                                detailUserCommand.Parameters.AddWithValue("group_seq", detail.GroupSeq)
                                detailUserCommand.Parameters.AddWithValue("detail_seq", detail.Seq)
                                detailUserCommand.Parameters.AddWithValue("user_code", user.Code)
                                detailUserCommand.ExecuteNonQuery()
                            Next
                        End Using
                    Next
                End Using
            Next
        End Using

    End Sub

    Public Sub UpdateReceipt(ByVal receipt As Receipt)

        Calculation.ValidateTaxRoundingMode(receipt.TaxRoundingMode)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            'シーケンス設定
            SetChildSequence(receipt)

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "update t_receipts set date = :date, shop_name = :shop_name, shop_location = :shop_location, tax_type = :tax_type, tax_rounding_mode = :tax_rounding_mode, payment_classification_code = :payment_classification_code, tag = :tag, update_user = :update_user, update_datetime = :update_datetime, total = :total where seq = :seq"
                command.Parameters.AddWithValue("seq", receipt.Seq)
                command.Parameters.AddWithValue("date", receipt.Date)
                command.Parameters.AddWithValue("shop_name", receipt.ShopName)
                command.Parameters.AddWithValue("shop_location", receipt.ShopLocation)
                command.Parameters.AddWithValue("tax_type", receipt.TaxType)
                command.Parameters.AddWithValue("tax_rounding_mode", receipt.TaxRoundingMode)
                command.Parameters.AddWithValue("payment_classification_code", receipt.PaymentClassification.Code)
                command.Parameters.AddWithValue("tag", receipt.Tag)
                command.Parameters.AddWithValue("update_user", Environment.UserName)
                command.Parameters.AddWithValue("update_datetime", DateTime.Now)
                command.Parameters.AddWithValue("total", receipt.Total)
                command.ExecuteNonQuery()
            End Using

            DeleteReceiptGroups(receipt)
            InsertReceiptGroups(receipt)

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Private Sub DeleteReceiptGroups(ByVal receipt As Receipt)

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.Parameters.AddWithValue("receipt_seq", receipt.Seq)

            '外部キーの参照元から順に削除する。
            command.CommandText = "delete from t_receipt_detail_users where receipt_seq = :receipt_seq"
            command.ExecuteNonQuery()

            command.CommandText = "delete from t_receipt_details where receipt_seq = :receipt_seq"
            command.ExecuteNonQuery()

            command.CommandText = "delete from t_receipt_groups where receipt_seq = :receipt_seq"
            command.ExecuteNonQuery()
        End Using

    End Sub

    Public Sub DeleteReceipt(ByVal receipt As Receipt)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            DeleteReceiptGroups(receipt)

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "delete from t_receipts where seq = :seq"
                command.Parameters.AddWithValue("seq", receipt.Seq)
                command.ExecuteNonQuery()
            End Using

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Public Sub RegisterTransfer(ByVal transfer As Transfer)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            'シーケンス設定
            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "select case when max(seq) is null then 0 else max(seq) end from t_transfers"
                transfer.Seq = Convert.ToInt32(command.ExecuteScalar()) + 1
            End Using

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "insert into t_transfers(seq, date, name, fee, from_payment_classification_code, to_payment_classification_code, tag, update_user, update_datetime) values (:seq, :date, :name, :fee, :from_payment_classification_code, :to_payment_classification_code, :tag, :update_user, :update_datetime)"
                command.Parameters.AddWithValue("seq", transfer.Seq)
                command.Parameters.AddWithValue("date", transfer.Date)
                command.Parameters.AddWithValue("name", transfer.Name)
                command.Parameters.AddWithValue("fee", transfer.Fee)
                command.Parameters.AddWithValue("from_payment_classification_code", transfer.FromPaymentClassification.Code)
                command.Parameters.AddWithValue("to_payment_classification_code", transfer.ToPaymentClassification.Code)
                command.Parameters.AddWithValue("tag", transfer.Tag)
                command.Parameters.AddWithValue("update_user", Environment.UserName)
                command.Parameters.AddWithValue("update_datetime", DateTime.Now)
                command.ExecuteNonQuery()
            End Using

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Public Sub UpdateTransfer(ByVal transfer As Transfer)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "update t_transfers set date = :date, name = :name, fee = :fee, from_payment_classification_code = :from_payment_classification_code, to_payment_classification_code = :to_payment_classification_code, tag = :tag, update_user = :update_user, update_datetime = :update_datetime where seq = :seq"
                command.Parameters.AddWithValue("seq", transfer.Seq)
                command.Parameters.AddWithValue("date", transfer.Date)
                command.Parameters.AddWithValue("name", transfer.Name)
                command.Parameters.AddWithValue("fee", transfer.Fee)
                command.Parameters.AddWithValue("from_payment_classification_code", transfer.FromPaymentClassification.Code)
                command.Parameters.AddWithValue("to_payment_classification_code", transfer.ToPaymentClassification.Code)
                command.Parameters.AddWithValue("tag", transfer.Tag)
                command.Parameters.AddWithValue("update_user", Environment.UserName)
                command.Parameters.AddWithValue("update_datetime", DateTime.Now)
                command.ExecuteNonQuery()
            End Using

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Public Sub DeleteTransfer(ByVal transfer As Transfer)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "delete from t_transfers where seq = :seq"
                command.Parameters.AddWithValue("seq", transfer.Seq)
                command.ExecuteNonQuery()
            End Using

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Public Sub RegisterBalance(ByVal balance As Balance)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            'シーケンス設定
            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "select case when max(seq) is null then 0 else max(seq) end from t_balances"
                balance.Seq = Convert.ToInt32(command.ExecuteScalar()) + 1
            End Using
            For Each group As BalanceGroup In balance.Groups
                group.BalanceSeq = balance.Seq
            Next

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "insert into t_balances (seq, date, tag, update_user, update_datetime) values (:seq, :date, :tag, :update_user, :update_datetime)"
                command.Parameters.AddWithValue("seq", balance.Seq)
                command.Parameters.AddWithValue("date", balance.Date)
                command.Parameters.AddWithValue("tag", balance.Tag)
                command.Parameters.AddWithValue("update_user", Environment.UserName)
                command.Parameters.AddWithValue("update_datetime", DateTime.Now)
                command.ExecuteNonQuery()
            End Using

            InsertBalanceGroups(balance)

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Private Sub InsertBalanceGroups(ByVal balance As Balance)

        Using groupCommand As SQLiteCommand = Connection.CreateCommand()
            groupCommand.CommandText = "insert into t_balance_groups (balance_seq, payment_classification_code, balance, valuation_amount, tag) values (:balance_seq, :payment_classification_code, :balance, :valuation_amount, :tag)"
            For Each group As BalanceGroup In balance.Groups
                groupCommand.Parameters.Clear()
                groupCommand.Parameters.AddWithValue("balance_seq", group.BalanceSeq)
                groupCommand.Parameters.AddWithValue("payment_classification_code", group.PaymentClassification.Code)
                groupCommand.Parameters.AddWithValue("balance", group.Balance)
                groupCommand.Parameters.AddWithValue("valuation_amount", group.ValuationAmount)
                groupCommand.Parameters.AddWithValue("tag", group.Tag)
                groupCommand.ExecuteNonQuery()
            Next
        End Using

    End Sub

    Public Sub UpdateBalance(ByVal balance As Balance)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            'シーケンス設定
            For Each group As BalanceGroup In balance.Groups
                group.BalanceSeq = balance.Seq
            Next

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "update t_balances set date = :date, tag = :tag, update_user = :update_user, update_datetime = :update_datetime where seq = :seq"
                command.Parameters.AddWithValue("seq", balance.Seq)
                command.Parameters.AddWithValue("date", balance.Date)
                command.Parameters.AddWithValue("tag", balance.Tag)
                command.Parameters.AddWithValue("update_user", Environment.UserName)
                command.Parameters.AddWithValue("update_datetime", DateTime.Now)
                command.ExecuteNonQuery()
            End Using

            DeleteBalanceGroups(balance)
            InsertBalanceGroups(balance)

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Public Sub DeleteBalance(ByVal balance As Balance)

        Dim trans As SQLiteTransaction = Nothing
        Try
            trans = Connection.BeginTransaction()

            Using command As SQLiteCommand = Connection.CreateCommand()
                command.CommandText = "delete from t_balances where seq = :seq"
                command.Parameters.AddWithValue("seq", balance.Seq)
                command.ExecuteNonQuery()
            End Using

            DeleteBalanceGroups(balance)

            trans.Commit()

        Catch ex As Exception
            If trans IsNot Nothing Then
                trans.Rollback()
            End If
            Throw

        Finally
            If trans IsNot Nothing Then
                trans.Dispose()
            End If

        End Try

    End Sub

    Private Sub DeleteBalanceGroups(ByVal balance As Balance)

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.Parameters.AddWithValue("balance_seq", balance.Seq)

            command.CommandText = "delete from t_balance_groups where balance_seq = :balance_seq"
            command.ExecuteNonQuery()
        End Using

    End Sub

    Public Sub RegisterPaymentMemo(ByVal PaymentMemoList As List(Of PaymentMemo))

        Using command As SQLiteCommand = Connection.CreateCommand()
            command.CommandText = "insert into t_payment_memo(payment_classification_code, check_date, tag) values (:payment_classification_code, :check_date, :tag) on conflict(payment_classification_code) do update set check_date = :check_date, tag = :tag"
            For Each PaymentMemo As PaymentMemo In PaymentMemoList
                command.Parameters.Clear()
                command.Parameters.AddWithValue("payment_classification_code", PaymentMemo.PaymentClassification.Code)
                command.Parameters.AddWithValue("check_date", PaymentMemo.CheckDate)
                command.Parameters.AddWithValue("tag", PaymentMemo.Tag)
                command.ExecuteNonQuery()
            Next
        End Using

    End Sub

#End Region

#Region "IDisposable Support"
    Private disposedValue As Boolean ' 重複する呼び出しを検出するには

    ' IDisposable
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                ' TODO: マネージド状態を破棄します (マネージド オブジェクト)。
                Connection.Dispose()
            End If

            ' TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、下の Finalize() をオーバーライドします。
            ' TODO: 大きなフィールドを null に設定します。
        End If
        disposedValue = True
    End Sub

    ' TODO: 上の Dispose(disposing As Boolean) にアンマネージド リソースを解放するコードが含まれる場合にのみ Finalize() をオーバーライドします。
    'Protected Overrides Sub Finalize()
    '    ' このコードを変更しないでください。クリーンアップ コードを上の Dispose(disposing As Boolean) に記述します。
    '    Dispose(False)
    '    MyBase.Finalize()
    'End Sub

    ' このコードは、破棄可能なパターンを正しく実装できるように Visual Basic によって追加されました。
    Public Sub Dispose() Implements IDisposable.Dispose
        ' このコードを変更しないでください。クリーンアップ コードを上の Dispose(disposing As Boolean) に記述します。
        Dispose(True)
        ' TODO: 上の Finalize() がオーバーライドされている場合は、次の行のコメントを解除してください。
        ' GC.SuppressFinalize(Me)
    End Sub
#End Region

End Class
