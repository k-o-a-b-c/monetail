using System.Text.Json;

namespace Monetail.Windows.Client;

public sealed class ClientSettingsStore(string directory)
{
    private readonly string path = Path.Combine(directory, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ApiEndpoint? Load()
    {
        byte[] contents;
        try { contents = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(contents, JsonOptions);
            return settings is { Version: 1, ApiUrl: not null } ? ApiEndpoint.Parse(settings.ApiUrl) : null;
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }

    public void Save(ApiEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(new Settings(1, endpoint.Value), JsonOptions));
    }

    private sealed record Settings(int Version, string ApiUrl);
}
