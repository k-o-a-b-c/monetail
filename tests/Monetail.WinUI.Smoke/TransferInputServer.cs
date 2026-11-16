using System.Net;
using System.Text.Json;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private sealed class TransferInputServer(PaymentServer masters)
    {
        internal readonly Dictionary<Guid, TransferInfo> Rows = [];
        internal readonly List<(HttpMethod Method, string Body)> Writes = [];
        internal readonly List<Uri> Reads = [];
        internal int Details, Suggestions, Autofills;
        internal bool LoseResponse;
        internal HttpStatusCode? WriteError;
        internal TaskCompletionSource<bool>? DetailGate, AutofillGate;
        private readonly Dictionary<Guid, Guid> created = [];
        internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!; var path = uri.AbsolutePath; var query = TransferQueryValues(uri);
            if (path.EndsWith("/transfer-suggestions"))
            { Suggestions++; return PaymentServer.Json(new TransferSuggestionsInfo(["ATM", "ＡＴＭ", "銀行振替"], false)); }
            if (path.EndsWith("/transfer-autofill"))
            { Autofills++; if (AutofillGate is { } gate) await gate.Task.WaitAsync(token); return PaymentServer.Json(new TransferAutofillInfo(masters.ActiveId, masters.OtherActiveId)); }
            var detail = !path.EndsWith("/transfers");
            Guid? id = detail ? Guid.Parse(path.Split('/')[^1]) : null;
            if (request.Method == HttpMethod.Get)
            {
                Reads.Add(uri);
                if (detail)
                {
                    Details++; if (DetailGate is { } gate) await gate.Task.WaitAsync(token);
                    return Rows.TryGetValue(id!.Value, out var row) ? PaymentServer.Json(row) : Fault(HttpStatusCode.NotFound, "not_found");
                }
                var all = Rows.Values.OrderByDescending(r => r.TransactionAt).ThenByDescending(r => r.TransferId.ToString("N"), StringComparer.Ordinal).ToArray();
                var start = query.TryGetValue("cursor", out var cursor) ? int.Parse(cursor) : 0;
                if (query.TryGetValue("focusTransferId", out var focus))
                { start = Array.FindIndex(all, r => r.TransferId == Guid.Parse(focus)); if (start < 0) return Fault(HttpStatusCode.NotFound, "not_found"); }
                return PaymentServer.Json(new TransferListInfo(all.Skip(start).Take(100).Select(r => new TransferSummaryInfo(r.TransferId, r.TransactionAt, r.Name, r.FromPaymentMethodId, r.ToPaymentMethodId, r.Amount, r.Memo)).ToArray(), all.Length > start + 100 ? (start + 100).ToString() : null));
            }
            var body = await request.Content!.ReadAsStringAsync(token); Writes.Add((request.Method, body));
            if (WriteError is { } status)
                return Fault(status, status == HttpStatusCode.Conflict ? "revision_conflict" : status == HttpStatusCode.Forbidden ? "forbidden" : status == HttpStatusCode.NotFound ? "not_found" : "invalid_input", id is Guid target && Rows.TryGetValue(target, out var current) ? current with { Name = "最新", Revision = Guid.NewGuid() } : null);
            using var json = JsonDocument.Parse(body); var value = json.RootElement;
            if (request.Method == HttpMethod.Delete) { Rows.Remove(id!.Value); return new(HttpStatusCode.NoContent); }
            if (request.Method == HttpMethod.Post)
            {
                var requestId = value.GetProperty("requestId").GetGuid();
                if (!created.TryGetValue(requestId, out var result)) { result = Guid.CreateVersion7(); created[requestId] = result; Rows[result] = Read(result, Guid.NewGuid(), value); }
                if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("synthetic response lost"); }
                var response = new HttpResponseMessage(HttpStatusCode.Created); response.Headers.Location = new Uri(path + "/" + result.ToString("D"), UriKind.Relative); return response;
            }
            Rows[id!.Value] = Read(id.Value, Guid.NewGuid(), value); return new(HttpStatusCode.NoContent);
        }
        internal TransferInfo Add(string name = "ATM", int amount = 100)
        {
            var id = Guid.CreateVersion7(); var row = new TransferInfo(id, DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Unspecified), name, masters.ActiveId, masters.OtherActiveId, amount, null, Guid.NewGuid()); Rows[id] = row; return row;
        }
        private static TransferInfo Read(Guid id, Guid revision, JsonElement row) => new(id, DateTime.SpecifyKind(DateTime.Parse(row.GetProperty("transactionAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Unspecified), row.GetProperty("name").GetString()!, row.GetProperty("fromPaymentMethodId").GetGuid(), row.GetProperty("toPaymentMethodId").GetGuid(), row.GetProperty("amount").GetInt32(), row.GetProperty("memo").ValueKind == JsonValueKind.Null ? null : row.GetProperty("memo").GetString(), revision);
        private static HttpResponseMessage Fault(HttpStatusCode status, string code, TransferInfo? latest = null) => PaymentServer.Json(new { code, message = (string?)null, field = (string?)null, latest, permissions = (object?)null }, status);
    }
}
