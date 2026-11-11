using System.Globalization;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Receipts;

/// <summary>未確定の文字列を保存値と分離する。解析失敗で0/1へ補正せず、反映は全項目の解析後だけ。</summary>
public sealed class ReceiptDetailInput
{
    private readonly ReceiptDetailDraft baseline;
    public ReceiptDetailDraft Values { get; }
    public string UnitPrice { get; set; }
    public string Quantity { get; set; }
    public string UnitDiscountAmount { get; set; }
    public string DiscountRate { get; set; }
    public ReceiptDetailInput(ReceiptDetailDraft draft)
    {
        baseline = draft.Copy(); Values = draft.Copy();
        UnitPrice = Format(draft.UnitPrice); Quantity = Format(draft.Quantity);
        UnitDiscountAmount = Format(draft.UnitDiscountAmount); DiscountRate = Format(draft.DiscountRate);
    }
    private static string Format<T>(T? value) where T : struct, IFormattable => value?.ToString(null, CultureInfo.InvariantCulture) ?? "";
    public bool HasChanges
    {
        get { try { return !Read().Same(baseline); } catch (ReceiptInputException) { return true; } }
    }
    public ReceiptDetailDraft Read()
    {
        var result = Values.Copy();
        result.UnitPrice = Integer(UnitPrice, "unitPrice", expression: true);
        result.Quantity = Integer(Quantity, "quantity", expression: false);
        result.UnitDiscountAmount = Integer(UnitDiscountAmount, "unitDiscountAmount", expression: true);
        result.DiscountRate = DiscountRate == "" ? null : ExactDecimal(DiscountRate);
        return result;
    }
    public void ApplyTo(ReceiptDetailDraft target)
    {
        var value = Read();
        target.Name = value.Name; target.UnitPrice = value.UnitPrice; target.Quantity = value.Quantity;
        target.UnitDiscountAmount = value.UnitDiscountAmount; target.DiscountRate = value.DiscountRate;
        target.Tax = value.Tax; target.ExpenseType = value.ExpenseType; target.OccurrenceType = value.OccurrenceType; target.Memo = value.Memo;
        target.PersonIds.Clear(); target.PersonIds.AddRange(value.PersonIds);
    }
    private static int? Integer(string text, string field, bool expression)
    {
        if (text == "") return null;
        if (expression ? ReceiptIntegerExpression.TryEvaluate(text, out var value) : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
        throw new ReceiptInputException("detailDraft." + field, "Int32範囲の整数を入力してください。");
    }
    private static decimal ExactDecimal(string text)
    {
        if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) &&
            Canonical(text) == Canonical(value.ToString("0.############################", CultureInfo.InvariantCulture))) return value;
        throw new ReceiptInputException("detailDraft.discountRate", "正確に表せる値引率を入力してください。");
    }
    private static string Canonical(string text)
    {
        var negative = text.StartsWith('-'); var parts = text.TrimStart('+', '-').Split('.');
        var whole = parts[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
    }
}

/// <summary>旧IntegerExpressionと同じ整数の+/-/*優先順位。各途中値もInt32。失敗と0を区別する。</summary>
internal static class ReceiptIntegerExpression
{
    public static bool TryEvaluate(string text, out int result)
    {
        result = 0; var index = 0;
        void Space() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
        int Number()
        {
            Space(); var sign = 1;
            if (index < text.Length && text[index] is '+' or '-') { sign = text[index++] == '-' ? -1 : 1; Space(); }
            var start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
            if (start == index) throw new FormatException();
            var unsigned = long.Parse(text.AsSpan(start, index - start), NumberStyles.None, CultureInfo.InvariantCulture);
            return checked((int)checked(sign * unsigned));
        }
        int Product()
        {
            var value = Number(); Space();
            while (index < text.Length && text[index] == '*') { index++; value = checked(value * Number()); Space(); }
            return value;
        }
        try
        {
            var value = Product(); Space();
            while (index < text.Length)
            {
                var op = text[index++]; if (op is not ('+' or '-')) return false;
                var term = Product(); value = op == '+' ? checked(value + term) : checked(value - term); Space();
            }
            result = value; return true;
        }
        catch (Exception error) when (error is FormatException or OverflowException) { return false; }
    }
}
