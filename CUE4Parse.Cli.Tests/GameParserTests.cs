using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

public class GameParserTests
{
    [Theory]
    [InlineData("GAME_UE5_6")]
    [InlineData("game_ue5_6")]
    public void ParseAcceptsEnumNameCaseInsensitively(string value)
        => Assert.Equal(EGame.GAME_UE5_6, GameParser.Parse(value));

    [Theory]
    [InlineData("5.6", EGame.GAME_UE5_6)]
    [InlineData("4.27", EGame.GAME_UE4_27)]
    public void ParseAcceptsEngineVersionShorthand(string value, EGame expected)
        => Assert.Equal(expected, GameParser.Parse(value));

    [Theory]
    [InlineData("not-a-game")]
    [InlineData("5")]          // Enum.TryParse happily returns (EGame)5 — a nonsense version.
    [InlineData("67108864")]   // Same trap with a plausible-looking underlying value.
    [InlineData("GAME_UE5_6, GAME_UE4_27")] // Enum.TryParse accepts comma-separated lists.
    public void ParseThrowsConfigErrorForUnknownValue(string value)
    {
        var ex = Assert.Throws<CliException>(() => GameParser.Parse(value));
        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("UNKNOWN_GAME", ex.ErrorCode);
    }
}
