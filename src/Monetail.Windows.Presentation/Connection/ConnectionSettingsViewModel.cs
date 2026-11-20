using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monetail.Windows.Client;

namespace Monetail.Windows.Presentation.Connection;

/// <summary>URLの生入力・検証と操作の寿命を所有する。保存/認証の契約は既存Clientへ委ねる。</summary>
public sealed partial class ConnectionSettingsViewModel : ObservableObject, IDisposable
{
    private readonly Func<ApiEndpoint, CancellationToken, Task> connect;
    private readonly Func<Task>? cancel;
    private readonly CancellationTokenSource lifetime = new();
    public ConnectionSettingsViewModel(ApiEndpoint? endpoint, Func<ApiEndpoint, CancellationToken, Task> connect, Func<Task>? cancel = null)
    {
        this.connect = connect;
        this.cancel = cancel;
        ServerUrl = endpoint?.Value ?? "";
        Description = endpoint is null ? "サーバーのURLを入力してください。" : "接続先を変更すると、変更先でのログインが必要です。";
        ConnectCommand.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsConnecting));
            CancelCommand.NotifyCanExecuteChanged();
        };
    }
    [ObservableProperty]
    public partial string ServerUrl
    {
        get; set;
    }
    [ObservableProperty]
    public partial string? UrlError
    {
        get; set;
    }
    public string Description
    {
        get;
    }
    public bool CanCancel => cancel is not null;
    public bool IsConnecting => ConnectCommand.IsRunning;
    private bool CanConnect() => !lifetime.IsCancellationRequested;
    private bool CanCancelNow() => CanCancel && CanConnect() && !IsConnecting;
    partial void OnServerUrlChanged(string value) => UrlError = null;
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (!CanConnect())
            return;
        ApiEndpoint endpoint;
        try
        {
            endpoint = ApiEndpoint.Parse(ServerUrl);
        }
        catch (FormatException error)
        {
            UrlError = error.Message;
            return;
        }
        UrlError = null;
        try
        {
            await connect(endpoint, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    [RelayCommand(CanExecute = nameof(CanCancelNow))]
    private Task CancelAsync() => CanCancelNow() ? cancel?.Invoke() ?? Task.CompletedTask : Task.CompletedTask;
    public void Dispose()
    {
        if (lifetime.IsCancellationRequested)
            return;
        lifetime.Cancel();
        lifetime.Dispose();
        ConnectCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
