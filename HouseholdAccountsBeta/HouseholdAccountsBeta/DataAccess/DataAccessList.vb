Imports System.Data.SQLite

Partial Public Class DataAccess
    Implements IDisposable

#Region "GroupStatistics"

    Public Function GetBalanceStatistics(ByVal year As Integer) As List(Of BalanceStatistics)

        Dim BalanceStatisticsList As New List(Of BalanceStatistics)()

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "select b.date, bg.payment_classification_code, bg.balance, bg.valuation_amount from t_balances b inner join t_balance_groups bg on bg.balance_seq = b.seq where b.date between :date_from and :date_to order by b.date desc, bg.payment_classification_code"
            command.Parameters.AddWithValue("date_from", New Date(year, 1, 1))
            command.Parameters.AddWithValue("date_to", New Date(year, 12, Date.DaysInMonth(year, 12), 23, 59, 59))

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim balance As New BalanceStatistics() With {
                        .[Date] = reader.GetDateTime(0),
                        .PaymentClassificationCode = reader.GetInt32(1),
                        .Balance = reader.GetInt32(2)
                    }
                    If Not reader.IsDBNull(3) Then
                        balance.ValuationAmount = reader.GetInt32(3)
                    End If
                    BalanceStatisticsList.Add(balance)
                End While
            End Using
        End Using

        Return BalanceStatisticsList

    End Function

    Public Function GetUsdJpyRatesForYear(ByVal year As Integer) As Dictionary(Of Integer, Rate)

        Dim RateDict As New Dictionary(Of Integer, Rate)()

        Using command As SQLiteCommand = Connection.CreateCommand
            command.CommandText = "Select r.rate_date, r.middle_rate FROM (Select r.rate_date, r.middle_rate, ROW_NUMBER() OVER (PARTITION BY strftime('%Y-%m', r.rate_date) ORDER BY ABS(julianday(r.rate_date) - julianday(date (strftime('%Y-%m-01', r.rate_date), '+1 month', '-1 day')))) AS seq FROM t_exchange_rates r WHERE r.rate_date BETWEEN :year || '-01-01' AND :year || '-12-31' and r.base_currency = :base and r.quote_currency = :quote) r WHERE r.seq = 1 ORDER BY rate_date"
            command.Parameters.AddWithValue("year", year.ToString())
            command.Parameters.AddWithValue("base", "USD")
            command.Parameters.AddWithValue("quote", "JPY")

            Using reader As SQLiteDataReader = command.ExecuteReader
                While reader.Read()
                    Dim rate As New Rate() With {
                        .RateDate = reader.GetDateTime(0),
                        .MiddleRate = reader.GetDecimal(1)
                    }
                    RateDict.Add(rate.RateDate.Month, rate)
                End While
            End Using
        End Using

        Return RateDict

    End Function

#End Region

End Class
