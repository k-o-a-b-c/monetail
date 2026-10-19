Imports System.Collections.Generic

' Only replaces I/O for AutoBalance. All amounts are calculated by production VB code.
' PersistenceTests compiles and exercises the real DataAccess separately on Windows.
Public Class DataAccess
    Public Property LastBalance As Balance
    Public Property Receipts As New List(Of Receipt)
    Public Property Transfers As New List(Of Transfer)

    Public Function GetLastBalance(baseDate As DateTime, payment As PaymentClassification) As Balance
        Return LastBalance
    End Function

    Public Function GetPaymentReceipts(dateFrom As DateTime, dateTo As DateTime, payment As PaymentClassification,
                                      Optional endExclusive As Boolean = False) As List(Of Receipt)
        Return Receipts
    End Function

    Public Function GetPaymentTransfers(dateFrom As DateTime, dateTo As DateTime, payment As PaymentClassification,
                                       Optional endExclusive As Boolean = False) As List(Of Transfer)
        Return Transfers
    End Function
End Class
