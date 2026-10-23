Imports System.Globalization
Imports System.Numerics

Friend Module DetailTaxTests
    Public Sub MixedRates()
        Dim lines = {Detail(19), Detail(19), Detail(19), Detail(10, False)}
        lines(0).TaxRate = New TaxRate With {.Code = 4, .Name = "8"}
        Dim group As New ReceiptGroup With {.GroupClassification = New GroupClassification With {.Code = 1}, .Details = lines.ToList()}
        Dim receipt = MakeReceipt("floor", group)
        Equal(71, receipt.Total)
        Equal(71, Calculation.GroupCalculatedFee(receipt, group))
        receipt.TaxRoundingMode = "half_up"
        Equal(73, receipt.Total)
        receipt.TaxType = 0
        Equal(67, receipt.Total)
        Equal(4, lines(0).TaxRate.Code)
        Equal("out_of_scope", lines(3).TaxRate.TaxTreatment)
        lines(0).TaxRate = Nothing
        Throws(Of InvalidOperationException)(Sub() receipt.GetTotal(1, "floor"))
    End Sub

    Public Sub ExactShares()
        Dim people = {New User With {.Code = 1}, New User With {.Code = 2}, New User With {.Code = 3}}
        For Each sign In {1, -1}
            For Each mode In {"floor", "half_up"}
                Dim lines = {Detail(sign, False), Detail(sign, False), Detail(sign, False)}
                For Each line In lines
                    line.Users.AddRange(people)
                Next
                Dim receipt = MakeReceipt(mode, Group(10, lines))
                Equal(sign, receipt.GetGroupStatisticsList(New List(Of User) From {people(0)}, False, True).Single().Fee)
                receipt.TaxType = 0
                Equal(sign, receipt.GetGroupStatisticsList(New List(Of User) From {people(0)}, False, True).Single().Fee)
            Next
        Next
    End Sub

    Public Sub Precision()
        Dim line = Detail(1000000000, False, discountRate:=0.1234567890123456789012345678D)
        Equal(998765432, MakeReceipt("floor", Group(10, line)).Total)
        Equal(0, MakeReceipt("floor", Group(10, Detail(Integer.MaxValue, False, Integer.MaxValue, discountRate:=100D))).Total)
        Equal("0.1", ExactMoney.RateText(ExactMoney.ParseRate("0.10000000000000000000000000001")))
    End Sub

    ' 確定税額の1円配分・正負相殺・ゼロ合計・大きな中間値を固定期待値で検証する。
    Public Sub ReceiptTaxAllocations()
        Dim cases = {
            Tuple.Create("floor", New Integer() {19, 19}, New Integer() {21, 20}),
            Tuple.Create("half_up", New Integer() {19, 19}, New Integer() {21, 21}),
            Tuple.Create("half_up", New Integer() {15, 15}, New Integer() {17, 16}),
            Tuple.Create("floor", New Integer() {-15, -15}, New Integer() {-16, -17}),
            Tuple.Create("floor", New Integer() {5, -1}, New Integer() {5, -1}),
            Tuple.Create("half_up", New Integer() {5, -1}, New Integer() {5, -1}),
            Tuple.Create("floor", New Integer() {5, -5}, New Integer() {6, -6}),
            Tuple.Create("floor", New Integer() {100, -101}, New Integer() {110, -112}),
            Tuple.Create("half_up", New Integer() {100, -101}, New Integer() {110, -111}),
            Tuple.Create("floor", New Integer() {0, 0}, New Integer() {0, 0})}
        For Each test In cases
            Dim amounts = test.Item2.Select(Function(n) New BigInteger(n)).ToArray()
            Dim rates = Enumerable.Repeat(Of Integer?)(10, amounts.Length).ToArray()
            Dim result = ExactMoney.ReceiptAmounts(amounts, rates, 1, test.Item1)
            Equal(String.Join(",", test.Item3), String.Join(",", result))
            Equal(String.Join(",", test.Item2), String.Join(",", ExactMoney.ReceiptAmounts(amounts, rates, 0, test.Item1)))
        Next
        Dim large = ExactMoney.ReceiptAmounts({New BigInteger(Integer.MaxValue), New BigInteger(Integer.MinValue)}, {New Integer?(10), New Integer?(10)}, 1, "floor")
        Equal("2362232011,-2362232013", String.Join(",", large))
        Dim untaxed = ExactMoney.ReceiptAmounts({New BigInteger(19), New BigInteger(-19)}, {CType(Nothing, Integer?), New Integer?(0)}, 1, "floor")
        Equal("19,-19", String.Join(",", untaxed))
    End Sub

    ' 税額確定後の分類抽出・対象者抽出・人数割と、別レシート間の独立性を検証する。
    Public Sub ReceiptTaxBeforeFilters()
        Dim a As New User With {.Code = 1}
        Dim b As New User With {.Code = 2}
        Dim first = Detail(19)
        first.Users.AddRange({a, b})
        Dim second = Detail(19)
        second.TaxRate = New TaxRate With {.Code = 99, .Name = "10"}
        second.Users.Add(b)
        Dim receipt = MakeReceipt("floor", Group(10, first), Group(10, second))
        Dim selected As New List(Of User) From {a}
        Equal(41, receipt.Total)
        Equal(21, Calculation.GroupCalculatedFee(receipt, receipt.Groups(0)))
        Equal(20, Calculation.GroupCalculatedFee(receipt, receipt.Groups(1)))
        Equal(21, Calculation.DetailCalculatedFee(receipt, receipt.Groups(0), first))
        Equal("21,0", String.Join(",", receipt.GetGroupStatisticsList(selected, False, False).Select(Function(row) row.Fee)))
        Equal("10,0", String.Join(",", receipt.GetGroupStatisticsList(selected, False, True).Select(Function(row) row.Fee)))
        Equal(41, receipt.Incomes + receipt.Expenses)
        Dim other = MakeReceipt("floor", Group(10, Detail(19)))
        Equal(40, other.Total + MakeReceipt("floor", Group(10, Detail(19))).Total)
        Equal(19, first.Fee)
        Equal(19, second.Fee)
        Equal("floor", receipt.TaxRoundingMode)
    End Sub

    Public Sub Notes()
        Equal(Nothing, Calculation.MergeGroupNotes(New List(Of String) From {Nothing, Nothing}))
        Equal("", Calculation.MergeGroupNotes(New List(Of String) From {Nothing, ""}))
        Equal("same ", Calculation.MergeGroupNotes(New List(Of String) From {"same "}))
        Equal("【統合元 1】" & vbLf & "same " & vbLf & "【統合元 2】" & vbLf & "same ", Calculation.MergeGroupNotes(New List(Of String) From {"same ", "same "}))
    End Sub
End Module
