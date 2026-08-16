using System.IO;
using CUE4Parse.Cli.Output;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class JsonOutputTests
{
    [Fact]
    public void WriteErrorProducesParseableEnvelopeWithCodeAndMessage()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteError("AES_KEY_MISSING", "missing key");

        var parsed = JObject.Parse(sw.ToString());
        Assert.Equal("AES_KEY_MISSING", parsed["error"]?["code"]?.Value<string>());
        Assert.Equal("missing key", parsed["error"]?["message"]?.Value<string>());
    }

    [Fact]
    public void WriteErrorEscapesControlCharactersInsteadOfEmittingInvalidJson()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteError("USAGE", "bad value. Must be one of:\n\t'a'\n\t'b'");

        // Must parse: raw newlines/tabs would make this invalid JSON.
        var parsed = JObject.Parse(sw.ToString());
        Assert.Contains("Must be one of", parsed["error"]?["message"]?.Value<string>());
    }

    [Fact]
    public void WriteLineEmitsOneCompactJsonObjectPerLine()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteLine(new { path = "a" });
        output.WriteLine(new { path = "b" });

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("a", JObject.Parse(lines[0])["path"]?.Value<string>());
        Assert.Equal("b", JObject.Parse(lines[1])["path"]?.Value<string>());
    }

    [Fact]
    public void WriteErrorIncludesDetailsWhenSupplied()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteError("AES_KEY_MISSING", "missing", new { missingGuids = new[] { "abc" } });

        var parsed = JObject.Parse(sw.ToString());
        Assert.Equal("abc", parsed["error"]?["details"]?["missingGuids"]?[0]?.Value<string>());
    }
}
