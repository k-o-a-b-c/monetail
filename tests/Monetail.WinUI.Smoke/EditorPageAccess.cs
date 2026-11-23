using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Views.Receipts;
using Monetail.WinUI.Views.Transfers;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private ReceiptEditorPage? CurrentReceiptPage => (window?.Content as FrameworkElement)?.FindName("WorkspaceHost") is Frame { Content: ReceiptEditorPage page } ? page : null;
    private TransferEditorPage? CurrentTransferPage => (window?.Content as FrameworkElement)?.FindName("WorkspaceHost") is Frame { Content: TransferEditorPage page } ? page : null;
    private object? ReceiptRecoveryPhaseValue => CurrentReceiptPage?.ViewModel is { } model ? model.GetType().GetField("recovery", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(model) : null;
    private object? ReceiptRecoveryPosition => ReceiptRecoveryPhaseValue?.ToString() is "Login" or "Loading" or "Failed"
        ? CurrentReceiptPage?.GetType().GetField("position", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(CurrentReceiptPage) : null;
    private object? TransferRecoveryPosition => CurrentTransferPage?.ViewModel?.RecoveryBlocked == true
        ? CurrentTransferPage.GetType().GetField("position", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(CurrentTransferPage) : null;
    private static ContentDialog? EditorConfirmation(FrameworkElement page) => page.GetType()
        .GetField("confirmation", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(page) as ContentDialog;
    private static ContentDialog? EditorDialog(FrameworkElement root, string name) => FindEditorElement(root, name) as ContentDialog;
    private static object? FindEditorElement(FrameworkElement root, string name)
    {
        var page = (root.FindName("WorkspaceHost") as Frame)?.Content as FrameworkElement;
        var dialog = page switch
        {
            ReceiptEditorPage receipt => EditorConfirmation(receipt),
            TransferEditorPage transfer => EditorConfirmation(transfer),
            _ => null
        };
        if (dialog is not null)
        {
            ObserveDialog(dialog);
            if (name is "ReceiptConfirmationDialog" or "TransferConfirmationDialog")
                return dialog;
        }
        if (page is ReceiptEditorPage && name is "ReceiptEditorPage" or "ReceiptEntry" ||
            page is TransferEditorPage && name is "TransferEditorPage" or "TransferEntry")
            return page;
        return page is ReceiptEditorPage or TransferEditorPage ? page.FindName(name) : null;
    }
}
