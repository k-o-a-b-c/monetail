namespace Monetail.Api.Features.Monetail.Receipts;

/// <summary>既存APIの入口を保ち、同一の共通実装へ委譲する。</summary>
public static class ReceiptCalculator
{
    public static ReceiptAmounts Calculate(ReceiptCalculationInput input) => global::Monetail.Calculations.Receipts.ReceiptCalculator.Calculate(input);
}
