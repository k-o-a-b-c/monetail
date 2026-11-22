using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Presentation.Receipts;

namespace Monetail.WinUI.Views.Receipts;

public sealed partial class ReceiptEditorPage
{
    private ContentDialog? confirmation;
    private bool leaving;
    private sealed record EditPosition(Control? Focus, int SelectionStart, int SelectionLength, (ScrollViewer Viewer, double X, double Y)[] Scrolls);
    private EditPosition? position;
    public bool HasDialog => confirmation is not null;
    internal ContentDialog? Confirmation => confirmation;

    private async Task<ContentDialogResult> ShowConfirmationAsync(ContentDialog dialog)
    {
        if (HasDialog || ViewModel is null)
            return ContentDialogResult.None;
        confirmation = dialog;
        ViewModel.SetDialogOpen(true);
        dialog.XamlRoot = XamlRoot;
        dialog.DefaultButton = ContentDialogButton.Close;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            confirmation = null;
            ViewModel?.SetDialogOpen(false);
        }
    }
    private async Task<ReceiptDraftChoice> ConfirmAsync(ReceiptEditorConfirmation kind)
    {
        if (ViewModel is not { } model || HasDialog)
            return ReceiptDraftChoice.Stay;
        var group = model.Editor.SelectedGroup;
        var dialog = kind == ReceiptEditorConfirmation.UnappliedDetail
            ? new ContentDialog
            {
                Title = "未反映の明細があります",
                Content = "現在の明細入力をどうしますか？",
                PrimaryButtonText = model.Editor.SelectedDetailKey is null ? "追加して移動" : "更新して移動",
                SecondaryButtonText = "入力を破棄して移動",
                CloseButtonText = "編集に戻る"
            }
            : new ContentDialog
            {
                Title = "科目を削除",
                Content = $"「{model.SelectedGroup?.Name}」と明細{group?.Details.Count}件を削除しますか？",
                PrimaryButtonText = "削除",
                CloseButtonText = "キャンセル"
            };
        var choice = await ShowConfirmationAsync(dialog);
        if (!ReferenceEquals(model, ViewModel) || model.IsReadOnly)
            return ReceiptDraftChoice.Stay;
        return choice switch
        {
            ContentDialogResult.Primary => ReceiptDraftChoice.ApplyAndMove,
            ContentDialogResult.Secondary => ReceiptDraftChoice.DiscardAndMove,
            _ => ReceiptDraftChoice.Stay
        };
    }
    public async Task<bool> TryLeaveAsync()
    {
        if (leaving || HasDialog || ViewModel?.IsTransitioning == true)
            return false;
        if (ViewModel is not { } model)
            return true;
        Synchronize();
        leaving = true;
        SuppressCompletion(true);
        try
        {
            if (!NeedsLeaveConfirmation)
                return true;
            var choice = await ShowConfirmationAsync(new ContentDialog
            {
                Title = "未保存の内容があります",
                Content = "保存はまだ利用できません。入力内容を破棄して移動しますか？",
                PrimaryButtonText = model.IsReadOnly ? "破棄して閉じる" : "破棄して移動",
                CloseButtonText = "編集に戻る"
            });
            return choice == ContentDialogResult.Primary && ReferenceEquals(model, ViewModel) && !model.AwaitingLogin;
        }
        finally
        {
            SuppressCompletion(false);
            leaving = false;
        }
    }
    public void RememberPosition()
    {
        if (position is not null || ViewModel is null)
            return;
        Synchronize();
        var focus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        position = new(focus, (focus as TextBox)?.SelectionStart ?? 0, (focus as TextBox)?.SelectionLength ?? 0,
            Visuals(this).OfType<ScrollViewer>().Select(v => (v, v.HorizontalOffset, v.VerticalOffset)).ToArray());
    }
    public void ForgetPosition() => position = null;
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Visuals(VisualTreeHelper.GetChild(root, i)))
                yield return item;
    }
    public void SuspendForAuthentication()
    {
        RememberPosition();
        SuppressCompletion(true);
        confirmation?.Hide();
        ViewModel?.SuspendForAuthentication();
        RefreshRecovery();
    }
    public void RestorePosition(Func<bool> current)
    {
        if (position is { } saved)
            RestorePosition(saved.Focus, saved.SelectionStart, saved.SelectionLength, saved.Scrolls, current);
    }
    public void Release()
    {
        Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        confirmation?.Hide();
        Loaded -= InitialFocus;
        if (ViewModel is { } model)
        {
            model.ConfirmationRequested -= ConfirmAsync;
            ModelChanged(model, null);
            model.Discard();
        }
        Bindings.StopTracking();
        ViewModel = null;
        IsCancellationTarget = null;
        position = null;
        Clear();
    }
}
