Imports System.Linq

Friend Module TaxRateSelectionTests
    Public Sub DateAndClassification()
        Dim rates = {New TaxRate With {.Code = 40, .Name = "8"}, New TaxRate With {.Code = 50, .Name = "10"}}
        For Each reversed In {False, True}
            Dim candidates = If(reversed, rates.Reverse().ToArray(), rates)
            For Each reduced In {False, True}
                Equal(True, Object.ReferenceEquals(rates(0), TaxRateSelection.GetDefaultRate(New Date(2019, 9, 30, 23, 59, 59), reduced, candidates)))
                Equal(True, Object.ReferenceEquals(rates(If(reduced, 0, 1)), TaxRateSelection.GetDefaultRate(New Date(2019, 10, 1), reduced, candidates)))
                Equal(True, Object.ReferenceEquals(rates(If(reduced, 0, 1)), TaxRateSelection.GetDefaultRate(New Date(2026, 10, 23), reduced, candidates)))
            Next
        Next
    End Sub

    Public Sub MatchesRateRatherThanCode()
        Dim rates = {New TaxRate With {.Code = 10, .Name = "0"}, New TaxRate With {.Code = 8, .Name = "5"},
                     New TaxRate With {.Code = 81, .Name = "8"}, New TaxRate With {.Code = 101, .Name = "10"}}
        Equal(101, TaxRateSelection.GetDefaultRate(New Date(2026, 1, 1), False, rates).Code)
        Equal(81, TaxRateSelection.GetDefaultRate(New Date(2026, 1, 1), True, rates).Code)
        Equal("0,5,8,10", String.Join(",", rates.Select(Function(rate) rate.Name)))
    End Sub

    Public Sub MissingRate()
        Dim rates = {New TaxRate With {.Code = 4, .Name = "8"}}
        Equal(Of TaxRate)(Nothing, TaxRateSelection.GetDefaultRate(New Date(2026, 1, 1), False, rates))
        Equal(Of TaxRate)(Nothing, TaxRateSelection.GetDefaultRate(New Date(2019, 9, 30), True, New TaxRate() {}))
        Throws(Of ArgumentNullException)(Sub() TaxRateSelection.GetDefaultRate(Date.Today, False, Nothing))
    End Sub
End Module
