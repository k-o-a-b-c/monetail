Imports System.Collections.Generic
Imports System.Globalization

Module ReceiptFixtures
    Public Function Detail(fee As Integer, Optional taxable As Boolean = True,
                           Optional quantity As Integer = 1, Optional discountFee As Integer = 0,
                           Optional discountRate As Decimal = 0D) As ReceiptDetail
        Return New ReceiptDetail With {
            .Name = "test item", .Fee = fee, .Quantity = quantity, .TaxTarget = taxable,
            .DiscountFee = discountFee, .DiscountRate = discountRate, .Tag = ""
        }
    End Function

    Public Function Group(rate As Integer, ParamArray details As ReceiptDetail()) As ReceiptGroup
        Return New ReceiptGroup With {
            .GroupClassification = New GroupClassification With {.Code = 1, .Name = "test group"},
            .TaxRate = New TaxRate With {.Code = If(rate = 8, 4, 5), .Name = rate.ToString(CultureInfo.InvariantCulture)},
            .Details = New List(Of ReceiptDetail)(details), .Tag = ""
        }
    End Function

    Public Function MakeReceipt(mode As String, ParamArray groups As ReceiptGroup()) As Receipt
        Return New Receipt With {
            .Date = New DateTime(2026, 10, 16, 12, 0, 0), .ShopName = "unregistered shop",
            .ShopLocation = "", .TaxType = 1, .TaxRoundingMode = mode,
            .PaymentClassification = New PaymentClassification With {.Code = 1, .Name = "cash"},
            .Groups = New List(Of ReceiptGroup)(groups), .Tag = ""
        }
    End Function
End Module
