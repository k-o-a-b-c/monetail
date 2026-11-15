using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monetail.Windows.Client.Transport;

/// <summary>振替の必須NULL/型/UUID/ローカル秒精度を検証する。レシートの読取契約は変更しない。</summary>
internal static class TransferJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
        Converters = { new Identifier(), new LocalTimestamp() }
    };
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
            reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: 36 } text && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty ? id : throw new JsonException();
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("D"));
    }
    private sealed class LocalTimestamp : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: 19 } text && DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? DateTime.SpecifyKind(date, DateTimeKind.Unspecified) : throw new JsonException();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
    }
}

internal sealed class TransferRows : Collection<TransferSummaryInfo>
{
    protected override void InsertItem(int index, TransferSummaryInfo item)
    {
        if (Count >= 100 || item is null) throw new JsonException(); base.InsertItem(index, item);
    }
}
internal sealed class TransferCandidateNames : Collection<string>
{
    protected override void InsertItem(int index, string item)
    {
        if (Count >= 100 || string.IsNullOrWhiteSpace(item) || item.Length > 100) throw new JsonException(); base.InsertItem(index, item);
    }
}
internal sealed record TransferPagePayload(TransferRows Transfers, string? NextCursor);
internal sealed record TransferSuggestionsPayload(TransferCandidateNames Suggestions, bool HasMore);

// 共通エラーのmessage/latest等は公開しない。認証境界等の追加項目は読み捨てる。
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed record TransferFailurePayload(string? Code = null, string? Field = null, string? Message = null,
    JsonElement? Latest = null, PeoplePermissions? Permissions = null);
