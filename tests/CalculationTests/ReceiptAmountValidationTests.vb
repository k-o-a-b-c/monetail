Friend Module ReceiptAmountValidationTests
    Public Sub AmountRanges()
        For Each mode In New String() {"floor", "half_up"}
            For Each taxType In New Integer() {0, 1}
                Dim maximum = MakeReceipt(mode, Group(10, Detail(Integer.MaxValue, taxable:=False)))
                Dim minimum = MakeReceipt(mode, Group(10, Detail(Integer.MinValue, taxable:=False)))
                Equal(True, ReceiptAmountValidation.IsInRange(maximum, taxType, mode))
                Equal(True, ReceiptAmountValidation.IsInRange(minimum, taxType, mode))
                Equal(False, ReceiptAmountValidation.IsInRange(MakeReceipt(mode, Group(10,
                    Detail(2000000000, taxable:=False, quantity:=2))), taxType, mode))
                Equal(False, ReceiptAmountValidation.IsInRange(MakeReceipt(mode, Group(10,
                    Detail(-2000000000, taxable:=False, quantity:=2))), taxType, mode))
                Equal(False, ReceiptAmountValidation.IsInRange(MakeReceipt(mode, Group(10,
                    Detail(Integer.MaxValue, taxable:=False, discountFee:=-1))), taxType, mode))
                ' 相殺後の合計は範囲内でも、収入・支出のどちらかがあふれる例。
                Dim expenses = MakeReceipt(mode, Group(10, Detail(1500000000, False), Detail(1500000000, False), Detail(-2000000000, False)))
                Equal(1000000000, expenses.GetTotal(taxType, mode))
                Equal(False, ReceiptAmountValidation.IsInRange(expenses, taxType, mode))
                Dim incomes = MakeReceipt(mode, Group(10, Detail(-1500000000, False), Detail(-1500000000, False), Detail(2000000000, False)))
                Equal(-1000000000, incomes.GetTotal(taxType, mode))
                Equal(False, ReceiptAmountValidation.IsInRange(incomes, taxType, mode))
                Equal(True, ReceiptAmountValidation.IsInRange(MakeReceipt(mode, Group(10,
                    Detail(2000000000, taxable:=False, quantity:=2, discountRate:=100))), taxType, mode))
            Next
        Next
    End Sub

    Public Sub CandidateIsReadOnly()
        Dim line = Detail(20, taxable:=False, quantity:=2)
        Dim group = ReceiptFixtures.Group(10, line)
        Dim receipt = MakeReceipt("floor", group)
        Dim tooLarge = Detail(2000000000, taxable:=False, quantity:=2)
        Equal(False, ReceiptAmountValidation.CanApplyDetail(receipt, group, line, tooLarge, 0, "half_up"))
        Equal(False, ReceiptAmountValidation.CanApplyDetail(receipt, group, Nothing, tooLarge, 0, "half_up"))
        Equal(True, ReceiptAmountValidation.CanApplyDetail(receipt, group, line, Detail(30, False), 0, "half_up"))
        Equal(1, receipt.Groups.Count)
        Equal(True, receipt.Groups(0) Is group)
        Equal(1, group.Details.Count)
        Equal(True, group.Details(0) Is line)
        Equal(20, line.Fee)
        Equal(2, line.Quantity)
        Equal(1, receipt.TaxType)
        Equal("floor", receipt.TaxRoundingMode)
        Equal(40, receipt.Total)
        Equal(2000000000, tooLarge.Fee)
    End Sub

    Public Sub SelectedTaxSettings()
        Dim receipt = MakeReceipt("floor", Group(10, Detail(2000000000)))
        receipt.TaxType = 0
        For Each mode In New String() {"floor", "half_up"}
            Equal(True, ReceiptAmountValidation.IsInRange(receipt, 0, mode))
            Equal(False, ReceiptAmountValidation.IsInRange(receipt, 1, mode))
        Next
        Equal(0, receipt.TaxType)
        Equal("floor", receipt.TaxRoundingMode)
        Equal(2000000000, receipt.Total)
    End Sub
End Module
