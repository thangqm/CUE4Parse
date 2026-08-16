namespace CUE4Parse.Cli.Tests;

public class SoundExporterTests
{
    /// <summary>
    /// Magic-byte assertions, not "the file is non-empty": the failures worth catching
    /// are picking the wrong chunk, a streaming-offset slip, and a mislabelled format —
    /// all of which produce a plausible-sized file with a wrong header.
    /// </summary>
    /// <remarks>
    /// UE cooks Bink to its own container, whose signature is <c>ABEU</c> — "UEBA"
    /// (Unreal Engine Bink Audio) stored little-endian, not RAD's bare <c>BCF</c>
    /// stream. Its siblings confirm the family rather than a slipped offset:
    /// <c>SW_Format_RADAudio</c> starts <c>ADAR</c> ("RADA") and
    /// <c>SW_Format_Opus</c> starts <c>UEOPUS</c>.
    /// </remarks>
    [Theory]
    [InlineData("SW_Format_BinkAudio", "binka", new byte[] { (byte)'A', (byte)'B', (byte)'E', (byte)'U' })]
    [InlineData("SW_Format_RADAudio", "rada", new byte[] { (byte)'A', (byte)'D', (byte)'A', (byte)'R' })]
    [InlineData("SW_Format_Opus", "opus", new byte[] { (byte)'U', (byte)'E', (byte)'O', (byte)'P', (byte)'U', (byte)'S' })]
    [InlineData("SW_Format_PlatformSpecific", "ogg", new byte[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S' })]
    public async Task ExportedAudioCarriesTheExpectedContainerMagic(string asset, string extension, byte[] magic)
    {
        var output = await GltfWriterTests.ExportAsync(
            $"CUE4ParseFixtures/Content/Fixtures/Audio/{asset}.uasset");

        var file = Directory
            .GetFiles(output.FullName, $"*.{extension}", SearchOption.AllDirectories)
            .Single();

        var head = new byte[magic.Length];
        await using (var stream = File.OpenRead(file)) _ = await stream.ReadAsync(head, TestContext.Current.CancellationToken);

        Assert.Equal(magic, head);
    }

    [Fact]
    public async Task PcmSoundsComeOutAsRiffWave()
    {
        var output = await GltfWriterTests.ExportAsync(
            "CUE4ParseFixtures/Content/Fixtures/Audio/SW_Format_ADPCM.uasset");

        var file = Directory.GetFiles(output.FullName, "*.wav", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(output.FullName, "*.adpcm", SearchOption.AllDirectories))
            .Single();

        var head = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        Assert.True(head.Length > 12);
        Assert.Equal("RIFF"u8.ToArray(), head[..4]);
    }

    /// <summary>
    /// The streaming path concatenates <c>RunningPlatformData.Chunks</c> by
    /// <c>AudioDataSize</c> rather than by buffer length, so an offset slip there
    /// produces a file that is still RIFF and still plausibly sized. Checking the
    /// declared RIFF length against the bytes on disk is what actually catches it.
    /// </summary>
    [Fact]
    public async Task StreamedSoundsConcatenateTheirChunksToADeclaredRiffLength()
    {
        var output = await GltfWriterTests.ExportAsync(
            "CUE4ParseFixtures/Content/Fixtures/Audio/SW_Streaming.uasset");

        var file = Directory.GetFiles(output.FullName, "*.wav", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal("WAVE"u8.ToArray(), bytes[8..12]);
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
    }

    [Fact]
    public async Task SoundCuesAreNotDispatchedToTheSoundExporter()
    {
        // A USoundCue is a node graph, not audio data. It keeps falling through to
        // `dump`, and export must report it as skipped rather than writing a bogus file.
        var output = await GltfWriterTests.ExportAsync(
            "CUE4ParseFixtures/Content/Fixtures/Audio/SC_Fixture.uasset");

        Assert.Empty(Directory.GetFiles(output.FullName, "*.wem", SearchOption.AllDirectories));
    }
}
