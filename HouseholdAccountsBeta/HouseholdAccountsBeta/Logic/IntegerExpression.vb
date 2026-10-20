Public NotInheritable Class IntegerExpression
    Private Sub New()
    End Sub

    ' 整数の加減算・乗算を評価する。空欄、不正な式、途中の桁あふれは0に補正する。
    Public Shared Function EvaluateOrZero(text As String) As Integer
        If String.IsNullOrWhiteSpace(text) Then Return 0
        Dim index As Integer = 0
        Dim result As Integer
        If Not ReadProduct(text, index, result) Then Return 0
        While index < text.Length
            Dim operation As Char = text(index)
            If operation <> "+"c AndAlso operation <> "-"c Then Return 0
            index += 1
            Dim term As Integer
            If Not ReadProduct(text, index, term) Then Return 0
            Dim combined As Long = If(operation = "+"c, CLng(result) + term, CLng(result) - term)
            If Not InRange(combined) Then Return 0
            result = CInt(combined)
        End While
        Return result
    End Function

    ' 乗算のまとまりを先に計算し、加減算側へ渡す。
    Private Shared Function ReadProduct(text As String, ByRef index As Integer, ByRef result As Integer) As Boolean
        If Not ReadInteger(text, index, result) Then Return False
        SkipWhitespace(text, index)
        While index < text.Length AndAlso text(index) = "*"c
            index += 1
            Dim factor As Integer
            If Not ReadInteger(text, index, factor) Then Return False
            ' 両辺をInt32に制限しているため、積はLongの範囲内で計算できる。
            Dim product As Long = CLng(result) * factor
            If Not InRange(product) Then Return False
            result = CInt(product)
            SkipWhitespace(text, index)
        End While
        Return True
    End Function

    Private Shared Function ReadInteger(text As String, ByRef index As Integer, ByRef result As Integer) As Boolean
        SkipWhitespace(text, index)
        Dim negative As Boolean = False
        If index < text.Length AndAlso (text(index) = "+"c OrElse text(index) = "-"c) Then
            negative = text(index) = "-"c
            index += 1
            SkipWhitespace(text, index)
        End If
        Dim start As Integer = index
        Dim magnitude As Long = 0
        ' MinValueの絶対値はInt32に収まらないため、符号適用前はLongで読む。
        Dim limit As Long = If(negative, 2147483648L, Integer.MaxValue)
        While index < text.Length AndAlso text(index) >= "0"c AndAlso text(index) <= "9"c
            magnitude = magnitude * 10 + (AscW(text(index)) - AscW("0"c))
            If magnitude > limit Then Return False
            index += 1
        End While
        If index = start Then Return False
        result = CInt(If(negative, -magnitude, magnitude))
        Return True
    End Function

    Private Shared Sub SkipWhitespace(text As String, ByRef index As Integer)
        While index < text.Length AndAlso Char.IsWhiteSpace(text(index))
            index += 1
        End While
    End Sub

    Private Shared Function InRange(value As Long) As Boolean
        Return value >= Integer.MinValue AndAlso value <= Integer.MaxValue
    End Function
End Class
