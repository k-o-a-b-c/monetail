Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text.Json

' 旧Windows版の本物の計算コードを呼び、APIの対照テストへ結果を渡す。
' 新版モデルの計算を模写せず、税率は旧グループ値、課税対象は旧明細フラグのまま渡す。
Public Module LegacyParityExporter
    Public Sub Export(path As String)
        Dim results As New List(Of Object)()
        Using document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "receipt-parity-cases.json")))
            For Each item In document.RootElement.GetProperty("cases").EnumerateArray()
                Dim receipt = MakeReceipt(item.GetProperty("rounding").GetString())
                receipt.TaxType = item.GetProperty("inclusion").GetInt32()
                For Each groupInput In item.GetProperty("groups").EnumerateArray()
                    Dim group = Group(groupInput.GetProperty("rate").GetInt32())
                    For Each detailInput In groupInput.GetProperty("details").EnumerateArray()
                        group.Details.Add(Detail(
                            detailInput.GetProperty("price").GetInt32(),
                            detailInput.GetProperty("taxable").GetBoolean(),
                            detailInput.GetProperty("quantity").GetInt32(),
                            detailInput.GetProperty("discount").GetInt32(),
                            Decimal.Parse(detailInput.GetProperty("discountRate").GetString(), CultureInfo.InvariantCulture)))
                    Next
                    receipt.Groups.Add(group)
                Next
                Dim groups As New List(Of Object)()
                For Each group In receipt.Groups
                    Dim details As New List(Of Object)()
                    For Each detail In group.Details
                        details.Add(New With {
                            .netAmount = detail.CalculateDiscountedFee(),
                            .referenceAmount = Calculation.DetailCalculatedFee(receipt, group, detail)
                        })
                    Next
                    groups.Add(New With {
                        .referenceAmount = Calculation.GroupCalculatedFee(receipt, group),
                        .details = details
                    })
                Next
                results.Add(New With {
                    .id = item.GetProperty("id").GetString(),
                    .totalAmount = receipt.Total,
                    .incomeAmount = receipt.Incomes,
                    .expenseAmount = receipt.Expenses,
                    .groups = groups
                })
            Next
        End Using
        File.WriteAllText(path, JsonSerializer.Serialize(New With {.formatVersion = 1, .cases = results}))
        Console.WriteLine($"旧版の実計算結果を出力: {results.Count}ケース")
    End Sub
End Module
