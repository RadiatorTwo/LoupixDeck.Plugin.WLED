using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace LoupixDeck.Plugin.WLED;

/// <summary>Device info returned by <c>/json/info</c>.</summary>
public sealed record WledInfo(string Mac, string Name);

/// <summary>Thin wrapper around the WLED JSON API (https://kno.wled.ge/interfaces/json-api/).</summary>
public sealed class WledClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task<WledInfo> GetInfoAsync(string host, CancellationToken ct = default)
    {
        JsonNode info = await GetJsonAsync(host, "/json/info", ct);
        string mac = info["mac"]?.GetValue<string>() ?? string.Empty;
        if (mac.Length == 0)
            throw new InvalidOperationException("Response is not from a WLED device.");
        return new WledInfo(mac.ToLowerInvariant(), info["name"]?.GetValue<string>() ?? host);
    }

    /// <summary>Reads <c>/json/state</c> into the cache of <paramref name="device"/>.</summary>
    public async Task RefreshStateAsync(WledDevice device, CancellationToken ct = default)
    {
        JsonNode state = await GetJsonAsync(device.Host, "/json/state", ct);
        ApplyState(device, state);
    }

    /// <summary>Loads effect, palette and preset names. Older firmware without presets.json is tolerated.</summary>
    public async Task RefreshListsAsync(WledDevice device, CancellationToken ct = default)
    {
        device.Effects = ToStringList(await GetJsonAsync(device.Host, "/json/eff", ct));
        device.Palettes = ToStringList(await GetJsonAsync(device.Host, "/json/pal", ct));

        try
        {
            device.Presets = ParsePresets(await GetJsonAsync(device.Host, "/presets.json", ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            device.Presets = [];
        }
    }

    /// <summary>
    /// Posts a partial state to <c>/json/state</c>. <c>"v": true</c> makes WLED answer with the
    /// full resulting state, which goes straight into the cache.
    /// </summary>
    public async Task PostStateAsync(WledDevice device, JsonObject patch, CancellationToken ct = default)
    {
        patch["v"] = true;
        using StringContent content = new(patch.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(BuildUri(device.Host, "/json/state"), content, ct);
        response.EnsureSuccessStatusCode();

        JsonNode? state = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if (state?["on"] != null)
            ApplyState(device, state);
    }

    private async Task<JsonNode> GetJsonAsync(string host, string path, CancellationToken ct)
    {
        string body = await _http.GetStringAsync(BuildUri(host, path), ct);
        return JsonNode.Parse(body) ?? throw new InvalidOperationException($"Empty response from {path}.");
    }

    private static Uri BuildUri(string host, string path)
    {
        string trimmed = host.Trim().TrimEnd('/');
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "http://" + trimmed;
        return new Uri(trimmed + path);
    }

    private static void ApplyState(WledDevice device, JsonNode state)
    {
        device.On = state["on"]?.GetValue<bool>() ?? device.On;
        device.Brightness = GetInt(state["bri"], device.Brightness);
        device.Preset = GetInt(state["ps"], device.Preset);

        // The main segment is what the global (segment-less) API calls act on.
        int mainSeg = GetInt(state["mainseg"], 0);
        JsonNode? segment = (state["seg"] as JsonArray)?
            .FirstOrDefault(s => GetInt(s?["id"], -1) == mainSeg);
        if (segment != null)
        {
            device.Effect = GetInt(segment["fx"], device.Effect);
            device.Palette = GetInt(segment["pal"], device.Palette);
            device.Speed = GetInt(segment["sx"], device.Speed);
            device.Intensity = GetInt(segment["ix"], device.Intensity);
        }

        device.Online = true;
    }

    private static int GetInt(JsonNode? node, int fallback) =>
        node is JsonValue value && value.TryGetValue(out int result) ? result : fallback;

    private static List<string> ToStringList(JsonNode node) =>
        (node as JsonArray)?.Select(n => n?.GetValue<string>() ?? string.Empty).ToList() ?? [];

    private static List<KeyValuePair<int, string>> ParsePresets(JsonNode node)
    {
        List<KeyValuePair<int, string>> presets = [];
        if (node is not JsonObject obj)
            return presets;

        foreach ((string key, JsonNode? value) in obj)
        {
            // Id 0 is WLED's placeholder entry; empty objects are deleted slots.
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || (id == 0))
                continue;
            if (value is not JsonObject preset || (preset.Count == 0))
                continue;

            string name = preset["n"]?.GetValue<string>() ?? string.Empty;
            presets.Add(new(id, string.IsNullOrWhiteSpace(name) ? $"Preset {id}" : name));
        }

        presets.Sort((a, b) => a.Key.CompareTo(b.Key));
        return presets;
    }

    public void Dispose() => _http.Dispose();
}
