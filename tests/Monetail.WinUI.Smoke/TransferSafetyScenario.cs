using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transfers;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckTransferSafety()
    {
        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var masters = new PaymentServer(); var server = new TransferInputServer(masters) { LoseResponse = true };
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenTransferEditorAsync(null); await FillTransfer(root, masters);
            await InvokeAsync(() => Find<Button>(root, "TransferSave"));
            try { await Wait(() => Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Visible || Find<ContentDialog>(root, "TransferConfirmationDialog").IsLoaded || Find<InfoBar>(root, "TransferEditorNotice").IsOpen || TransferEditing is null); }
            catch (TaskCanceledException error)
            {
                var model = TransferView.ViewModel;
                var flags = $"sending={model?.IsSending},dialog={TransferRuntime("transferDialogOpen")},stopped={model?.IsStopped},unknown={model?.IsUnknown}";
                throw new InvalidOperationException($"結果不明待機: {flags}; writes={server.Writes.Count}; rows={server.Rows.Count}; saveEnabled={Find<Button>(root, "TransferSave").IsEnabled}; time={TransferEditing?.Draft.TransactionTime}; amount={TransferEditing?.Draft.Amount}; popup={Find<AutoSuggestBox>(root, "TransferName").IsSuggestionListOpen}", error);
            }
            if (Find<Button>(root, "TransferWriteRetry").Visibility != Visibility.Visible) throw new InvalidOperationException($"結果不明診断: writes={server.Writes.Count}; rows={server.Rows.Count}; notice={Find<InfoBar>(root, "TransferEditorNotice").Message}; dialog={Find<ContentDialog>(root, "TransferConfirmationDialog").Content}; time={TransferEditing?.Draft.TransactionTime}; from={TransferEditing?.Draft.From.Source}; to={TransferEditing?.Draft.To.Source}");
            var pending = TransferView.ViewModel?.PendingRequest;
            Check(server.Rows.Count == 1 && server.Writes.Count == 1 && !Find<Button>(root, "TransferSave").IsEnabled && Find<TextBox>(root, "TransferAmount").IsReadOnly,
                "応答喪失では登録済みか断定せず入力・元要求を保持して編集と別保存を禁止");
            Check(Find<Button>(root, "TransferLoadLatest").Visibility == Visibility.Collapsed, "通信結果不明では競合用の最新採用を提示しない");
            await InvokeAsync(() => Find<Button>(root, "TransferBack")); await ConfirmTransfer(root, ContentDialogResult.None);
            Check(ReferenceEquals(pending, TransferView.ViewModel?.PendingRequest), "結果未確認の離脱取消は元のrequestId付き要求を保持");
            await InvokeAsync(() => Find<Button>(root, "TransferWriteRetry")); await Wait(() => TransferEditing is null);
            Check(server.Rows.Count == 1 && server.Writes.Count == 2 && server.Writes[0].Body == server.Writes[1].Body, "結果不明の明示再試行は同じJSONで二重登録せず成功行へ戻る");
        }
        foreach (var error in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.Unauthorized })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var masters = new PaymentServer(); var server = new TransferInputServer(masters); var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenTransferEditorAsync(saved.TransferId);
            Find<TextBox>(root, "TransferAmount").Text = "2000"; server.WriteError = error;
            if (error == HttpStatusCode.Unauthorized) fixture.RejectSession = true;
            await InvokeAsync(() => Find<Button>(root, "TransferSave"));
            if (error == HttpStatusCode.Conflict)
            {
                await Wait(() => Find<ContentDialog>(root, "TransferConfirmationDialog").IsLoaded);
                await InvokeAsync(() => TransferDialogFooter(root, ContentDialogResult.Primary));
                await Wait(() => Equals(Find<ContentDialog>(root, "TransferConfirmationDialog").Title, "振替が更新されています"));
                await ConfirmTransfer(root, ContentDialogResult.None);
            }
            else await ConfirmTransfer(root, ContentDialogResult.Primary);
            if (error == HttpStatusCode.Unauthorized)
            {
                await Wait(() => Visible(root, "LoginPage"));
                Check(Find<TextBox>(root, "TransferAmount").Text == "2000" && TransferEditing?.Revision == saved.Revision && !VisibleAncestors(Find<Grid>(root, "TransferEditorPage")), "401は入力と旧版を保持して専用の再ログイン画面へ退避する");
                await CompleteTransferLogin(fixture, root);
                Check(Equals(TransferView.ViewModel?.IsUnknown, false) && Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Visible, "確定401の復帰は結果不明にせず、保持した要求の明示再試行を提示する");
            }
            await Wait(() => Find<InfoBar>(root, "TransferEditorNotice").IsOpen && Find<TextBox>(root, "TransferAmount").IsReadOnly);
            Check(TransferEditing!.Revision == saved.Revision && Find<TextBox>(root, "TransferAmount").Text == "2000" && !Find<Button>(root, "TransferSave").IsEnabled && !Find<Button>(root, "TransferDelete").IsEnabled,
                $"振替{(int)error}は入力と旧版を保持し書込を安全に停止");
            Check(Find<Grid>(root, "TransferEditorPage").Visibility == Visibility.Visible && (Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Visible) == (error == HttpStatusCode.Unauthorized), $"確定した{(int)error}を結果不明の再送や自動ログインと混同しない");
            Check((Find<Button>(root, "TransferLoadLatest").Visibility == Visibility.Visible) == (error == HttpStatusCode.Conflict), "最新版の採用はrevision_conflictだけに提示する");
            await InvokeAsync(() => Find<Button>(root, "TransferBack"));
            await Wait(() => Find<ContentDialog>(root, "TransferConfirmationDialog").IsLoaded);
            Check(Find<ContentDialog>(root, "TransferConfirmationDialog").SecondaryButtonText == "" && Find<ContentDialog>(root, "TransferConfirmationDialog").PrimaryButtonText == "破棄して閉じる", "停止後の離脱では保存して移動を提示しない");
            await ConfirmTransfer(root, ContentDialogResult.Primary); await Wait(() => TransferEditing is null);
        }
        foreach (var role in new[] { "Viewer", "Editor" })
        {
            using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
            var masters = new PaymentServer { Role = role }; var server = new TransferInputServer(masters); var saved = server.Add();
            fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
            var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync();
            Check(Find<Button>(root, "TransferRegisterPlaceholder").IsEnabled == (role == "Editor"), "Viewerと人物対応Editorの新規登録権限を分離");
            await window!.OpenTransferEditorAsync(saved.TransferId);
            Check(TransferEditing!.IsReadOnly && Find<TextBox>(root, "TransferNameReadOnly").IsReadOnly && Find<TextBox>(root, "TransferFromReadOnly").Text.Length > 0 && Find<Button>(root, "TransferSave").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferReuse").Visibility == Visibility.Collapsed && Find<Button>(root, "TransferDelete").Visibility == Visibility.Collapsed,
                "Viewer・片側だけ本人のEditorは操作を非表示にし値の選択・コピーが可能な読み取り専用表示");
            await InvokeAsync(() => Find<Button>(root, "TransferBack")); await Wait(() => TransferEditing is null);
            Check(server.Writes.Count == 0, "読み取り専用を開いて戻るだけでは確認も書込も行わない");
        }
    }
}
