Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json

Friend Module PersonStatisticsTests
    Public Sub FilterMatrix()
        Dim a As New User With {.Code = 1}
        Dim b As New User With {.Code = 2}
        Dim c As New User With {.Code = 3}
        Dim outsider As New User With {.Code = 4}
        Dim people = {a, b, c, outsider}
        ' 添字は選択集合のビット値。対象者なし11円、A=12、B=26、AB=60、ABC=99、BC=84。
        ' 按分は採用明細の1人分であり、選択した人数を掛けない。
        Dim partialTotals = {292, 171, 269, 281, 183, 255, 269, 281, 0, 171, 269, 281, 183, 255, 269, 281}
        Dim partialShare = {143, 75, 131, 143, 75, 117, 131, 143, 0, 75, 131, 143, 75, 117, 131, 143}
        Dim exactTotals = {11, 12, 26, 60, 0, 0, 84, 99, 0, 0, 0, 0, 0, 0, 0, 0}
        Dim exactShare = {0, 12, 26, 30, 0, 0, 42, 33, 0, 0, 0, 0, 0, 0, 0, 0}
        For Each mode In New String() {"floor", "half_up"}
            For Each taxType In New Integer() {0, 1}
                Dim receipt = MakeReceipt(mode, Group(10, Line(11, False), Line(12, False, a),
                    Line(26, False, b), Line(60, False, a, b), Line(99, False, a, b, c), Line(84, False, b, c)))
                receipt.TaxType = taxType
                For mask As Integer = 0 To 15
                    Dim selected As New List(Of User)
                    For index As Integer = 0 To people.Length - 1
                        If (mask And (1 << index)) <> 0 Then selected.Add(people(index))
                    Next
                    ' 同じ参照と、別読込を想定した同コードの別オブジェクトを比較する。
                    Dim copies = selected.Select(Function(u) New User With {.Code = u.Code, .Name = "別読込"}).ToList()
                    For Each selection In {selected, copies}
                        AssertFees(receipt, selection, False, False, partialTotals(mask))
                        AssertFees(receipt, selection, False, True, partialShare(mask))
                        AssertFees(receipt, selection, True, False, exactTotals(mask))
                        AssertFees(receipt, selection, True, True, exactShare(mask))
                    Next
                Next
            Next
        Next
    End Sub

    Public Sub AllocationRounding()
        Dim a As New User With {.Code = 1}
        Dim b As New User With {.Code = 2}
        Dim c As New User With {.Code = 3}
        Dim selected As New List(Of User) From {New User With {.Code = a.Code}}
        For Each rate In New Integer() {8, 10}
            For Each mode In New String() {"floor", "half_up"}
                For Each taxType In New Integer() {0, 1}
                    ' 2人／3人、正負、課税／非課税／混在。期待値は製品の計算関数で生成しない。
                    Dim receipt = MakeReceipt(mode,
                        Group(rate, Line(5, True, a, b)), Group(rate, Line(-5, True, a, b)),
                        Group(rate, Line(1, True, a, b, c)), Group(rate, Line(-1, True, a, b, c)),
                        Group(rate, Line(27, True, a, b)), Group(rate, Line(-27, True, a, b)),
                        Group(rate, Line(5, False, a, b)), Group(rate, Line(-5, False, a, b)),
                        Group(rate, Line(27, True, a, b), Line(1, False, a, b)), Group(rate, Line(11, True, a, b)))
                    receipt.TaxType = taxType
                    Dim expected As Integer()
                    If taxType = 0 Then
                        expected = If(mode = "floor", New Integer() {2, -3, 0, -1, 13, -14, 2, -3, 14, 5},
                            New Integer() {3, -3, 0, 0, 14, -14, 3, -3, 14, 6})
                    ElseIf mode = "floor" Then
                        expected = If(rate = 8, New Integer() {2, -3, 0, -1, 14, -15, 2, -3, 15, 5},
                            New Integer() {2, -3, 0, -1, 14, -15, 2, -3, 15, 6})
                    Else
                        expected = If(rate = 8, New Integer() {3, -3, 0, 0, 15, -15, 3, -3, 15, 6},
                            New Integer() {3, -3, 0, 0, 15, -15, 3, -3, 15, 7})
                    End If
                    AssertFees(receipt, selected, False, True, expected)
                Next
            Next
        Next
    End Sub

    Public Sub GroupBoundaries()
        Dim a As New User With {.Code = 1}
        Dim b As New User With {.Code = 2}
        Dim selected As New List(Of User) From {a}
        For Each mode In New String() {"floor", "half_up"}
            Dim receipt = MakeReceipt(mode, Group(10, Line(3, True, a, b), Line(3, True, a, b)),
                Group(10, Line(3, True, a, b)), Group(10, Line(19, True, b)), Group(8))
            receipt.Groups(2).GroupClassification.Code = 2
            receipt.Groups(3).GroupClassification.Code = 3
            ' 同分類の2グループを合併せず、各グループ内で合算してから丸める。
            AssertFees(receipt, selected, False, True, If(mode = "floor", New Integer() {3, 1, 0, 0}, New Integer() {3, 2, 0, 0}))
            AssertFees(receipt, selected, False, False, If(mode = "floor", New Integer() {6, 3, 0, 0}, New Integer() {7, 3, 0, 0}))
            For Each isAnd In New Boolean() {False, True}
                For Each onlyPerson In New Boolean() {False, True}
                    AssertFees(MakeReceipt(mode), selected, isAnd, onlyPerson)
                Next
            Next
        Next
    End Sub

    Public Sub UserCodeMatching()
        Dim a As New User With {.Code = 1, .Name = "A"}
        Dim b As New User With {.Code = 2, .Name = "B"}
        Dim copyA As New User With {.Code = 1, .Name = "改名したA"}
        Dim copyB As New User With {.Code = 2, .Name = "改名したB"}
        Dim sameNameOtherCode As New User With {.Code = 3, .Name = "A"}
        For Each mode In {"floor", "half_up"}
            For Each taxType In {0, 1}
                Dim receipt = MakeReceipt(mode, Group(10, Line(100, False, a, b), Line(7, False, a)))
                receipt.TaxType = taxType
                ' 参照・表示名・順序にかかわらずコードで判定し、同名の別コードは除外する。
                For Each selection In {New List(Of User) From {b, a}, New List(Of User) From {copyB, copyA}, New List(Of User) From {a, copyB}}
                    AssertFees(receipt, selection, True, False, 100)
                    AssertFees(receipt, selection, False, False, 107)
                    AssertFees(receipt, selection, True, True, 50)
                    AssertFees(receipt, selection, False, True, 57)
                Next
                AssertFees(receipt, New List(Of User) From {copyA}, True, False, 7)
                AssertFees(receipt, New List(Of User) From {sameNameOtherCode}, False, False, 0)
                AssertFees(receipt, New List(Of User) From {copyA, sameNameOtherCode}, True, False, 0)
                AssertFees(receipt, New List(Of User) From {copyA, sameNameOtherCode}, False, False, 107)
            Next
        Next
    End Sub

    Private Function Line(amount As Integer, taxable As Boolean, ParamArray users As User()) As ReceiptDetail
        Dim result = Detail(amount, taxable)
        result.Users.AddRange(users)
        Return result
    End Function

    Private Sub AssertFees(receipt As Receipt, selected As List(Of User), isAnd As Boolean, onlyPerson As Boolean,
                           ParamArray expected As Integer())
        Dim before = JsonSerializer.Serialize(receipt)
        Dim beforeSelected = selected.ToArray()
        Dim selectionValues = JsonSerializer.Serialize(selected)
        Dim context = $"mode={receipt.TaxRoundingMode} tax={receipt.TaxType} users={String.Join(",", selected.Select(Function(u) u.Code))} exact={isAnd} share={onlyPerson}"
        Dim actual = receipt.GetGroupStatisticsList(selected, isAnd, onlyPerson)
        Equal(expected.Length, actual.Count, context)
        For index As Integer = 0 To expected.Length - 1
            Equal(expected(index), actual(index).Fee, context & " group=" & index)
            Equal(receipt.Groups(index).GroupClassification.Code, actual(index).GroupCode, context)
            Equal(receipt.Date, actual(index).YearMonth, context)
        Next
        Equal(before, JsonSerializer.Serialize(receipt), "Receipt and accounting totals changed: " & context)
        Equal(True, beforeSelected.SequenceEqual(selected), "Selected user references or order changed: " & context)
        Equal(selectionValues, JsonSerializer.Serialize(selected), "Selected users changed: " & context)
    End Sub
End Module
