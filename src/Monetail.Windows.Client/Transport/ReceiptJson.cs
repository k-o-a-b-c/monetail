using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monetail.Windows.Client.Transport;

/// <summary>レシート階層と専用409を逐次読み込む。他APIと他エラーの64KiB上限は維持する。</summary>
internal static class ReceiptJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
        Converters = { new Identifier(), new LocalTimestamp(), new DecimalString() }
    };
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static async Task<T> ReadAsync<T>(HttpContent content, CancellationToken token) where T : class
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(token);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, token) ?? throw new JsonException();
        }
        catch (JsonException) { throw new ApiConnectionException(); }
    }
    internal static T Read<T>(byte[] bytes) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw new JsonException(); }
        catch (JsonException) { throw new ApiConnectionException(); }
    }
    private sealed class Identifier : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && Guid.TryParseExact(reader.GetString(), "D", out var id) && id != Guid.Empty ? id : throw new JsonException();
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("D"));
    }
    private sealed class LocalTimestamp : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && DateTime.TryParseExact(reader.GetString(), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? DateTime.SpecifyKind(date, DateTimeKind.Unspecified) : throw new JsonException();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
    }
    private sealed class DecimalString : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String) throw new JsonException();
            var text = reader.GetString()!;
            if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
                Canonical(text) != Canonical(value.ToString("0.############################", CultureInfo.InvariantCulture))) throw new JsonException();
            return value;
        }
        // APIと同じ精度照合で、TryParseによる暗黙の丸めを拒否する。表記は保存/表示の都合で変更しない。
        private static string Canonical(string text)
        {
            var negative = text.StartsWith('-'); var parts = text.TrimStart('+', '-').Split('.');
            var whole = parts[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
            var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
            return (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
        }
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

// 100件上限のある配列はストリームの構築時にも検証し、後から101件を黙って切り捨てない。
internal sealed class ReceiptRows : Collection<ReceiptSummaryInfo>
{
    protected override void InsertItem(int index, ReceiptSummaryInfo item)
    {
        if (Count >= 100 || item is null) throw new JsonException(); base.InsertItem(index, item);
    }
}
internal sealed class ReceiptCandidateNames : Collection<string>
{
    protected override void InsertItem(int index, string item)
    {
        if (Count >= 100 || item is null || item.Length > 100) throw new JsonException(); base.InsertItem(index, item);
    }
}
internal sealed record ReceiptPagePayload(ReceiptRows Receipts, string? NextCursor);
internal sealed record ReceiptSuggestionsPayload(ReceiptCandidateNames Suggestions, bool HasMore);
