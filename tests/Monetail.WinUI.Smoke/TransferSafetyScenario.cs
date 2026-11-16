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
            await InvokeAsync(() => Find<Button>(root, "TransferSave")); await Wait(() => Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Visible);
            var pending = typeof(MainWindow).GetField("transferPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
            Check(server.Rows.Count == 1 && server.Writes.Count == 1 && !Find<Button>(root, "TransferSave").IsEnabled && Find<TextBox>(root, "TransferAmount").IsReadOnly,
                "応答喪失では登録済みか断定せず入力・元要求を保持して編集と別保存を禁止");
            await InvokeAsync(() => Find<Button>(root, "TransferBack")); await ConfirmTransfer(root, ContentDialogResult.None);
            Check(ReferenceEquals(pending, typeof(MainWindow).GetField("transferPending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)), "結果未確認の離脱取消は元のrequestId付き要求を保持");
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
            await InvokeAsync(() => Find<Button>(root, "TransferSave")); await ConfirmTransfer(root, ContentDialogResult.Primary);
            await Wait(() => Find<InfoBar>(root, "TransferEditorNotice").IsOpen && Find<TextBox>(root, "TransferAmount").IsReadOnly);
            Check(TransferEditing!.Revision == saved.Revision && Find<TextBox>(root, "TransferAmount").Text == "2000" && !Find<Button>(root, "TransferSave").IsEnabled && !Find<Button>(root, "TransferDelete").IsEnabled,
                $"振替{(int)error}は入力と旧版を保持し書込を安全に停止");
            Check(Find<Grid>(root, "TransferEditorPage").Visibility == Visibility.Visible && Find<Button>(root, "TransferWriteRetry").Visibility == Visibility.Collapsed, $"確定した{(int)error}を結果不明の再送や自動ログインと混同しない");
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
