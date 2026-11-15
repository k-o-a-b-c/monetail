using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Transfers;

internal sealed record TransferDeleteInput(string RequestId, string Revision)
{
    internal static readonly string[] Fields = ["requestId", "revision"];
    internal static TransferDeleteInput Decode(LedgerPayload input) => new(TransferCreateInput.V4Uuid(input, "requestId"), TransferCreateInput.V4Uuid(input, "revision"));
}
