using CUE4Parse;
using Serilog;
using Serilog.Events;

namespace CUE4Parse.Cli.Tests;

public class NaniteWarningTests
{
    private sealed class CapturingSink : Serilog.Core.ILogEventSink
    {
        private readonly Lock _gate = new();
        private readonly List<(LogEventLevel Level, string Message)> _events = [];

        // ExportSession is parallel, so Emit is called from several threads at once.
        public void Emit(LogEvent logEvent)
        {
            lock (_gate) _events.Add((logEvent.Level, logEvent.RenderMessage()));
        }

        public (LogEventLevel Level, string Message)[] Snapshot()
        {
            lock (_gate) return [.. _events];
        }
    }

    [Fact]
    public async Task ExportingANaniteOnlyMeshWithNoNaniteWarnsInsteadOfSilentlyProducingNothing()
    {
        var sink = new CapturingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        CUE4ParseLog.UseLogger(Log.Logger);

        try
        {
            await GltfWriterTests.ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Nanite.uasset");
        }
        finally
        {
            Log.Logger = previous;
            CUE4ParseLog.UseLogger(previous);
        }

        // Matched on the remediation text, not on "Nanite" alone. Two unrelated messages
        // already carry that substring for this asset and would make the check vacuous:
        // MeshExporter logs "...quality ({NaniteFormat})" at Debug, rendering the enum
        // name "NoNanite", and the loader warns about a missing import for the package
        // "/Game/Fixtures/Meshes/SM_Nanite" — whose own name contains it.
        var events = sink.Snapshot();
        var rendered = string.Join("\n", events.Select(e => $"{e.Level}: {e.Message}"));

        Assert.True(
            events.Any(e => e.Level == LogEventLevel.Warning
                            && e.Message.Contains("--nanite nanite-only", StringComparison.Ordinal)),
            $"No warning told the user how to keep the Nanite data. Captured:\n{rendered}");
    }
}
