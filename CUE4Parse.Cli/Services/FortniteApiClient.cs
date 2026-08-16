using CUE4Parse.Cli.Output;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Services;

public sealed class FortniteApiClient(HttpClient http) : IFortniteApiClient
{
    private const string AesEndpoint = "https://fortnite-api.com/v2/aes";
    private const string MappingsEndpoint = "https://fortnite-api.com/v2/mappings";

    public async Task<AesKeys> GetAesKeysAsync(CancellationToken ct)
    {
        var data = (await GetJsonAsync(AesEndpoint, ct))["data"]
            ?? throw new CliException(ExitCode.Error, "BAD_API_RESPONSE", "AES response had no 'data' field.");

        var dynamic = new Dictionary<string, string>();
        foreach (var entry in data["dynamicKeys"] as JArray ?? [])
        {
            var guid = entry["guid"]?.Value<string>();
            var key = entry["key"]?.Value<string>();
            if (guid is not null && key is not null) dynamic[guid] = key;
        }

        return new AesKeys(data["mainKey"]?.Value<string>() ?? string.Empty, dynamic);
    }

    public async Task<byte[]> GetMappingsAsync(CancellationToken ct)
    {
        var files = (await GetJsonAsync(MappingsEndpoint, ct))["data"] as JArray;

        var url = files?.FirstOrDefault()?["url"]?.Value<string>()
            ?? throw new CliException(
                ExitCode.Mappings, "NO_MAPPINGS_AVAILABLE",
                "The mappings API listed no downloadable files.");

        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCode.Error, "API_UNAVAILABLE",
                $"Mappings download failed with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<JObject> GetJsonAsync(string url, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CliException(ExitCode.Error, "API_UNAVAILABLE", $"Could not reach {url}: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCode.Error, "API_UNAVAILABLE",
                $"{url} returned HTTP {(int)response.StatusCode}.");
        }

        return JObject.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
