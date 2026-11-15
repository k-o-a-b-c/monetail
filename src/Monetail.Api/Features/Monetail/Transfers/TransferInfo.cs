namespace Monetail.Api.Features.Monetail.Transfers;

/// <summary>詳細GETの8項目。監査列やマスタ名称を公開しない。</summary>
public sealed record TransferInfo(string TransferId, string TransactionAt, string Name,
    string FromPaymentMethodId, string ToPaymentMethodId, int Amount, string? Memo, string Revision);
