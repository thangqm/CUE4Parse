using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.Cli.Tests;

public class MeshExportContextTests
{
    [Fact]
    public void ContextCarriesSaveDirectoryAndOptionsTogether()
    {
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2);
        var context = new MeshExportContext(
            "MESH_X",
            "Game/Chars/MESH_X.MESH_X",
            "Game/Chars",
            options);

        Assert.Equal("MESH_X", context.ObjectName);
        Assert.Equal("Game/Chars", context.SaveDirectory);
        Assert.Same(options, context.Options);
        Assert.Null(context.MaterialPaths);
    }
}
