using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckStartupBoundaries()
    {
        foreach (var count in new[] { 0, 1, 2 })
        {
            using var fixture = new SyntheticConnection(saved: true) { ChoiceCount = count };
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var destination = count switch { 0 => "LedgerCreatePage", 1 => "HomePage", _ => "LedgerChoicePage" };
            Check(Visible(root, destination) && Startup(root) is null && fixture.Requests.Take(3).SequenceEqual(
                (string[])["/auth/me", "/ledgers/open-last", "/ledgers"]) &&
                fixture.Requests.Count(path => path == "/ledgers/open-last") == 1 &&
                fixture.Requests.Count(path => path.EndsWith("/open", StringComparison.Ordinal)) == (count == 1 ? 1 : 0),
                $"通常起動の所属{count}件は既存Workspaceの判断で表示先を決め、家計簿を二重取得しない");
        }

        using (var fixture = new SyntheticConnection(saved: true) { HasPreviousLedger = true })
        {
            var root = await OpenSyntheticWindow(fixture);
            await StartSyntheticAsync();
            var tokens = fixture.Tokens.Current;
            var ledger = TransferRuntime("currentLedger");
            var retry = TransferRuntime("retry");
            var requests = fixture.Requests.Count;
            var run = typeof(MainWindow).GetMethod("RunAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("共通操作がありません。");
            (Exception Error, string Message)[] failures =
            [
                (new LedgerRequestException(HttpStatusCode.NotFound), "この家計簿は開けません。参加状態を確認してください。"),
                (new LedgerRequestException(HttpStatusCode.Forbidden), "この操作を行う権限がありません。"),
                (new LedgerRequestException(HttpStatusCode.TooManyRequests), "操作回数の上限に達しました。しばらく待って再試行してください。"),
                (new LedgerRequestException(HttpStatusCode.BadRequest), "家計簿の操作を完了できませんでした。内容を確認して再試行してください。"),
                (new CategoryRequestException(HttpStatusCode.BadRequest, "invalid_input", null), "科目一覧を取得できませんでした。参加状態と権限を確認してください。"),
                (new PaymentMethodRequestException(HttpStatusCode.BadRequest, "invalid_input", null), "支払方法一覧を取得できませんでした。参加状態と権限を確認してください。"),
                (new PeopleRequestException(HttpStatusCode.TooManyRequests, null, null), "操作回数の上限に達しました。しばらく待って再試行してください。"),
                (new PeopleRequestException(HttpStatusCode.BadRequest, "invalid_input", null), "人物一覧を取得できませんでした。家計簿への参加状態と権限を確認してください。"),
                (new ApiConnectionException(), "サーバーに接続できませんでした。接続先と通信状態を確認してください。"),
                (new IOException("合成の保存先障害"), "設定・認証情報を読み書きできませんでした。保存先へのアクセスを確認してください。")
            ];
            foreach (var (error, message) in failures)
            {
                Func<Task> action = () => Task.FromException(error);
                await (Task)(run.Invoke(window, [action, null]) ?? throw new InvalidOperationException("操作結果がありません。"));
                var notice = Find<InfoBar>(root, "ErrorNotice");
                Check(notice.IsOpen && notice.Message == message && Visible(root, "HomePage") && FailureScreen(root) is null &&
                    Equals(TransferRuntime("busy"), false) && ReferenceEquals(TransferRuntime("retry"), retry) &&
                    ReferenceEquals(TransferRuntime("currentLedger"), ledger) && ReferenceEquals(fixture.Tokens.Current, tokens) &&
                    fixture.Requests.Count == requests,
                    $"通常業務の{error.GetType().Name}/{message}は共有通知だけで、失敗Page・再送・認証削除へ進まない");
            }
        }
    }
}
