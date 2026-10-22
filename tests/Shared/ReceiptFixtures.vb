Imports System.Collections.Generic
Imports System.Globalization

Module ReceiptFixtures
    Public Function Detail(fee As Integer, Optional taxable As Boolean = True,
                           Optional quantity As Integer = 1, Optional discountFee As Integer = 0,
                           Optional discountRate As Decimal = 0) As ReceiptDetail
        Return New ReceiptDetail With {
            .Name = "test item", .Fee = fee, .Quantity = quantity,
            .TaxRate = New TaxRate With {.Code = If(taxable, 5, 0), .Name = If(taxable, "10", "0"), .TaxTreatment = If(taxable, "taxable", "out_of_scope")},
            .DiscountFee = discountFee, .DiscountRate = discountRate, .Tag = ""
        }
    End Function

    Public Function Group(rate As Integer, ParamArray details As ReceiptDetail()) As ReceiptGroup
        Dim tax As New TaxRate With {.Code = If(rate = 8, 4, 5), .Name = rate.ToString(CultureInfo.InvariantCulture)}
        For Each line In details
            If line.TaxRate.IsTaxable Then line.TaxRate = tax
        Next
        Return New ReceiptGroup With {
            .GroupClassification = New GroupClassification With {.Code = 1, .Name = "test group"},
            .Details = New List(Of ReceiptDetail)(details), .Tag = ""
        }
    End Function

    Public Function MakeReceipt(mode As String, ParamArray groups As ReceiptGroup()) As Receipt
        ' 1分類1グループの有効なテストモデルを作る。複数分類の税率別合算も検証する。
        Dim used As New HashSet(Of Integer)
        Dim code = 92
        For Each item In groups
            If used.Contains(item.GroupClassification.Code) Then
                While used.Contains(code)
                    code += 1
                End While
                item.GroupClassification.Code = code
            End If
            used.Add(item.GroupClassification.Code)
        Next
        Return New Receipt With {
            .Date = New DateTime(2026, 10, 16, 12, 0, 0), .ShopName = "unregistered shop",
            .ShopLocation = "", .TaxType = 1, .TaxRoundingMode = mode,
            .PaymentClassification = New PaymentClassification With {.Code = 1, .Name = "cash"},
            .Groups = New List(Of ReceiptGroup)(groups), .Tag = ""
        }
    End Function
End Module
