Friend Module DecimalDiscountTests
    Public Sub IntegerDiscountBoundaries()
        Equal(34, CInt(Math.Ceiling(3000R * 1.1R / 100)), "Old Double boundary")
        AssertNet(3000, 1.1D, 2967)
        AssertNet(1500, 2.2D, 1467)
        AssertNet(1503, 1.1D, 2967, quantity:=2, discountFee:=3)
        AssertNet(100, 2.5D, 97)
        AssertNet(-100, 2.5D, -98)
        AssertNet(-3000, 1.1D, -2967)
        AssertNet(3000, 0D, 3000)
        AssertNet(3000, 100D, 0)
        AssertNet(100, 125D, -25)
        AssertNet(3000, 1.001D, 2969)
        AssertNet(Integer.MaxValue, 100D, 0, quantity:=Integer.MaxValue, discountFee:=Integer.MinValue)
    End Sub

    Public Sub TaxAndAggregation()
        For Each mode In {"floor", "half_up"}
            For Each rate In {8, 10}
                Dim receipt = MakeReceipt(mode, Group(rate, Detail(3000, discountRate:=1.1D)))
                Dim expected As Integer = If(rate = 8, 3204, If(mode = "floor", 3263, 3264))
                receipt.SavedTotal = expected - 1
                Equal(expected, receipt.Total)
                Equal(expected, receipt.Expenses)
                Equal(0, receipt.Incomes)
                Equal(expected, Calculation.GroupCalculatedFee(receipt, receipt.Groups(0)))
                Equal(expected, Calculation.DetailCalculatedFee(receipt, receipt.Groups(0), receipt.Groups(0).Details(0)))
                Equal(expected, receipt.GetGroupStatisticsList(New List(Of User), False, False)(0).Fee)
                Equal(2967, receipt.GetTotal(0, mode))
                Equal(CLng(expected - 1), receipt.SavedTotal.Value)
            Next
            ' 率値引は明細ごとに整数化し、その後に税率別で合算する。
            Dim split = MakeReceipt(mode, Group(10, Detail(100, discountRate:=2.5D)), Group(10, Detail(100, discountRate:=2.5D)))
            Equal(194, split.GetTotal(0, mode))
            Equal(213, split.Total)
        Next
    End Sub

    Private Sub AssertNet(price As Integer, rate As Decimal, expected As Integer,
                          Optional quantity As Integer = 1, Optional discountFee As Integer = 0)
        For Each mode In {"floor", "half_up"}
            Dim receipt = MakeReceipt(mode, Group(10, Detail(price, quantity:=quantity, discountFee:=discountFee, discountRate:=rate)))
            receipt.TaxType = 0
            Equal(expected, receipt.Total)
            Equal(expected, Calculation.GroupCalculatedFee(receipt, receipt.Groups(0)))
            Equal(expected, Calculation.DetailCalculatedFee(receipt, receipt.Groups(0), receipt.Groups(0).Details(0)))
            Equal(expected, receipt.GetGroupStatisticsList(New List(Of User), False, False)(0).Fee)
        Next
    End Sub
End Module
