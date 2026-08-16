using CUE4Parse.Cli.Output;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Services;

public static class GameParser
{
    /// <summary>Parses an <see cref="EGame"/> from an enum name ("GAME_UE5_6") or shorthand ("5.6").</summary>
    public static EGame Parse(string value)
    {
        var trimmed = value.Trim();

        // Enum.TryParse accepts bare integers and comma-separated lists, so every
        // candidate is validated against the declared members. IsDefined alone is not
        // enough: "67108864" is the underlying value of GAME_UE4_0 and would sail
        // through, so bare decimal integers are rejected outright — nobody names a
        // game that way, and "5" silently meaning something is worse than an error.
        var isBareInteger = trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit);

        if (!isBareInteger &&
            Enum.TryParse<EGame>(trimmed, ignoreCase: true, out var direct) &&
            Enum.IsDefined(direct))
        {
            return direct;
        }

        // Shorthand: "5.6" -> "GAME_UE5_6"
        var parts = trimmed.Split('.');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor) &&
            Enum.TryParse<EGame>($"GAME_UE{major}_{minor}", ignoreCase: true, out var shorthand) &&
            Enum.IsDefined(shorthand))
        {
            return shorthand;
        }

        throw new CliException(
            ExitCode.Config,
            "UNKNOWN_GAME",
            $"Unknown game version '{value}'. Use an EGame name such as 'GAME_UE5_6' or shorthand such as '5.6'.");
    }
}
