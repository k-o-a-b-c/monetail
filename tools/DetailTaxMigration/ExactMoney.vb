Imports System.Globalization
Imports System.Numerics
Imports System.Text.RegularExpressions

' 移行と将来の会計処理で使う十進値。途中の乗除算も暗黙に丸めない。
Public NotInheritable Class ExactMoney
    Private Sub New()
    End Sub

    Public Shared Function ParseRate(text As String) As Decimal
        If text Is Nothing OrElse text.Length > 1024 Then Throw New FormatException("割引率の桁数が不正です。")
        Dim match = Regex.Match(text, "\A([+-]?)([0-9]+)(?:\.([0-9]+))?(?:[eE]([+-]?[0-9]+))?\z")
        If Not match.Success Then Throw New FormatException("割引率は十進数で指定してください。")
        Dim scale = match.Groups(3).Value.Length
        Dim exponent As Integer
        If match.Groups(4).Success AndAlso
           (Not Integer.TryParse(match.Groups(4).Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, exponent) OrElse
            Math.Abs(CLng(exponent)) > 1000) Then Throw New FormatException("割引率の指数が範囲外です。")
        scale -= exponent
        Dim coefficient = BigInteger.Parse(match.Groups(2).Value & match.Groups(3).Value, CultureInfo.InvariantCulture)
        If match.Groups(1).Value = "-" Then coefficient = -coefficient
        If scale < 0 Then
            coefficient *= BigInteger.Pow(10, -scale)
            scale = 0
        End If
        While scale > 0 AndAlso coefficient Mod 10 = BigInteger.Zero
            coefficient /= 10
            scale -= 1
        End While
        If scale > 28 OrElse BigInteger.Abs(coefficient) > (BigInteger.One << 96) - 1 Then
            Throw New FormatException("割引率はDecimalで正確に表現できません。")
        End If
        ' Decimal.Parseの暗黙の桁丸めに頼らず、検証済みの係数から生成する。
        Dim negative = coefficient.Sign < 0
        coefficient = BigInteger.Abs(coefficient)
        Dim mask = (BigInteger.One << 32) - 1
        Return New Decimal(SignedWord(coefficient And mask), SignedWord((coefficient >> 32) And mask),
                           SignedWord((coefficient >> 64) And mask), negative, CByte(scale))
    End Function

    Private Shared Function SignedWord(value As BigInteger) As Integer
        Return BitConverter.ToInt32(BitConverter.GetBytes(CUInt(value)), 0)
    End Function

    Public Shared Function RateText(value As Decimal) As String
        Return value.ToString("G29", CultureInfo.InvariantCulture)
    End Function

    Public Shared Function Net(fee As Integer, discount As Integer, quantity As Integer, rate As Decimal) As BigInteger
        Dim basis = (New BigInteger(fee) - discount) * quantity
        Dim bits = Decimal.GetBits(rate)
        Dim coefficient = New BigInteger(CUInt(CLng(bits(0)) And &HFFFFFFFFL)) +
                          (New BigInteger(CUInt(CLng(bits(1)) And &HFFFFFFFFL)) << 32) +
                          (New BigInteger(CUInt(CLng(bits(2)) And &HFFFFFFFFL)) << 64)
        If bits(3) < 0 Then coefficient = -coefficient
        Dim scale = (bits(3) >> 16) And &HFF
        ' Ceilingの直前まで整数の商・余りで扱い、微小な二進誤差を作らない。
        Dim divisor = BigInteger.Pow(10, scale) * 100
        Dim numerator = basis * coefficient
        Dim reduction = numerator / divisor
        If numerator Mod divisor > 0 Then reduction += 1
        Return basis - reduction
    End Function

    Public Shared Function Taxed(net As BigInteger, rate As Integer, taxType As Integer, mode As String) As BigInteger
        If mode <> "floor" AndAlso mode <> "half_up" Then Throw New ArgumentException("不正な税端数方式です。")
        If taxType <> 0 AndAlso taxType <> 1 Then Throw New ArgumentException("不正な内税／外税区分です。")
        If taxType = 0 Then Return net
        Dim numerator = net * rate
        Dim tax = numerator / 100
        Dim remainder = numerator Mod 100
        If mode = "floor" Then
            If remainder < 0 Then tax -= 1
        ElseIf BigInteger.Abs(remainder) >= 50 Then
            tax += numerator.Sign
        End If
        Return net + tax
    End Function
End Class
