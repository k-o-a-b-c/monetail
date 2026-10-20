Imports System.Collections.Generic

Friend Module IntegerExpressionCases
    Public Function PriorityCases() As KeyValuePair(Of String, Integer)()
        Return {Item("100+50-20", 130), Item("100+20*3", 160), Item("2*3+4*5", 26),
            Item("100-20*3+4", 44), Item("2*3*4", 24), Item("10-3-2", 5),
            Item("0*123+7", 7), Item("5*0-9", -9), Item("2147483647+2*-1", 2147483645)}
    End Function

    Public Function SignedCases() As KeyValuePair(Of String, Integer)()
        Return {Item("-10*2+5", -15), Item("2*-3", -6), Item("-2*-3", 6),
            Item("1--2", 3), Item("1+-2", -1), Item("+2*+3", 6), Item("1++2", 3),
            Item(" - 2 * 3 + 10 ", 4), Item(vbTab & "10" & vbCrLf & "+ 2*3", 16),
            Item("00012+003", 15), Item("2147483647", Integer.MaxValue),
            Item("-2147483648", Integer.MinValue), Item("2*-1073741824", Integer.MinValue),
            Item("-2147483648+2147483647", -1), Item("-0", 0)}
    End Function

    Public Function InvalidCases() As KeyValuePair(Of String, Integer)()
        Return {Item(Nothing, 0), Item("", 0), Item(" ", 0), Item("abc", 0), Item("1.5", 0),
            Item("1,000", 0), Item("(1+2)*3", 0), Item("6/2", 0), Item("2^3", 0),
            Item("2×3", 0), Item("２*３", 0), Item("1 2", 0), Item("1+", 0),
            Item("1*", 0), Item("*2", 0), Item("2**3", 0), Item("--2", 0),
            Item("1+++2", 0), Item("+", 0), Item("1+abc", 0), Item("1+2 trailing", 0)}
    End Function

    Public Function OverflowCases() As KeyValuePair(Of String, Integer)()
        Return {Item("2147483648", 0), Item("-2147483649", 0), Item("9999999999999999999999999999", 0),
            Item("2147483647+1", 0), Item("-2147483648-1", 0), Item("2147483647*2", 0),
            Item("-2147483648*-1", 0), Item("50000*50000", 0), Item("2147483647+1-1", 0),
            Item("-2147483648-1+1", 0), Item("2147483647*2-2147483647", 0)}
    End Function

    Public Function MoneyUiCases() As KeyValuePair(Of String, Integer)()
        Return {Item("100+20*3", 160), Item("2*3+4*5", 26), Item("-10*2+5", -15),
            Item("", 0), Item("abc", 0), Item("1.5", 0), Item("1+", 0), Item("50000*50000", 0)}
    End Function

    Private Function Item(text As String, value As Integer) As KeyValuePair(Of String, Integer)
        Return New KeyValuePair(Of String, Integer)(text, value)
    End Function
End Module
