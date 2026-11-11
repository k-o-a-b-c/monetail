using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Monetail.WinUI.Input;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckReceiptMemos()
    {
        using var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true };
        var categories = new CategoryServer(); fixture.CategoriesHandler = categories.HandleAsync;
        var payments = new PaymentServer(); fixture.PaymentsHandler = payments.HandleAsync;
        fixture.PeopleHandler = (_, _) => Task.FromResult(PaymentServer.Json(payments.People()));
        var id = Guid.NewGuid(); var groupId = Guid.NewGuid(); var detailId = Guid.NewGuid(); var revision = Guid.NewGuid(); var writes = 0;
        const string parentMemo = "親\r\nメモ", groupMemo = "科目\nメモ", detailMemo = "明細\r\nメモ";
        fixture.ReceiptsHandler = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) { writes++; throw new InvalidOperationException("全体保存不可"); }
            if (request.RequestUri!.AbsolutePath.EndsWith("/receipts")) return Task.FromResult(PaymentServer.Json(new { receipts = Array.Empty<object>(), nextCursor = (string?)null }));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                receiptId = id, transactionAt = DateTime.Today.ToString("yyyy-MM-dd") + "T00:00:00", shopName = "メモの確認", shopLocation = (string?)null,
                paymentMethodId = payments.ActiveId, taxInclusionMode = 0, taxRoundingMode = "floor", recurrenceInterval = (int?)null, recurrenceUnit = (string?)null,
                memo = parentMemo, revision, groups = new[] { new { groupId, categoryId = categories.Rows[0].CategoryId, memo = groupMemo, details = new[] {
                    new { detailId, displayOrder = 0, name = "明細", unitPrice = 100, quantity = 1, unitDiscountAmount = 0, discountRate = "0", taxRate = (string?)null,
                        expenseType = "Variable", occurrenceType = "Irregular", memo = detailMemo, personIds = Array.Empty<Guid>() }
                } } }
            }) });
        };
        var root = await OpenSyntheticWindow(fixture); await StartSyntheticAsync(); await window!.OpenReceiptEditorAsync(id);
        var editor = ReceiptEditing!; var group = editor.Groups.Single(); window.ActivateReceiptCategory(group.Key);
        Find<ListView>(root, "ReceiptDetailsList").SelectedIndex = 0;
        await Wait(() => editor.SelectedDetailKey == group.Details[0].Key);
        var keyboard = (FormKeyboard)typeof(MainWindow).GetField("receiptEditorKeyboard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var composing = (HashSet<TextBox>)typeof(FormKeyboard).GetField("composing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(keyboard)!;
        bool Dirty() => (bool)typeof(MainWindow).GetProperty("ReceiptEditorDirty", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        Check(!Dirty() && editor.Header.Memo == parentMemo && group.Memo == groupMemo && editor.Draft!.Memo == detailMemo,
            "1行表示で開いて選択しただけでは保存済みの改行メモも未保存変更にしない");
        var memos = new[] { Find<TextBox>(root, "ReceiptMemo"), Find<TextBox>(root, "ReceiptCategoryMemo"), Find<TextBox>(root, "ReceiptItemMemo") };
        var next = new Control[] { Find<ComboBox>(root, "ReceiptCategoryChoice"), Find<Button>(root, "ReceiptCategoryRemove"), Find<Button>(root, "ReceiptDetailApply") };
        for (var index = 0; index < memos.Length; index++)
        {
            var memo = memos[index]; var initialText = memo.Text;
            Check(!memo.AcceptsReturn && memo.TextWrapping == TextWrapping.NoWrap && memo.ReadLocalValue(FrameworkElement.MinHeightProperty) == DependencyProperty.UnsetValue,
                $"{memo.Name}は複数行用の高さを持たない標準1行TextBox");
            memo.Text = "入力" + index;
            Check(Dirty() && (index == 0 ? editor.Header.Memo : index == 1 ? group.Memo : editor.Draft!.Memo) == "入力" + index,
                $"{memo.Name}の入力を対応するモデルと未保存判定へ反映");
            await ActivateReceiptInputWindowAsync(); memo.Focus(FocusState.Keyboard);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), memo));
            var options = new FindNextElementOptions { SearchRoot = root.XamlRoot.Content };
            var previous = FocusManager.FindNextElement(FocusNavigationDirection.Previous, options);
            await PressReceiptKey(0x0D, 0x10);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), previous));
            memo.Focus(FocusState.Keyboard); await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), memo));
            composing.Add(memo); await PressReceiptKey(0x0D); await Settle();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), memo) && group.Details.Count == 1 && group.Details[0].Values.Memo == detailMemo,
                $"{memo.Name}のIME変換確定Enterはフォーカス移動も明細反映もしない");
            composing.Clear();
            // 合成終了がKeyDownに先行する場合も既存の共通ガードを通す。
            typeof(FormKeyboard).GetField("completingWithEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(keyboard, true);
            Check(keyboard.HandleEnter(false, false) is null, $"{memo.Name}の変換終了直後のEnterも移動しない");
            typeof(FormKeyboard).GetField("completingWithEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(keyboard, false);
            await PressReceiptKey(0x0D);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), next[index]));
            Check(group.Details.Count == 1 && editor.SelectedDetailKey == group.Details[0].Key && group.Details[0].Values.Memo == detailMemo,
                $"{memo.Name}のEnterは次の項目へ移動するだけで追加/更新を実行しない");
            // ボタン上のEnterは実行操作なので、ここではメモへ明示的に戻して値戻しを確認する。
            memo.Focus(FocusState.Keyboard);
            await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), memo));
            memo.Text = initialText;
            Check(!Dirty() && editor.Header.Memo == parentMemo && group.Memo == groupMemo && editor.Draft!.Memo == detailMemo,
                $"{memo.Name}を表示時の値へ戻すと保存済み文字列と未変更判定を復元");
        }
        // 他項目のイベント・再描画でも、表示上の改行処理をメモの編集として取り込まない。
        var shop = Find<TextBox>(root, "ReceiptShopName"); shop.Text = "一時変更"; shop.Text = "メモの確認";
        window.RenderReceiptTaxValues();
        Check(!Dirty() && editor.Header.Memo == parentMemo && group.Memo == groupMemo && editor.Draft!.Memo == detailMemo,
            "他項目変更/再描画は保存済みメモを正規化せず維持");
        memos[2].Text = "ショートカット更新"; memos[2].Focus(FocusState.Keyboard);
        await Wait(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), memos[2]));
        await PressReceiptKey(0x0D, 0x12);
        await Wait(() => editor.SelectedDetailKey is null && group.Details[0].Values.Memo == "ショートカット更新");
        Check(group.Details.Count == 1 && group.Details[0].DetailId == detailId && editor.Revision == revision && writes == 0 &&
            ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), Find<TextBox>(root, "ReceiptItemName")),
            "明細メモのAlt+Enterは既存の画面内更新だけ実行し、ID/版と全体保存禁止を維持");
    }
}
