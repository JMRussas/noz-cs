//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoZ.Editor;

public static class GenerationClient
{
    private static readonly HttpClient _http = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static void Generate(GenerationRequest request, Action<GenerationResponse?> callback)
    {
        Task.Run(async () =>
        {
            try
            {
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{request.Server}/generate", content);

                Log.Info(await content.ReadAsStringAsync());

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    Log.Info($"Generation response (first 512 chars): {responseJson[..Math.Min(512, responseJson.Length)]}");
                    var result = JsonSerializer.Deserialize<GenerationResponse>(responseJson, _jsonOptions);
                    EditorApplication.RunOnMainThread(() => callback(result));
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log.Error($"Generation server returned {response.StatusCode}: {errorBody}");
                    EditorApplication.RunOnMainThread(() => callback(null));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Generation failed: {ex.Message}");
                EditorApplication.RunOnMainThread(() => callback(null));
            }
        });
    }
}

public class GenerationRequest
{
    [JsonIgnore]
    public string Server { get; set; } = "";

    public List<GenerationNode> Nodes { get; set; } = [];
    public string Output { get; set; } = "";
    public Dictionary<string, string> Inputs { get; set; } = new();
}

public class GenerationNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Properties { get; set; }
}

public class GenerationResponse
{
    public string Image { get; set; } = "";
    public long Seed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
