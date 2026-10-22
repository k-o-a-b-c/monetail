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
        Throws(Of FormatException)(Sub() ExactMoney.ParseRate("0.12345678901234567890123456789"))
    End Sub

    Public Sub Notes()
        Equal(Nothing, Calculation.MergeGroupNotes(New List(Of String) From {Nothing, Nothing}))
        Equal("", Calculation.MergeGroupNotes(New List(Of String) From {Nothing, ""}))
        Equal("same ", Calculation.MergeGroupNotes(New List(Of String) From {"same "}))
        Equal("【統合元 1】" & vbLf & "same " & vbLf & "【統合元 2】" & vbLf & "same ", Calculation.MergeGroupNotes(New List(Of String) From {"same ", "same "}))
    End Sub
End Module
