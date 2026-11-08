using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private static int uiThreadId, invocationSequence;
    private sealed class InvocationRejectedException(string reason) : InvalidOperationException("UI Invoke rejected: " + reason)
    {
        internal string Reason { get; } = reason;
    }

    private static void TraceInvocation(string message)
    {
        // Content/Tag/Text、HTTP本文、認証情報、接続先は記録しない。
        if (invokeTracePath is not null)
            File.AppendAllText(invokeTracePath, $"{DateTimeOffset.UtcNow:O} thread={Environment.CurrentManagedThreadId}; {message}{Environment.NewLine}");
    }
    private static string TargetState(Button button) =>
        $"id={RuntimeHelpers.GetHashCode(button)}; name={button.Name}; loaded={button.IsLoaded}; root={button.XamlRoot is not null}; rootId={(button.XamlRoot is { } root ? RuntimeHelpers.GetHashCode(root) : 0)}; host={button.XamlRoot?.IsHostVisible}; enabled={button.IsEnabled}; visible={VisibleAncestors(button)}; size={button.ActualWidth > 0 && button.ActualHeight > 0}";
    private static bool VisibleAncestors(DependencyObject target)
    {
        for (DependencyObject? current = target; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }
    private static bool InvokeTargetReady(Button button) =>
        Environment.CurrentManagedThreadId == uiThreadId && button.DispatcherQueue.HasThreadAccess &&
        button.IsLoaded && button.XamlRoot is { IsHostVisible: true } && button.IsEnabled &&
        button.ActualWidth > 0 && button.ActualHeight > 0 && VisibleAncestors(button);

    private static void Invoke(Button button, [CallerFilePath] string caller = "", [CallerLineNumber] int line = 0)
    {
        var sequence = Interlocked.Increment(ref invocationSequence);
        TraceInvocation($"invoke={sequence}; phase=begin; caller={Path.GetFileName(caller)}:{line}; uiThread={Environment.CurrentManagedThreadId == uiThreadId}");
        // UIプロパティを読む前にスレッドを検証し、不正な参照をネイティブPeerへ渡さない。
        if (Environment.CurrentManagedThreadId != uiThreadId) throw new InvocationRejectedException("wrong_thread");
        TraceInvocation($"invoke={sequence}; phase=target; {TargetState(button)}");
        if (!InvokeTargetReady(button)) throw new InvocationRejectedException("not_ready");
        TraceInvocation($"invoke={sequence}; phase=create_peer");
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button)
            ?? throw new InvalidOperationException("ButtonのAutomationPeerがありません。");
        TraceInvocation($"invoke={sequence}; phase=get_pattern; peer={peer.GetType().Name}");
        var provider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("ButtonのInvokeパターンがありません。");
        TraceInvocation($"invoke={sequence}; phase=invoke_provider");
        provider.Invoke();
        TraceInvocation($"invoke={sequence}; phase=returned");
    }

    private static void ObserveDialogs(FrameworkElement root)
    {
        foreach (var name in new[] { "PersonDialog", "PaymentDialog", "CategoryDialog", "ParticipantDialog", "InvitationCancelDialog" })
        {
            var dialog = Find<ContentDialog>(root, name);
            var id = RuntimeHelpers.GetHashCode(dialog);
            dialog.Opened += (_, _) => TraceInvocation($"dialog={name}; id={id}; event=opened");
            dialog.Closing += (_, _) => TraceInvocation($"dialog={name}; id={id}; event=closing");
            dialog.Closed += (_, _) => TraceInvocation($"dialog={name}; id={id}; event=closed");
        }
    }

    private async Task CheckInvocationGuards(FrameworkElement root)
    {
        static bool Rejects(Button button, string reason)
        {
            try { Invoke(button); return false; }
            catch (InvocationRejectedException error) { return error.Reason == reason; }
        }
        var button = Find<Button>(root, "SettingsButton");
        Check(Rejects(new Button(), "not_ready"), "未接続ButtonはAutomationPeerを作成する前に拒否");
        Check(await Task.Run(() => Rejects(button, "wrong_thread")), "別スレッドのInvokeはUIプロパティ/Peerへ触れる前に拒否");
        button.Visibility = Visibility.Collapsed;
        Check(Rejects(button, "not_ready"), "Loadedでも非表示のButtonはInvokeしない");
        button.Visibility = Visibility.Visible;
        button.IsEnabled = false;
        Check(Rejects(button, "not_ready"), "Loadedでも無効なButtonはInvokeしない");
        button.IsEnabled = true;
        await Wait(() => InvokeTargetReady(button));
    }
}
