Imports System.Collections.Generic
Imports System.Globalization

Friend Module IntegerExpressionTests
    Public Sub Priority()
        AssertCases(IntegerExpressionCases.PriorityCases())
    End Sub

    Public Sub SignedAndBoundaries()
        AssertCases(IntegerExpressionCases.SignedCases())
    End Sub

    Public Sub InvalidInputs()
        AssertCases(IntegerExpressionCases.InvalidCases())
    End Sub

    Public Sub Overflow()
        AssertCases(IntegerExpressionCases.OverflowCases())
    End Sub

    Private Sub AssertCases(cases As IEnumerable(Of KeyValuePair(Of String, Integer)))
        Dim previousCulture = CultureInfo.CurrentCulture
        Try
            For Each name In New String() {"en-US", "de-DE"}
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name)
                For Each item As KeyValuePair(Of String, Integer) In cases
                    Equal(item.Value, IntegerExpression.EvaluateOrZero(item.Key), name & " expression=" & item.Key)
                Next
            Next
        Finally
            CultureInfo.CurrentCulture = previousCulture
        End Try
    End Sub
End Module
