using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Monetail.WinUI.Views;
using Monetail.Windows.Presentation.Lists;
using Windows.Graphics;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    // 行やAPIを持たない表示状態でも、将来の残高一覧を組み込めることだけを確認する。
    private sealed class PanelProbeState : IRecordListState
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsInitialLoading
        {
            get; set;
        }
        public bool IsEmpty
        {
            get; set;
        }
        public bool IsInitialFailure
        {
            get; set;
        }
        public bool IsContinuationLoading
        {
            get; set;
        }
        public bool IsContinuationFailure
        {
            get; set;
        }
        public bool CanContinue
        {
            get; set;
        }
        public bool IsFocused
        {
            get; set;
        }
        public string ErrorText { get; set; } = "組込確認の取得失敗";
        public IAsyncRelayCommand RetryCommand { get; set; } = new AsyncRelayCommand(() => Task.CompletedTask);
        public IAsyncRelayCommand ContinueCommand { get; set; } = new AsyncRelayCommand(() => Task.CompletedTask);
        public void Notify(string property) => PropertyChanged?.Invoke(this, new(property));
    }

    private async Task CheckRecordListPanel()
    {
        var state = new PanelProbeState { IsInitialLoading = true };
        var retries = 0;
        var continuations = 0;
        var canRegister = false;
        var register = new AsyncRelayCommand(() => Task.CompletedTask, () => canRegister);
        state.RetryCommand = new AsyncRelayCommand(() => { retries++; return Task.CompletedTask; });
        state.ContinueCommand = new AsyncRelayCommand(() => { continuations++; return Task.CompletedTask; });
        var opening = new TextBlock { Text = "組込確認：編集開始", Visibility = Visibility.Collapsed };
        var footer = new RecordListFooter { AutomationPrefix = "Balance", State = state, ErrorText = "組込確認：追加取得失敗" };
        var list = new ListView { Footer = footer };
        list.Items.Add("組込確認の行");
        var panel = new RecordListPanel
        {
            Heading = "残高",
            EmptyText = "組込確認：0件",
            AutomationPrefix = "Balance",
            RegisterAutomationId = "ProbeRegister",
            State = state,
            RegisterCommand = register,
            OpeningContent = opening,
            ListContent = list
        };
        var host = new Window { Content = panel };
        host.AppWindow.ResizeClient(new SizeInt32(640, 480));
        host.Activate();
        T Element<T>(FrameworkElement scope, string name) where T : class => scope.FindName(name) as T
            ?? throw new InvalidOperationException("共通表示要素がありません: " + name);
        try
        {
            await Wait(() => panel.IsLoaded && list.IsLoaded && All(list).OfType<ScrollViewer>().Any());
            var progress = Element<ProgressRing>(panel, "InitialProgress");
            var registerButton = Element<Button>(panel, "Register");
            Check(progress.IsActive && Element<StackPanel>(panel, "InitialNotice").Visibility == Visibility.Visible &&
                !panel.IsTabStop && !footer.IsTabStop && !registerButton.IsEnabled && Element<Button>(panel, "ReturnToStart").Visibility == Visibility.Collapsed,
                "共通Panelは残高用の表示状態だけで初期読込・外部登録権限・focusなしを表示できる");
            canRegister = true;
            register.NotifyCanExecuteChanged();
            await Wait(() => registerButton.IsEnabled);
            Check(ReferenceEquals(registerButton.Command, register) && AutomationProperties.GetAutomationId(registerButton) == "ProbeRegister" &&
                AutomationProperties.GetName(registerButton) == "残高の登録", "共通登録Buttonは外部CommandのCanExecuteと指定AutomationIdを使う");

            state.IsInitialLoading = false;
            state.IsEmpty = true;
            state.Notify(nameof(state.IsInitialLoading));
            state.Notify(nameof(state.IsEmpty));
            await Wait(() => !progress.IsActive && Element<TextBlock>(panel, "Empty").Visibility == Visibility.Visible);
            Check(Element<TextBlock>(panel, "Empty").Text == "組込確認：0件" && AutomationProperties.GetLiveSetting(Element<TextBlock>(panel, "Empty")) == AutomationLiveSetting.Polite,
                "共通0件表示は外部文言・Polite通知とINotifyPropertyChangedの更新を使う");

            state.IsEmpty = false;
            state.IsInitialFailure = true;
            state.Notify(nameof(state.IsEmpty));
            state.Notify(nameof(state.IsInitialFailure));
            await Wait(() => Element<StackPanel>(panel, "InitialFailure").Visibility == Visibility.Visible);
            await InvokeAsync(() => Element<Button>(panel, "InitialRetry"));
            Check(retries == 1 && Element<TextBlock>(panel, "InitialError").Text == state.ErrorText,
                "共通初回失敗は固有エラーと既存RetryCommandを表示する");

            state.IsInitialFailure = false;
            state.IsContinuationLoading = true;
            state.Notify(nameof(state.IsInitialFailure));
            state.Notify(nameof(state.IsContinuationLoading));
            await Wait(() => Element<ProgressRing>(footer, "NextProgress").IsActive);
            Check(Element<StackPanel>(footer, "NextNotice").Visibility == Visibility.Visible && ReferenceEquals(list.Footer, footer),
                "追加読込はListView.Footer内の共通リングで表示する");
            state.IsContinuationLoading = false;
            state.IsContinuationFailure = true;
            state.Notify(nameof(state.IsContinuationLoading));
            state.Notify(nameof(state.IsContinuationFailure));
            await Wait(() => Element<StackPanel>(footer, "NextFailure").Visibility == Visibility.Visible);
            await InvokeAsync(() => Element<Button>(footer, "NextRetry"));
            Check(retries == 2 && All(footer).OfType<TextBlock>().Any(text => text.Text == footer.ErrorText),
                "共通Footerは外部の追加取得失敗文言と同じRetryCommandを使う");
            state.IsContinuationFailure = false;
            state.CanContinue = true;
            state.Notify(nameof(state.IsContinuationFailure));
            state.Notify(nameof(state.CanContinue));
            await Wait(() => Element<Button>(footer, "Continue").Visibility == Visibility.Visible);
            await InvokeAsync(() => Element<Button>(footer, "Continue"));
            Check(continuations == 1 && ReferenceEquals(Element<Button>(footer, "Continue").Command, state.ContinueCommand),
                "共通Footerの明示続行は外部Commandだけを実行する");

            opening.Visibility = Visibility.Visible;
            await Wait(() => Element<Border>(panel, "OpeningNotice").Visibility == Visibility.Visible);
            opening.Visibility = Visibility.Collapsed;
            await Wait(() => Element<Border>(panel, "OpeningNotice").Visibility == Visibility.Collapsed);
            Check(ReferenceEquals(panel.OpeningContent, opening) && ReferenceEquals(panel.ListContent, list),
                "編集開始通知は同じ外部UIを重ねて表示し、非表示時も一覧を保持する");

            for (var i = 1; i < 200; i++)
                list.Items.Add("組込確認の行" + i);
            var scroll = All(list).OfType<ScrollViewer>().First();
            await Wait(() => scroll.ScrollableHeight > 0);
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
            await Wait(() => scroll.VerticalOffset > 0);
            panel.UpdateLayout();
            Check(list.ItemsPanelRoot is ItemsStackPanel && All(list).OfType<ListViewItem>().Count() < list.Items.Count &&
                All(scroll).Contains(footer), "共通表示枠はListViewの高さ制約・仮想化とFooterの同一スクロールを維持する");
            var replacement = new PanelProbeState { IsInitialLoading = true };
            panel.State = replacement;
            footer.State = replacement;
            await Wait(() => progress.IsActive && Element<Button>(footer, "Continue").Visibility == Visibility.Collapsed);
            state.IsEmpty = true;
            state.Notify(nameof(state.IsEmpty));
            Check(Element<TextBlock>(panel, "Empty").Visibility == Visibility.Collapsed && ReferenceEquals(panel.State, replacement),
                "状態参照の交換後は旧ViewModelの通知を反映せず、同じ正本だけを読む");
            panel.State = null;
            footer.State = null;
            await Wait(() => !progress.IsActive && Element<StackPanel>(panel, "InitialNotice").Visibility == Visibility.Collapsed);
            Check(Element<StackPanel>(footer, "NextNotice").Visibility == Visibility.Collapsed,
                "状態の解除で共通のリングと読込通知を停止する");
        }
        finally
        {
            host.Close();
        }
    }
}
