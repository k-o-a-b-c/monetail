Imports System.Globalization
Imports System.Numerics

' 移行とアプリの会計処理で使う十進値。途中の乗除算も暗黙に丸めない。
Public NotInheritable Class ExactMoney
    ' 共有計算クラスのインスタンス化を防ぐ。
    Private Sub New()
    End Sub

    ' 割引率の文字列を共通書式のDecimalへ変換する。
    Public Shared Function ParseRate(text As String) As Decimal
        Dim value As Decimal
        If Not Decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then
            Throw New FormatException("割引率はDecimalの範囲内の数値で指定してください。")
        End If
        Return value
    End Function

    ' 割引率を精度を保って保存用の共通書式へ変換する。
    Public Shared Function RateText(value As Decimal) As String
        Return value.ToString("G29", CultureInfo.InvariantCulture)
    End Function

    ' 単価・数量・値引額・割引率から値引き後の金額を計算する。
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

    ' 整数金額から内税・外税と端数方式に応じた税込金額を計算する。
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

' 人数按分を十進小数へ近似せず、最後の業務上の丸めまで分数で保持する。
Public Structure MoneyFraction
    ' 分数金額の分子を保持する。
    Private ReadOnly numerator As BigInteger
    ' 分数金額の分母を保持する。
    Private ReadOnly denominator As BigInteger

    ' 金額と整数の分母から分数金額を生成する。
    Public Sub New(value As BigInteger, Optional divisor As Integer = 1)
        Me.New(value, New BigInteger(divisor))
    End Sub

    ' 分子と分母を約分して分数金額を生成する。
    Private Sub New(value As BigInteger, divisor As BigInteger)
        If divisor <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(divisor))
        Dim gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(value), divisor)
        numerator = value / gcd
        denominator = divisor / gcd
    End Sub

    ' 未初期化時も計算できるよう分母を補完する。
    Private ReadOnly Property Divisor As BigInteger
        Get
            Return If(denominator.IsZero, BigInteger.One, denominator)
        End Get
    End Property

    ' 二つの分数金額を誤差なく加算する。
    Public Shared Operator +(left As MoneyFraction, right As MoneyFraction) As MoneyFraction
        Return New MoneyFraction(left.numerator * right.Divisor + right.numerator * left.Divisor, left.Divisor * right.Divisor)
    End Operator

    ' 分数金額を指定された端数方式で整数へ丸める。
    Public Function Rounded(mode As String) As BigInteger
        If mode <> "floor" AndAlso mode <> "half_up" Then Throw New ArgumentException("不正な税端数方式です。")
        Dim result = numerator / Divisor
        Dim remainder = numerator Mod Divisor
        If mode = "floor" Then
            If remainder < 0 Then result -= 1
        ElseIf BigInteger.Abs(remainder) * 2 >= Divisor Then
            result += numerator.Sign
        End If
        Return result
    End Function

    ' 分数金額から内税・外税と端数方式に応じた税込金額を計算する。
    Public Function WithTax(rate As Integer, taxType As Integer, mode As String) As MoneyFraction
        If taxType <> 0 AndAlso taxType <> 1 Then Throw New ArgumentException("不正な内税／外税区分です。")
        If taxType = 0 Then Return Me
        Dim tax = New MoneyFraction(numerator * rate, Divisor * 100).Rounded(mode)
        Return Me + New MoneyFraction(tax)
    End Function
End Structure
