using System.Globalization;

namespace Monetail.Windows.Client.Receipts;

public enum ReceiptInputSource { Initial, Automatic, Manual, Saved }

/// <summary>値の変更と明示選択を分離する。再描画はこの状態を変更せず、HTTPは呼ばない。</summary>
public sealed class ReceiptParentInputState(ReceiptHeaderDraft header, bool existingReceipt)
{
    public ReceiptInputSource TaxInclusionSource { get; private set; } = existingReceipt ? ReceiptInputSource.Saved : ReceiptInputSource.Initial;
    public ReceiptInputSource TaxRoundingSource { get; private set; } = existingReceipt ? ReceiptInputSource.Saved : ReceiptInputSource.Initial;
    public bool CanAutofillTaxInclusion => !existingReceipt && TaxInclusionSource != ReceiptInputSource.Manual;
    public bool CanAutofillTaxRounding => !existingReceipt && TaxRoundingSource != ReceiptInputSource.Manual;
    public void SelectTaxInclusion(int value)
    {
        ValidateInclusion(value); header.TaxInclusionMode = value; TaxInclusionSource = ReceiptInputSource.Manual;
    }
    public void SelectTaxRounding(string value)
    {
        ValidateRounding(value); header.TaxRoundingMode = value; TaxRoundingSource = ReceiptInputSource.Manual;
    }
    public bool TryAutofillTaxInclusion(int? value)
    {
        if (!CanAutofillTaxInclusion) return false;
        ValidateInclusion(value ?? 0); header.TaxInclusionMode = value ?? 0;
        TaxInclusionSource = value is null ? ReceiptInputSource.Initial : ReceiptInputSource.Automatic; return true;
    }
    public bool TryAutofillTaxRounding(string? value)
    {
        if (!CanAutofillTaxRounding) return false;
        ValidateRounding(value ?? "floor"); header.TaxRoundingMode = value ?? "floor";
        TaxRoundingSource = value is null ? ReceiptInputSource.Initial : ReceiptInputSource.Automatic; return true;
    }
    private static void ValidateInclusion(int value) { if (value is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static void ValidateRounding(string value) { if (value is not ("floor" or "half_up")) throw new ArgumentOutOfRangeException(nameof(value)); }
}

public readonly record struct ReceiptRecurrenceInput(bool IsValid, int? Interval, string? Unit)
{
    public static ReceiptRecurrenceInput Parse(string text, string? unit)
    {
        if (text.Length == 0) return new(true, null, null);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) && interval > 0 && unit is "Week" or "Month" or "Year"
            ? new(true, interval, unit) : new(false, null, null);
    }
}
