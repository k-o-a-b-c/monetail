using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Views;

/// <summary>一覧表示と選択だけを担当する。取得・認証・家計簿を開く処理は所有しない。</summary>
public sealed partial class LedgerChoiceView : Page
{
    public LedgerChoiceView() => InitializeComponent();

    public IReadOnlyList<LedgerInfo> Ledgers
    {
        get => LedgerChoices.ItemsSource as IReadOnlyList<LedgerInfo> ?? [];
        set
        {
            LedgerChoices.ItemsSource = value;
            OpenLedgerButton.IsEnabled = false;
        }
    }

    public event EventHandler<Guid>? OpenRequested;

    private void ChooseLedger(object sender, SelectionChangedEventArgs args) =>
        OpenLedgerButton.IsEnabled = LedgerChoices.SelectedItem is LedgerInfo;

    private void OpenSelectedLedger(object sender, RoutedEventArgs args)
    {
        if (LedgerChoices.SelectedItem is LedgerInfo selected) OpenRequested?.Invoke(this, selected.LedgerId);
    }
}
