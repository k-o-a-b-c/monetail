using Monetail.Windows.Client.Transport;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.WinUI;

public sealed partial class MainWindow
{
    // API実行とアカウント/家計簿の照合はWindow、候補の表示・IME・遅延応答の世代はViewが所有する。
    private async Task<TransferSuggestionsInfo> LoadTransferSuggestionsAsync(TransferSuggestionQuery query, CancellationToken token)
    {
        var editor = transferEditor;
        var ledger = currentLedger?.LedgerId ?? throw new OperationCanceledException(token);
        var person = account?.AuthUserId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var result = await new TransfersWorkspace(connection.Api, connection.Session, ledger).SuggestAsync(query, linked.Token);
        if (!ReferenceEquals(editor, transferEditor) || currentLedger?.LedgerId != ledger || account?.AuthUserId != person)
            throw new OperationCanceledException(linked.Token);
        return result;
    }
    private async Task<TransferAutofillInfo> LoadTransferAutofillAsync(TransferAutofillQuery query, CancellationToken token)
    {
        var editor = transferEditor;
        var ledger = currentLedger?.LedgerId ?? throw new OperationCanceledException(token);
        var person = account?.AuthUserId;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        var result = await new TransfersWorkspace(connection.Api, connection.Session, ledger).AutofillAsync(query, linked.Token);
        if (!ReferenceEquals(editor, transferEditor) || currentLedger?.LedgerId != ledger || account?.AuthUserId != person)
            throw new OperationCanceledException(linked.Token);
        return result;
    }
    private void StopTransferAuthentication() =>
        NavigateToLogin("ログインを継続できません。入力を保持しています。もう一度ログインしてください。");
}
