Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json

Module Program
    Function Main() As Integer
        CheckJsonExpectations()
        Check("receipt amount validation catches signed overflow and income expense cancellation", AddressOf ReceiptAmountValidationTests.AmountRanges)
        Check("pending amount validation preserves the original receipt lists details and settings", AddressOf ReceiptAmountValidationTests.CandidateIsReadOnly)
        Check("amount validation uses selected tax settings without changing saved settings", AddressOf ReceiptAmountValidationTests.SelectedTaxSettings)
        Check("integer expressions evaluate multiplication before addition and subtraction", AddressOf IntegerExpressionTests.Priority)
        Check("integer expressions support signed operands whitespace and Int32 boundaries", AddressOf IntegerExpressionTests.SignedAndBoundaries)
        Check("malformed and non integer expressions normalize to zero", AddressOf IntegerExpressionTests.InvalidInputs)
        Check("integer literal and intermediate arithmetic overflow normalize to zero", AddressOf IntegerExpressionTests.Overflow)
        Check("default and validated modes", AddressOf ModesAreValidated)
        Check("exact decimal floor including signed boundaries", AddressOf DecimalFloorSnapshots)
        Check("manual discount_fee and Ceiling percentage discount", AddressOf ExistingDiscounts)
        Check("receipt tax aggregation is authoritative over group estimates", AddressOf ReceiptVsGroups)
        Check("income and expense signs remain separate", AddressOf IncomeAndExpenseSigns)
        Check("preview tax settings leave receipt and saved totals unchanged", AddressOf PreviewDoesNotMutateReceipt)
        Check("negative half_up tax and mixed refunds", AddressOf Refunds)
        Check("user allocation is a rounded estimate", AddressOf UserEstimates)
        Check("person filters preserve empty exact and partial selections with and without allocation", AddressOf PersonStatisticsTests.FilterMatrix)
        Check("person allocation preserves signed rounding for two and three people across tax modes and rates", AddressOf PersonStatisticsTests.AllocationRounding)
        Check("person statistics round within each group and retain empty groups and metadata", AddressOf PersonStatisticsTests.GroupBoundaries)
        Check("person matching ignores order and currently compares User references", AddressOf PersonStatisticsTests.UserReferenceMatching)
        Check("balance estimation uses receipt mode", AddressOf BalanceEstimation)
        Check("each detail keeps independent mixed taxable and excluded rates", AddressOf DetailTaxTests.MixedRates)
        Check("fractional shares remain exact at integer boundaries", AddressOf DetailTaxTests.ExactShares)
        Check("percentage precision and extreme intermediates stay exact", AddressOf DetailTaxTests.Precision)
        Check("merging group notes preserves null empty whitespace and duplicates", AddressOf DetailTaxTests.Notes)
        Return Finish()
    End Function

    Private Sub CheckJsonExpectations()
        Using document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tax-rounding-cases.json")))
            For Each testCase In document.RootElement.GetProperty("cases").EnumerateArray()
                Dim item = testCase
                Check(item.GetProperty("id").GetString(),
                    Sub()
                        Dim receipt = MakeReceipt(item.GetProperty("rounding_mode").GetString())
                        receipt.TaxType = item.GetProperty("tax_type").GetInt32()
                        Dim groups As New Dictionary(Of String, ReceiptGroup)
                        For Each line In item.GetProperty("lines").EnumerateArray()
                            Dim key = line.GetProperty("group").GetString()
                            If Not groups.ContainsKey(key) Then
                                groups.Add(key, Group(line.GetProperty("tax_rate_percent").GetInt32()))
                                receipt.Groups.Add(groups(key))
                            End If
                            Dim detailLine = Detail(line.GetProperty("net_amount_yen").GetInt32(), line.GetProperty("taxable").GetBoolean())
                            If detailLine.TaxRate.IsTaxable Then detailLine.TaxRate = New TaxRate With {.Code = line.GetProperty("tax_rate_percent").GetInt32(), .Name = line.GetProperty("tax_rate_percent").GetInt32().ToString()}
                            groups(key).Details.Add(detailLine)
                        Next
                        Equal(item.GetProperty("expected_total_yen").GetInt32(), receipt.Total)
                    End Sub)
            Next
        End Using
    End Sub

    Private Sub ModesAreValidated()
        Dim receipt As New Receipt
        Dim shop As New Shop
        Equal("floor", receipt.TaxRoundingMode)
        Equal("floor", shop.TaxRoundingMode)
        receipt.TaxRoundingMode = "half_up"
        shop.TaxRoundingMode = "half_up"
        For Each mode In New String() {Nothing, "", "HALF_UP", "truncate"}
            Dim invalid = mode
            Throws(Of ArgumentException)(Sub() receipt.TaxRoundingMode = invalid)
            Throws(Of ArgumentException)(Sub() shop.TaxRoundingMode = invalid)
            Throws(Of ArgumentException)(Sub() receipt.GetTotal(1, invalid))
            Equal("half_up", receipt.TaxRoundingMode)
            Equal("half_up", shop.TaxRoundingMode)
        Next
        Equal(0, MakeReceipt("half_up").Total)
    End Sub

    Private Sub DecimalFloorSnapshots()
        ' 新旧共通の正確な十進期待値。旧Doubleの-111円は-110円へ修正する。
        Dim amounts = {-1000, -575, -100, -50, -25, -5, -1, 0, 1, 5, 19, 25, 50, 100, 575, 1000}
        Dim expected8 = {-1080, -621, -108, -54, -27, -6, -2, 0, 1, 5, 20, 27, 54, 108, 621, 1080}
        Dim expected10 = {-1100, -633, -110, -55, -28, -6, -2, 0, 1, 5, 20, 27, 55, 110, 632, 1100}
        For index = 0 To amounts.Length - 1
            Equal(expected8(index), MakeReceipt("floor", Group(8, Detail(amounts(index)))).Total, "8% " & amounts(index))
            Equal(expected10(index), MakeReceipt("floor", Group(10, Detail(amounts(index)))).Total, "10% " & amounts(index))
        Next
        ' 対象外を独立集計すると、参考額だけでなく会計合計も変わる境界を固定する。
        Dim excludedBoundary = MakeReceipt("floor", Group(10, Detail(-100), Detail(1000, False)))
        Equal(890, excludedBoundary.Total, "Mixed negative taxable and excluded amounts")
        Equal(-110, excludedBoundary.Incomes)
        Equal(1000, excludedBoundary.Expenses)
        Dim mixed = MakeReceipt("floor", Group(8, Detail(19), Detail(3, False)), Group(10, Detail(19)), Group(10, Detail(19)))
        Equal(64, mixed.Total)
        mixed.TaxType = 0
        Equal(60, mixed.Total)
    End Sub

    Private Sub ExistingDiscounts()
        ' 0.14%値引の二進誤差を修正し、値引14円・差引9986円にする。
        Equal(9986, MakeReceipt("floor", Group(10, Detail(10000, taxable:=False, discountRate:=0.14D))).Total)
        ' The manual three-yen discount remains an amount, not an automatic 3/103 rule.
        For Each mode In New String() {"floor", "half_up"}
            Equal(108, MakeReceipt(mode, Group(8, Detail(103, discountFee:=3))).Total)
        Next
        ' (103 - 3) * 2 - Ceiling(200 * 2.6 / 100) = 194.
        Equal(209, MakeReceipt("floor", Group(8, Detail(103, quantity:=2, discountFee:=3, discountRate:=2.6D))).Total)
        Equal(210, MakeReceipt("half_up", Group(8, Detail(103, quantity:=2, discountFee:=3, discountRate:=2.6D))).Total)
        Equal(-211, MakeReceipt("floor", Group(8, Detail(-103, quantity:=2, discountFee:=-3, discountRate:=2.6D))).Total)
    End Sub

    Private Sub ReceiptVsGroups()
        Dim receipt = MakeReceipt("floor", Group(10, Detail(19)), Group(10, Detail(19)))
        Equal(41, receipt.Total)
        Equal(40, receipt.Groups.Sum(Function(g) Calculation.GroupCalculatedFee(receipt, g)))
        Equal(40, receipt.GetGroupStatisticsList(New List(Of User), False, False).Sum(Function(s) s.Fee))
        receipt.TaxRoundingMode = "half_up"
        Equal(42, receipt.Total)
        Equal(42, receipt.Groups.Sum(Function(g) Calculation.GroupCalculatedFee(receipt, g)))
        Equal(21, Calculation.DetailCalculatedFee(receipt, receipt.Groups(0), receipt.Groups(0).Details(0)))
        ' Two rounded group estimates must not replace one tax-rate aggregate.
        receipt = MakeReceipt("half_up", Group(10, Detail(15)), Group(10, Detail(15)))
        Equal(33, receipt.Total)
        Equal(34, receipt.Groups.Sum(Function(g) Calculation.GroupCalculatedFee(receipt, g)))
        Equal(34, receipt.GetGroupStatisticsList(New List(Of User), False, False).Sum(Function(s) s.Fee))
        Dim nonTax = Detail(15, False)
        receipt.Groups(0).Details.Add(nonTax)
        Equal(15, Calculation.DetailCalculatedFee(receipt, receipt.Groups(0), nonTax))
        Equal(48, receipt.Total)
    End Sub

    Private Sub IncomeAndExpenseSigns()
        Dim receipt = MakeReceipt("floor", Group(10, Detail(5), Detail(-1)))
        Equal(4, receipt.Total)
        Equal(5, receipt.Expenses)
        Equal(-2, receipt.Incomes)
        receipt.TaxRoundingMode = "half_up"
        Equal(4, receipt.Total)
        Equal(6, receipt.Expenses)
        Equal(-1, receipt.Incomes)
        receipt.TaxType = 0
        Equal(4, receipt.Total)
        Equal(5, receipt.Expenses)
        Equal(-1, receipt.Incomes)
    End Sub

    Private Sub PreviewDoesNotMutateReceipt()
        ' 10% is split across groups; preview must aggregate 19 + 19 before rounding.
        ' 8%, non-taxable items and refunds still use the same calculation as saved totals.
        For Each mode In New String() {"floor", "half_up"}
            For Each taxType In New Integer() {0, 1}
                Dim receipt = MakeReceipt(mode, Group(10, Detail(19)), Group(10, Detail(19)),
                                          Group(8, Detail(19), Detail(-5), Detail(3, False)))
                receipt.TaxType = taxType
                Dim originalTotal = receipt.Total
                Dim originalIncomes = receipt.Incomes
                Dim originalExpenses = receipt.Expenses
                Equal(If(taxType = 0, 55, If(mode = "floor", 59, 60)), originalTotal)
                Equal(If(taxType = 0, -5, If(mode = "floor", -6, -5)), originalIncomes)
                Equal(If(taxType = 0, 60, If(mode = "floor", 64, 66)), originalExpenses)

                For Each previewMode In New String() {"floor", "half_up"}
                    Equal(55, receipt.GetTotal(0, previewMode))
                    Equal(If(previewMode = "floor", 59, 60), receipt.GetTotal(1, previewMode))
                    Equal(taxType, receipt.TaxType)
                    Equal(mode, receipt.TaxRoundingMode)
                    Equal(originalTotal, receipt.Total)
                    Equal(originalIncomes, receipt.Incomes)
                    Equal(originalExpenses, receipt.Expenses)
                Next
            Next
        Next
    End Sub

    Private Sub Refunds()
        Equal(-6, MakeReceipt("half_up", Group(10, Detail(-5))).Total)
        Equal(-110, MakeReceipt("half_up", Group(10, Detail(-100))).Total)
        Equal(6, MakeReceipt("half_up", Group(10, Detail(5))).Total)
        Equal(15, MakeReceipt("half_up", Group(10, Detail(19), Detail(-5))).Total)
        Equal(-15, MakeReceipt("half_up", Group(10, Detail(-19), Detail(5))).Total)
    End Sub

    Private Sub UserEstimates()
        Dim first = New User With {.Code = 1}
        Dim second = New User With {.Code = 2}
        Dim line = Detail(5)
        line.Users.AddRange({first, second})
        Dim receipt = MakeReceipt("half_up", Group(10, line))
        Dim users As New List(Of User) From {first}
        Equal(3, receipt.GetGroupStatisticsList(users, False, True).Single().Fee)
        Equal(6, receipt.Total)
        Equal(0, receipt.GetGroupStatisticsList(users, True, True).Single().Fee)
        receipt.TaxType = 0
        Equal(3, receipt.GetGroupStatisticsList(users, False, True).Single().Fee)
        Equal(5, receipt.Total)
        receipt.TaxRoundingMode = "floor"
        Equal(2, receipt.GetGroupStatisticsList(users, False, True).Single().Fee)
    End Sub

    Private Sub BalanceEstimation()
        Dim receipt = MakeReceipt("half_up", Group(10, Detail(19), Detail(19)))
        Dim payment = receipt.PaymentClassification
        Dim data As New DataAccess With {
            .LastBalance = New Balance With {
                .Date = receipt.Date.AddDays(-1),
                .Groups = New List(Of BalanceGroup) From {New BalanceGroup With {.PaymentClassification = payment, .Balance = 1000}}
            }
        }
        data.Receipts.Add(receipt)
        Calculation.DataAccess = data
        Equal(958, Calculation.AutoBalance(receipt.Date, payment).Value)
        receipt.TaxRoundingMode = "floor"
        Equal(959, Calculation.AutoBalance(receipt.Date, payment).Value)
        Calculation.DataAccess = New DataAccess
        Equal(False, Calculation.AutoBalance(receipt.Date, payment).HasValue)
    End Sub
End Module
