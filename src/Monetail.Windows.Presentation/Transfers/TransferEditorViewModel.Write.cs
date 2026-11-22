using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Presentation.Transfers;

public sealed record TransferWriteCompletion(TransferWriteRequest Request, TransferSnapshot? Snapshot, Guid? SavedId);
public readonly record struct TransferWriteResult(TransferWriteCompletion? Completion = null, string? Field = null, bool OfferLatest = false);

public sealed partial class TransferEditorViewModel
{
    public async Task<TransferWriteResult> SendPendingAsync()
    {
        if (PendingRequest is not { } request || Context is not { } context || IsSending || request.LedgerId != context.LedgerId)
            return default;
        var wasUnknown = IsUnknown;
        StartSending();
        var workspace = new TransfersWorkspace(context.Api, context.Session, request.LedgerId);
        Guid? savedId = request.TransferId;
        try
        {
            if (request.Operation == TransferWriteOperation.Create)
                savedId = (await workspace.CreateAsync(request, context.WindowToken)).TransferId;
            else if (request.Operation == TransferWriteOperation.Update)
                await workspace.UpdateAsync(request, context.WindowToken);
            else
                await workspace.DeleteAsync(request, context.WindowToken);
        }
        catch (Exception error) when (error is AuthenticationRejectedException or AuthenticationContextChangedException)
        {
            SetUncertainResult(wasUnknown);
            AuthenticationRequired?.Invoke();
            return default;
        }
        catch (TransferRequestException error)
        {
            ClearPendingWrite();
            if (error.StatusCode == HttpStatusCode.Forbidden)
            {
                Editor.EnterReadOnly(TransferReadOnlyReason.Forbidden);
                ShowNotice("編集権限がなくなったため、保存できません");
            }
            else if (error.StatusCode == HttpStatusCode.NotFound)
            {
                Editor.EnterReadOnly(TransferReadOnlyReason.NotFound);
                ShowNotice("振替が削除されたか、アクセスできなくなりました。");
            }
            else if (error.StatusCode == HttpStatusCode.Conflict)
            {
                if (request.Operation is TransferWriteOperation.Update or TransferWriteOperation.Delete && error.Code == "revision_conflict" && error.LatestTransfer is { } latest)
                {
                    Editor.RegisterConflict(latest);
                    ShowNotice("他の端末で更新されています。入力を保持して読み取り専用にしました。編集を再開するには最新情報を読み込んでください。", TransferNoticeLevel.Warning);
                    return new(OfferLatest: true);
                }
                StopWriting();
                ShowNotice("要求を確認できません。入力を保持して書込を停止しました。");
            }
            else
            {
                ShowNotice(error.StatusCode == HttpStatusCode.TooManyRequests ? "操作回数の上限に達しました。しばらく待ってから保存してください。" : error.StatusCode == HttpStatusCode.ServiceUnavailable ? "保存先を利用できません。入力を保持しています。" : "入力内容を確認してください。");
                return new(Field: error.Field);
            }
            return default;
        }
        catch (Exception error) when (error is TransferWriteUncertainException or ApiConnectionException or HttpRequestException or IOException or OperationCanceledException)
        {
            SetUncertainResult(true);
            ShowNotice("保存結果を確認できませんでした。同じ要求で再試行してください。");
            return default;
        }
        finally
        {
            FinishSending();
        }
        var completion = new TransferWriteCompletion(request, PendingSnapshot, savedId);
        ClearPendingWrite();
        return new(completion);
    }
}
