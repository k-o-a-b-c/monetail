using System.Net;
using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Management;
using Monetail.Windows.Presentation.Workspace;
using Monetail.WinUI.Views.Workspace;

namespace Monetail.WinUI;
public sealed partial class MainWindow
{
    private ManagementPage? ManagementPage => WorkspaceHost.Content as ManagementPage;
    private bool ManagementDialogOpen => transferDialogOpen || receiptDialogOpen || ManagementPage?.HasDialog == true;

    private void EnsureManagement()
    {
        if (management is not null || account is null || currentLedger is null || connection.Endpoint is null)
            return;
        var context = new ManagementContext(connection.Api, connection.Session, connection.Masters, currentLedger, account.AuthUserId, connection.Endpoint.Value, RunAsync, lifetime.Token)
        {
            IsBusy = busy
        };
        management = new(context, () => !busy && !ManagementDialogOpen, ConnectAsync, LogoutAsync, account.DisplayName ?? "");
    }

    private bool SuspendManagementForAuthentication()
    {
        if (management?.Context.IsSuspended == true)
            return true;
        if (ManagementPage?.SuspendForAuthentication() != true)
            return false;
        // 管理の実入力だけを保持し、従来どおり認証失効前の記録一覧・マスタを解除する。
        ResetRecordHome();
        return true;
    }

    private void DiscardManagement()
    {
        ManagementPage?.Release();
        management?.Dispose();
        management = null;
    }

    private void ReturnFromManagement(object? sender, EventArgs args)
    {
        if (!closed && !busy && ReferenceEquals(sender, ManagementPage) && !ManagementDialogOpen)
            NavigateWorkspace(WorkspaceScreen.HomePage);
    }

    private async Task<bool> ResumeManagementAsync(CancellationToken token)
    {
        if (management?.Context is not { IsSuspended: true } context || account is null || ManagementPage is not { } page)
            return false;
        if (context.AccountId != account.AuthUserId || context.Origin != connection.Endpoint?.Value)
        {
            DiscardManagement();
            return false;
        }

        var ledger = context.Ledger;
        string? denied = null;
        try
        {
            ledger = await connection.Ledgers.OpenAsync(ledger.LedgerId, token);
            context.Reopened(ledger, connection.Api, connection.Session);
        }
        catch (LedgerRequestException error)when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            context.ApplyPermissions(new(null, null));
            denied = "この家計簿を開く権限がありません。未保存の入力は残していますが、保存できません。";
        }

        // DisplayLedgerは明示的な破棄を伴うため、保持したPageの復帰には使わない。
        DisplayLedgerHeader(ledger);
        await page.ResumeAsync(denied, token);
        token.ThrowIfCancellationRequested();
        if (!closed && ReferenceEquals(page, ManagementPage))
            NavigateWorkspace(WorkspaceScreen.ManagementPage);
        return true;
    }
}
