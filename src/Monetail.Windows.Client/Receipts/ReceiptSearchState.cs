using System.Globalization;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Receipts;

/// <summary>編集中のテキストと適用済み条件を分離する。選択変更は編集中テキストを確定しない。</summary>
public sealed class ReceiptSearchState
{
    public DateOnly Month { get; private set; }
    public string MonthText { get; set; }
    public string ShopText { get; set; } = "";
    public string AppliedShop { get; private set; } = "";
    private bool allPeriods;
    public bool AllPeriods
    {
        get => allPeriods;
        set { allPeriods = value; if (value) MonthError = null; }
    }
    public Guid? OwnerPersonId { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public string? MonthError { get; private set; }
    public ReceiptSearchState(DateOnly today) { Month = new(today.Year, today.Month, 1); MonthText = Format(Month); }
    public bool ApplyText()
    {
        if (!AllPeriods)
        {
            if (MonthText.Length != 7 || !DateOnly.TryParseExact(MonthText + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            { MonthError = "年月をYYYY-MMで入力してください。"; return false; }
            Month = month;
        }
        AppliedShop = ShopText; MonthError = null; return true;
    }
    public bool CanMove(int delta) => !AllPeriods && (delta < 0 ? Month > new DateOnly(1, 1, 1) : Month < new DateOnly(9999, 12, 1));
    public bool MoveMonth(int delta)
    {
        if (!CanMove(delta)) return false;
        var pendingText = MonthText != Format(Month); Month = Month.AddMonths(delta);
        if (!pendingText) MonthText = Format(Month);
        return true;
    }
    public string AppliedMonth => Format(Month);
    public ReceiptListQuery Query() => new(AllPeriods ? "all" : Format(Month), OwnerPersonId, PaymentMethodId, AppliedShop);
    private static string Format(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
