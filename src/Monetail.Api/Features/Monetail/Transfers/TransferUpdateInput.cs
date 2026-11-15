using Monetail.Api.Features.Monetail.Ledgers;

namespace Monetail.Api.Features.Monetail.Transfers;

internal sealed record TransferUpdateInput(TransferCreateInput Values, string Revision)
{
    internal static readonly string[] Fields = [.. TransferCreateInput.Fields, "revision"];
    internal static TransferUpdateInput Decode(LedgerPayload input) => new(TransferCreateInput.Decode(input), TransferCreateInput.V4Uuid(input, "revision"));
}
