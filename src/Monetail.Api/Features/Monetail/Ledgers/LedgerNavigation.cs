namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>端末間で共有する、アカウントが最後に開いた家計簿を保持する。</summary>
public sealed class LedgerNavigation
{
    public string AuthUserId { get; set; } = null!;
    public string LastOpenedLedgerId { get; set; } = null!;
}
