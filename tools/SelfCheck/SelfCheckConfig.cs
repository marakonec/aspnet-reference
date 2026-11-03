using System.Text.Json;
using System.Text.Json.Serialization;

namespace SelfCheck;

public sealed class SelfCheckConfig
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "http://localhost:15100";

    [JsonPropertyName("keyOperation")]
    public Endpoint? KeyOperation { get; set; }

    [JsonPropertyName("keyOperationBody")]
    public JsonElement? KeyOperationBody { get; set; }

    [JsonPropertyName("cacheableResource")]
    public string? CacheableResource { get; set; }

    [JsonPropertyName("volatileResource")]
    public string? VolatileResource { get; set; }

    [JsonPropertyName("invalidBodyTarget")]
    public Endpoint? InvalidBodyTarget { get; set; }

    [JsonPropertyName("invalidBody")]
    public JsonElement? InvalidBody { get; set; }

    [JsonPropertyName("unknownResource")]
    public string? UnknownResource { get; set; }

    [JsonPropertyName("artifacts")]
    public Dictionary<string, string> Artifacts { get; set; } = [];

    public sealed class Endpoint
    {
        [JsonPropertyName("method")] public string Method { get; set; } = "GET";
        [JsonPropertyName("path")] public string Path { get; set; } = "/";
    }

    public static SelfCheckConfig Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "selfcheck.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Немає файла selfcheck.json у {repoRoot}. Запустити самоперевірку з кореня репозиторію " +
                "або вказати його параметром --repo.");

        var options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonSerializer.Deserialize<SelfCheckConfig>(
                   File.ReadAllText(path), options)
               ?? throw new InvalidOperationException("Файл selfcheck.json порожній");
    }
}
