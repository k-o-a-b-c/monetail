Friend Module DecimalTaxTests
    Public Sub TaxBoundaries()
        Equal(-111, CInt(Math.Floor(-100R * (1 + 10 / 100))), "Old Double boundary")
        Equal(-56, CInt(Math.Floor(-50R * (1 + 10 / 100))), "Old Double boundary")
        For Each mode In {"floor", "half_up"}
            Equal(-110D, Calculation.TaxRateSubtotal(-100D, 0D, 10, 1, mode))
            Equal(-55D, Calculation.TaxRateSubtotal(-50D, 0D, 10, 1, mode))
            Equal(110D, Calculation.TaxRateSubtotal(100D, 0D, 10, 1, mode))
            Equal(890D, Calculation.TaxRateSubtotal(825D, -1D, 8, 1, mode))
            Equal(-100D, Calculation.TaxRateSubtotal(-100D, 0D, 10, 0, mode))
            Equal(9907919180215092D, Calculation.TaxRateSubtotal(9007199254740993D, 0D, 10, 1, mode))
            Equal(If(mode = "floor", 5D, 6D), Calculation.TaxRateSubtotal(5D, 0D, 10, 1, mode))
            Equal(-6D, Calculation.TaxRateSubtotal(-5D, 0D, 10, 1, mode))
            Equal(If(mode = "floor", -9907919180215093D, -9907919180215092D),
                  Calculation.TaxRateSubtotal(-9007199254740993D, 0D, 10, 1, mode))
        Next
    End Sub

    Public Sub ReceiptAndReferenceTotals()
        For Each mode In {"floor", "half_up"}
            Dim receipt = MakeReceipt(mode, Group(10, Detail(-50)), Group(10, Detail(-50)))
            receipt.SavedTotal = -111
            Equal(-110, receipt.Total)
            Equal(-110, receipt.Incomes)
            Equal(0, receipt.Expenses)
            Equal(-111L, receipt.SavedTotal.Value)
            For Each receiptGroup As ReceiptGroup In receipt.Groups
                Equal(-55, Calculation.GroupCalculatedFee(receipt, receiptGroup))
                Equal(-55, Calculation.DetailCalculatedFee(receipt, receiptGroup, receiptGroup.Details(0)))
            Next
            Dim statistics = receipt.GetGroupStatisticsList(New List(Of User), False, False)
            Equal(-55, statistics(0).Fee)
            Equal(-55, statistics(1).Fee)
            Equal(-100, receipt.GetTotal(0, mode))
            Equal(1, receipt.TaxType)
            Equal(-111L, receipt.SavedTotal.Value)
            Dim mixed = MakeReceipt(mode, Group(10, Detail(-100)), Group(10, Detail(19), Detail(19)), Group(8, Detail(100)))
            Equal(If(mode = "floor", 39, 40), mixed.Total)
            Equal(If(mode = "floor", 149, 150), mixed.Expenses)
            Equal(-110, mixed.Incomes)
        Next
    End Sub
End Module
