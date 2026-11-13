using System.Collections.ObjectModel;
using System.Net;
using Monetail.Windows.Client.Ledgers;

namespace Monetail.Windows.Client.Transport;

public sealed record TaxRateInfo(string TaxCategoryCode, DateOnly ValidFrom, DateOnly? ValidTo, decimal? Rate)
{
    public bool IsValidOn(DateOnly date) => ValidFrom <= date && (ValidTo is null || date <= ValidTo);
}

/// <summary>全期間を保持する不変の読取結果。候補化・初期値の選択は呼出側の明示操作で行う。</summary>
public sealed class TaxRatesInfo
{
    public ReadOnlyCollection<TaxRateInfo> Rates { get; }
    public TaxRatesInfo(IEnumerable<TaxRateInfo> rates) => Rates = Array.AsReadOnly(rates.ToArray());
}
internal sealed record TaxRatesPayload(TaxRateInfo[] Rates);

public sealed class TaxRateRequestException(HttpStatusCode status, string? code, string? field, PeoplePermissions? permissions)
    : Exception("税率履歴を取得できませんでした。")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? Code { get; } = code;
    public string? Field { get; } = field;
    public PeoplePermissions? Permissions { get; } = permissions;
}

public sealed partial class MonetailApiClient
{
    public async Task<TaxRatesInfo> ListTaxRatesAsync(string access, Guid ledgerId, CancellationToken token = default)
    {
        ReceiptQueries.Id(ledgerId, "ledgerId");
        // 全履歴に件数制限を作らない。Decimal文字列/NULLの厳密な読取を既存の逐次JSON処理と共用する。
        var result = await SendResponseAsync<TaxRatesPayload>(HttpMethod.Get, $"ledgers/{ledgerId:D}/tax-rates", access,
            null, false, token, ReceiptJson.ReadAsync<TaxRatesPayload>, failure: (status, bytes) =>
            {
                if (status == HttpStatusCode.ServiceUnavailable && bytes.Length == 0) return new TaxRateRequestException(status, null, null, null);
                var error = ReadPeopleFailure(status, bytes, false);
                return new TaxRateRequestException(status, error.Code, error.Field, error.Permissions);
            }, includeStoreFailure: true, successStatus: HttpStatusCode.OK);
        if (result.Rates is null || result.Rates.Any(row => row is null || !CategoryInput.IsTax(row.TaxCategoryCode) ||
            row.ValidTo < row.ValidFrom || row.TaxCategoryCode == "Exempt" && row.Rate is not null)) throw new ApiConnectionException();
        return new(result.Rates);
    }
}
