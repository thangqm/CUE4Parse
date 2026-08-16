using System.Net;
using System.Text;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Tests;

file sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(responder(request));
}

public class FortniteApiClientTests
{
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task GetAesKeysAsyncParsesMainAndDynamicKeys()
    {
        var client = new FortniteApiClient(new HttpClient(new StubHandler(_ => Json("""
        {
          "data": {
            "mainKey": "0xAAAA",
            "dynamicKeys": [
              { "guid": "11111111-1111-1111-1111-111111111111", "key": "0xBBBB" }
            ]
          }
        }
        """))));

        var keys = await client.GetAesKeysAsync(CancellationToken.None);

        Assert.Equal("0xAAAA", keys.MainKey);
        Assert.Equal("0xBBBB", keys.DynamicKeys["11111111-1111-1111-1111-111111111111"]);
    }

    [Fact]
    public async Task GetAesKeysAsyncThrowsWhenTheApiReturnsAnError()
    {
        var client = new FortniteApiClient(new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var ex = await Assert.ThrowsAsync<CliException>(() => client.GetAesKeysAsync(CancellationToken.None));
        Assert.Equal("API_UNAVAILABLE", ex.ErrorCode);
    }

    [Fact]
    public async Task GetMappingsAsyncDownloadsTheFirstListedFile()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var client = new FortniteApiClient(new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsoluteUri.Contains("/v2/mappings")
                ? Json("""{ "data": [ { "url": "https://example.invalid/m.usmap" } ] }""")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));

        Assert.Equal(payload, await client.GetMappingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetMappingsAsyncThrowsWhenTheApiListsNoFiles()
    {
        var client = new FortniteApiClient(new HttpClient(new StubHandler(
            _ => Json("""{ "data": [] }"""))));

        var ex = await Assert.ThrowsAsync<CliException>(() => client.GetMappingsAsync(CancellationToken.None));
        Assert.Equal("NO_MAPPINGS_AVAILABLE", ex.ErrorCode);
    }

    /// <summary>
    /// UpdateCommand writes AesKeys with JsonConvert and CachePaths.ReadCachedKeys
    /// reads it back. A positional record needs the ctor binding to line up, and a
    /// silent failure here would leave 'cue4 update' with no observable effect.
    /// </summary>
    [Fact]
    public void CachedKeysRoundTripThroughTheSameSerializer()
    {
        var original = new AesKeys("0xAAAA", new Dictionary<string, string> { ["11111111222222223333333344444444"] = "0xBBBB" });

        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<AesKeys>(
            Newtonsoft.Json.JsonConvert.SerializeObject(original))!;

        Assert.Equal("0xAAAA", restored.MainKey);
        Assert.Equal("0xBBBB", restored.DynamicKeys["11111111222222223333333344444444"]);
    }
}
