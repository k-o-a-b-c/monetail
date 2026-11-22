namespace Monetail.WinUI.Views.Management;
// 管理Pageの寿命だけを共通Shellへ渡す。入力や業務操作は公開しない。
internal interface IManagementFeature
{
    event EventHandler? ReturnRequested;
    bool CanStay { get; }

    bool HasDialog { get; }

    bool CanRestoreAuthentication { get; }

    Task LoadAsync();
    void SuspendForAuthentication();
    Task ResumeAsync(string? denied, CancellationToken token);
    void Release();
}
