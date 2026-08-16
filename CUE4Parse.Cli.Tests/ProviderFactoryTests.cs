using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

public class ProviderFactoryTests
{
    [Theory]
    [InlineData("0x0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
    [InlineData("0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
    public void ParseAesKeyAcceptsHexWithAndWithoutPrefix(string value)
        => Assert.NotNull(ProviderFactory.ParseAesKey(value));

    [Fact]
    public void ParseAesKeyThrowsAesErrorForMalformedKey()
    {
        var ex = Assert.Throws<CliException>(() => ProviderFactory.ParseAesKey("nonsense"));
        Assert.Equal(ExitCode.AesKey, ex.ExitCode);
        Assert.Equal("BAD_AES_KEY", ex.ErrorCode);
    }

    [Theory]
    [InlineData("11111111222222223333333344444444")]
    [InlineData("11111111-2222-2222-3333-333344444444")]
    [InlineData("{11111111-2222-2222-3333-333344444444}")]
    public void ParseAesGuidAcceptsBareAndDashedForms(string value)
        => Assert.Equal(
            ProviderFactory.ParseAesGuid("11111111222222223333333344444444"),
            ProviderFactory.ParseAesGuid(value));

    [Fact]
    public void ParseAesGuidThrowsAesErrorForMalformedGuid()
    {
        // FGuid has no TryParse and its ctor throws on anything but 32 hex chars.
        var ex = Assert.Throws<CliException>(() => ProviderFactory.ParseAesGuid("not-a-guid"));
        Assert.Equal(ExitCode.AesKey, ex.ExitCode);
        Assert.Equal("BAD_AES_GUID", ex.ErrorCode);
    }

    [Fact]
    public void CreateThrowsMountErrorWhenPaksDirectoryDoesNotExist()
    {
        var profile = new ResolvedProfile(
            Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N")),
            EGame.GAME_UE5_6, null, null, new Dictionary<string, string>());

        var ex = Assert.Throws<CliException>(() => ProviderFactory.Create(profile));
        Assert.Equal(ExitCode.Mount, ex.ExitCode);
        Assert.Equal("PAKS_DIR_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public void CreateThrowsMappingsErrorWhenMappingsFileIsMissing()
    {
        var paks = Directory.CreateTempSubdirectory().FullName;
        var profile = new ResolvedProfile(
            paks, EGame.GAME_UE5_6,
            Path.Combine(paks, "nope.usmap"), null, new Dictionary<string, string>());

        var ex = Assert.Throws<CliException>(() => ProviderFactory.Create(profile));
        Assert.Equal(ExitCode.Mappings, ex.ExitCode);
        Assert.Equal("MAPPINGS_NOT_FOUND", ex.ErrorCode);
    }
}
