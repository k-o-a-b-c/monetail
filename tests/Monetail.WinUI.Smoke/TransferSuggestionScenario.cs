using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckTransferSuggestions()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true }; var masters = new PaymentServer(); var server = new TransferInputServer(masters);
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(masters.People())); fixture.PaymentsHandler = masters.HandleAsync; fixture.TransfersHandler = server.HandleAsync;
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenTransferEditorAsync(null);
        var name = Find<AutoSuggestBox>(root, "TransferName"); name.Focus(FocusState.Keyboard);
        await Wait(() => server.Suggestions > 0 && name.IsSuggestionListOpen);
        Check(TransferEditing!.Draft.Name == "" && !TransferEditing.HasUnsavedChanges && server.Autofills == 0, "空欄フォーカスで最近の振替名を取得し、表示だけで確定・補完しない");
        await TypeTransferName(root, "AT"); await Wait(() => name.IsSuggestionListOpen && server.Suggestions >= 2); var before = server.Autofills;
        await PressReceiptKey(0x28); await Settle();
        Check(TransferEditing.Draft.Name == "AT" && server.Autofills == before, "候補の矢印プレビューは業務値を確定せず補完しない");
        await PressReceiptKey(0x1B); await Settle();
        Check(!name.IsSuggestionListOpen && name.Text == "AT" && TransferEditing.Draft.Name == "AT", "Escはプレビューを戻して候補だけ閉じ入力を維持");
        Find<TextBox>(root, "TransferAmount").Focus(FocusState.Programmatic); await Wait(() => server.Autofills > before);
        name.Focus(FocusState.Keyboard); await Wait(() => name.IsSuggestionListOpen);
        var searches = server.Suggestions; await PressReceiptKey(0x28); await PressReceiptKey(0x09); await Settle();
        Check(name.Text == "AT" && TransferEditing.Draft.Name == "AT" && !name.IsSuggestionListOpen && server.Suggestions == searches, "Tabは候補を確定せず自由入力を保ち、同条件の候補を再利用");
        name.Focus(FocusState.Keyboard); await Wait(() => name.IsSuggestionListOpen); await PressReceiptKey(0x28); await PressReceiptKey(0x0D);
        await Wait(() => TransferEditing.Draft.Name == "ATM" && !name.IsSuggestionListOpen);
        Check(Find<ComboBox>(root, "TransferFrom").FocusState != FocusState.Unfocused && server.Writes.Count == 0, "Enter確定は元表記を採用して次の振替元へ移動し保存しない");
        await Wait(() => TransferEditing.Draft.To.PaymentMethodId is not null);
        var auto = TransferEditing.Draft.To.PaymentMethodId; var from = Find<ComboBox>(root, "TransferFrom"); from.SelectedItem = from.Items.Cast<ReceiptFilterChoice>().Single(c => c.Id == masters.OtherActiveId);
        Check(TransferEditing.Draft.From.Source == TransferSelectionSource.Manual, "振替元の明示選択は手動由来として保護");
        // 旧応答はモデルの文脈と画面世代でも拒否する。
        server.AutofillGate = new(TaskCreationOptions.RunContinuationsAsynchronously); name.Text = ""; await TypeTransferName(root, "New");
        Find<TextBox>(root, "TransferAmount").Focus(FocusState.Programmatic); await Settle(); var manual = TransferEditing.Draft.From.PaymentMethodId;
        Find<DateInputBox>(root, "TransferDate").Text = "未確定";
        server.AutofillGate.SetResult(true); await Settle();
        Check(TransferEditing.Draft.From.PaymentMethodId == manual && TransferEditing.Draft.UnresolvedDateText == "未確定", "日付変更後の遅延補完は手動選択と未確定文字列を変更しない");
        var keyboard = (FormKeyboard)typeof(MainWindow).GetField("transferKeyboard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var composing = (HashSet<TextBox>)typeof(FormKeyboard).GetField("composing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(keyboard)!;
        var inner = All(name).OfType<TextBox>().First(); composing.Add(inner); var writes = server.Writes.Count;
        Check(keyboard.HandleEnter(true, false) is null && server.Writes.Count == writes, "IME合成中のCtrl+Enterは振替保存を実行しない"); composing.Clear();
        await InvokeAsync(() => Find<Button>(root, "TransferBack")); await ConfirmTransfer(root, ContentDialogResult.None);
        Check(Find<DateInputBox>(root, "TransferDate").Text == "未確定", "離脱取消は日付を共通補完せず元の生入力を保持");
        await InvokeAsync(() => Find<Button>(root, "TransferBack")); await ConfirmTransfer(root, ContentDialogResult.Secondary); await Wait(() => TransferEditing is null);
        Check(server.Writes.Count == 0, "明示破棄は画面内だけを閉じ、候補や補完から保存しない");
    }
}
